using System.Security.Claims;
using System.Security.Cryptography;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.EntityFrameworkCore;
using NeuronCodec.Web.Data;
using NeuronCodec.Web.Data.Entities;

namespace NeuronCodec.Web.Services;

public enum LoginOutcome
{
    Success,
    InvalidCredentials,
    TwoFactorRequired,
    LockedOut,
}

public record LoginResult(LoginOutcome Outcome, AdminUser? User = null, TimeSpan? LockoutRemaining = null);

/// <summary>
/// Authentication for the single admin account: password check, TOTP second factor, recovery
/// codes, and the cookie sign-in itself.
/// </summary>
public class AdminAuth(
    AppDbContext db,
    IDataProtectionProvider dataProtection,
    IHttpContextAccessor accessor,
    TimeProvider clock,
    ILogger<AdminAuth> logger)
{
    public const string AuthScheme = CookieAuthenticationDefaults.AuthenticationScheme;
    public const string SecurityStampClaim = "ncodec:stamp";

    /// <summary>Marks a half-signed-in session that has passed the password but not yet the TOTP step.</summary>
    public const string TwoFactorPendingScheme = "NeuronCodec.TwoFactorPending";

    private const int MaxFailedLogins = 8;
    private static readonly TimeSpan LockoutDuration = TimeSpan.FromMinutes(15);
    private const string ProtectorPurpose = "NeuronCodec.TotpSecret.v1";

    private IDataProtector Protector => dataProtection.CreateProtector(ProtectorPurpose);

    public Task<AdminUser?> GetAdminAsync(CancellationToken ct = default) =>
        db.AdminUsers.Include(u => u.RecoveryCodes).FirstOrDefaultAsync(ct);

    /// <summary>Verifies the password, applying and enforcing lockout on repeated failures.</summary>
    public async Task<LoginResult> VerifyPasswordAsync(string username, string password, CancellationToken ct = default)
    {
        var user = await db.AdminUsers
            .Include(u => u.RecoveryCodes)
            .FirstOrDefaultAsync(u => u.Username == username, ct);

        var now = clock.GetUtcNow().UtcDateTime;

        if (user is null)
        {
            // Burn comparable time on an unknown username so the response does not leak whether
            // the account exists.
            PasswordHasher.Verify(password, PasswordHasher.Hash("decoy"));
            return new LoginResult(LoginOutcome.InvalidCredentials);
        }

        if (user.LockedOutUntil is { } until && until > now)
            return new LoginResult(LoginOutcome.LockedOut, LockoutRemaining: until - now);

        if (!PasswordHasher.Verify(password, user.PasswordHash))
        {
            user.FailedLoginCount++;
            if (user.FailedLoginCount >= MaxFailedLogins)
            {
                user.LockedOutUntil = now + LockoutDuration;
                user.FailedLoginCount = 0;
                logger.LogWarning("Admin account locked out until {Until} after repeated failures.", user.LockedOutUntil);
            }

            await db.SaveChangesAsync(ct);
            return new LoginResult(LoginOutcome.InvalidCredentials);
        }

        user.FailedLoginCount = 0;
        user.LockedOutUntil = null;
        await db.SaveChangesAsync(ct);

        return user.TwoFactorEnabled
            ? new LoginResult(LoginOutcome.TwoFactorRequired, user)
            : new LoginResult(LoginOutcome.Success, user);
    }

    /// <summary>Checks a TOTP code against the stored secret, rejecting replays of a used step.</summary>
    public async Task<bool> VerifyTotpAsync(AdminUser user, string? code, CancellationToken ct = default)
    {
        var secret = UnprotectSecret(user.TotpSecretProtected);
        if (secret is null) return false;

        if (!Totp.Validate(secret, code, clock.GetUtcNow(), user.LastTotpStep, out var matchedStep))
            return false;

        user.LastTotpStep = matchedStep;
        await db.SaveChangesAsync(ct);
        return true;
    }

    /// <summary>Consumes a single-use recovery code.</summary>
    public async Task<bool> RedeemRecoveryCodeAsync(AdminUser user, string? code, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(code)) return false;

        var normalized = NormalizeRecoveryCode(code);
        var codes = await db.RecoveryCodes
            .Where(r => r.AdminUserId == user.Id && r.UsedAt == null)
            .ToListAsync(ct);

        var match = codes.FirstOrDefault(r => PasswordHasher.Verify(normalized, r.CodeHash));
        if (match is null) return false;

        match.UsedAt = clock.GetUtcNow().UtcDateTime;
        await db.SaveChangesAsync(ct);
        logger.LogWarning("A recovery code was used to sign in. {Remaining} remain.", codes.Count - 1);
        return true;
    }

    public async Task SignInAsync(AdminUser user, bool persistent, CancellationToken ct = default)
    {
        var context = accessor.HttpContext ?? throw new InvalidOperationException("No HTTP context.");

        user.LastLoginAt = clock.GetUtcNow().UtcDateTime;
        await db.SaveChangesAsync(ct);

        await context.SignOutAsync(TwoFactorPendingScheme);

        var identity = new ClaimsIdentity(
        [
            new Claim(ClaimTypes.NameIdentifier, user.Id.ToString()),
            new Claim(ClaimTypes.Name, user.Username),
            new Claim(SecurityStampClaim, user.SecurityStamp.ToString()),
        ], AuthScheme);

        await context.SignInAsync(AuthScheme, new ClaimsPrincipal(identity), new AuthenticationProperties
        {
            IsPersistent = persistent,
            ExpiresUtc = clock.GetUtcNow() + (persistent ? TimeSpan.FromDays(14) : TimeSpan.FromHours(8)),
        });
    }

    /// <summary>Issues the short-lived cookie that carries a user between the password and TOTP steps.</summary>
    public async Task StartTwoFactorAsync(AdminUser user, bool persistent)
    {
        var context = accessor.HttpContext ?? throw new InvalidOperationException("No HTTP context.");

        var identity = new ClaimsIdentity(
        [
            new Claim(ClaimTypes.NameIdentifier, user.Id.ToString()),
            new Claim("ncodec:persist", persistent ? "1" : "0"),
        ], TwoFactorPendingScheme);

        await context.SignInAsync(TwoFactorPendingScheme, new ClaimsPrincipal(identity),
            new AuthenticationProperties
            {
                IsPersistent = false,
                ExpiresUtc = clock.GetUtcNow() + TimeSpan.FromMinutes(10),
            });
    }

    /// <summary>Reads back the half-signed-in user, or null when that step has expired.</summary>
    public async Task<(AdminUser? User, bool Persistent)> GetTwoFactorPendingAsync(CancellationToken ct = default)
    {
        var context = accessor.HttpContext;
        if (context is null) return (null, false);

        var result = await context.AuthenticateAsync(TwoFactorPendingScheme);
        if (!result.Succeeded) return (null, false);

        var idClaim = result.Principal.FindFirstValue(ClaimTypes.NameIdentifier);
        if (!int.TryParse(idClaim, out var id)) return (null, false);

        var user = await db.AdminUsers.Include(u => u.RecoveryCodes).FirstOrDefaultAsync(u => u.Id == id, ct);
        var persistent = result.Principal.FindFirstValue("ncodec:persist") == "1";
        return (user, persistent);
    }

    public async Task SignOutAsync()
    {
        var context = accessor.HttpContext;
        if (context is null) return;

        await context.SignOutAsync(AuthScheme);
        await context.SignOutAsync(TwoFactorPendingScheme);
    }

    // ── Password ─────────────────────────────────────────────────────────────

    public async Task ChangePasswordAsync(AdminUser user, string newPassword, CancellationToken ct = default)
    {
        user.PasswordHash = PasswordHasher.Hash(newPassword);
        user.MustChangePassword = false;
        user.SecurityStamp++;
        await db.SaveChangesAsync(ct);
    }

    /// <summary>Rejects passwords that would not survive a basic online guessing attempt.</summary>
    public static string? ValidatePassword(string? password)
    {
        if (string.IsNullOrWhiteSpace(password)) return "Enter a password.";
        if (password.Length < 12) return "Use at least 12 characters.";
        if (password.Length > 256) return "That password is too long.";

        var classes = 0;
        if (password.Any(char.IsLower)) classes++;
        if (password.Any(char.IsUpper)) classes++;
        if (password.Any(char.IsDigit)) classes++;
        if (password.Any(c => !char.IsLetterOrDigit(c))) classes++;

        return classes < 3
            ? "Use at least three of: lowercase, uppercase, digits, symbols."
            : null;
    }

    // ── Two-factor enrolment ─────────────────────────────────────────────────

    public string GenerateTotpSecret() => Totp.GenerateSecret();

    public string ProtectSecret(string secret) => Protector.Protect(secret);

    public string? UnprotectSecret(string? protectedSecret)
    {
        if (string.IsNullOrWhiteSpace(protectedSecret)) return null;

        try
        {
            return Protector.Unprotect(protectedSecret);
        }
        catch (CryptographicException ex)
        {
            logger.LogError(ex, "The stored TOTP secret could not be unprotected. The key ring may have been lost.");
            return null;
        }
    }

    /// <summary>
    /// Confirms enrolment: stores the protected secret, turns 2FA on, and replaces any existing
    /// recovery codes with a fresh set, returned in plaintext for one-time display.
    /// </summary>
    public async Task<IReadOnlyList<string>> EnableTwoFactorAsync(AdminUser user, string secret, CancellationToken ct = default)
    {
        user.TotpSecretProtected = ProtectSecret(secret);
        user.TwoFactorEnabled = true;
        user.SecurityStamp++;
        await db.SaveChangesAsync(ct);
        return await ReplaceRecoveryCodesAsync(user, ct);
    }

    public async Task DisableTwoFactorAsync(AdminUser user, CancellationToken ct = default)
    {
        user.TwoFactorEnabled = false;
        user.TotpSecretProtected = null;
        user.LastTotpStep = 0;
        user.SecurityStamp++;

        var codes = await db.RecoveryCodes.Where(r => r.AdminUserId == user.Id).ToListAsync(ct);
        db.RecoveryCodes.RemoveRange(codes);
        await db.SaveChangesAsync(ct);
    }

    public async Task<IReadOnlyList<string>> ReplaceRecoveryCodesAsync(AdminUser user, CancellationToken ct = default)
    {
        var existing = await db.RecoveryCodes.Where(r => r.AdminUserId == user.Id).ToListAsync(ct);
        db.RecoveryCodes.RemoveRange(existing);

        var plaintext = Enumerable.Range(0, 10).Select(_ => GenerateRecoveryCode()).ToList();
        foreach (var code in plaintext)
        {
            db.RecoveryCodes.Add(new RecoveryCode
            {
                AdminUserId = user.Id,
                CodeHash = PasswordHasher.Hash(NormalizeRecoveryCode(code)),
            });
        }

        await db.SaveChangesAsync(ct);
        return plaintext;
    }

    public Task<int> CountUnusedRecoveryCodesAsync(int adminUserId, CancellationToken ct = default) =>
        db.RecoveryCodes.CountAsync(r => r.AdminUserId == adminUserId && r.UsedAt == null, ct);

    /// <summary>Ten characters from an unambiguous alphabet, grouped as xxxxx-xxxxx.</summary>
    private static string GenerateRecoveryCode()
    {
        const string alphabet = "abcdefghjkmnpqrstuvwxyz23456789";
        var chars = new char[10];
        for (var i = 0; i < chars.Length; i++)
            chars[i] = alphabet[RandomNumberGenerator.GetInt32(alphabet.Length)];

        return new string(chars[..5]) + "-" + new string(chars[5..]);
    }

    private static string NormalizeRecoveryCode(string code) =>
        new(code.Where(char.IsLetterOrDigit).Select(char.ToLowerInvariant).ToArray());
}
