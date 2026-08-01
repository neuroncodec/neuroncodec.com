namespace NeuronCodec.Web.Data.Entities;

public class AdminUser
{
    public int Id { get; set; }

    public string Username { get; set; } = "";

    /// <summary>PBKDF2-HMACSHA256, encoded by <c>PasswordHasher</c>.</summary>
    public string PasswordHash { get; set; } = "";

    /// <summary>
    /// True while the account still carries the password seeded from the environment.
    /// Cleared on the first successful password change; the admin area forces the change until then.
    /// </summary>
    public bool MustChangePassword { get; set; }

    /// <summary>Base32 TOTP secret, protected at rest with ASP.NET Data Protection.</summary>
    public string? TotpSecretProtected { get; set; }
    public bool TwoFactorEnabled { get; set; }

    public List<RecoveryCode> RecoveryCodes { get; set; } = [];

    /// <summary>
    /// Bumped on password change and on 2FA changes; embedded in the auth cookie so existing
    /// sessions elsewhere stop validating.
    /// </summary>
    public int SecurityStamp { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime? LastLoginAt { get; set; }

    /// <summary>Last TOTP step accepted, so a code cannot be replayed within its window.</summary>
    public long LastTotpStep { get; set; }

    public int FailedLoginCount { get; set; }
    public DateTime? LockedOutUntil { get; set; }
}

public class RecoveryCode
{
    public int Id { get; set; }
    public int AdminUserId { get; set; }
    public AdminUser AdminUser { get; set; } = null!;

    /// <summary>Hashed with the same PBKDF2 scheme as passwords; codes are single-use.</summary>
    public string CodeHash { get; set; } = "";
    public DateTime? UsedAt { get; set; }
}
