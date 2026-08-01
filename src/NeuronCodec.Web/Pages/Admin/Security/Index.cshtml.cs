using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using NeuronCodec.Web.Data.Entities;
using NeuronCodec.Web.Services;
using QRCoder;

namespace NeuronCodec.Web.Pages.Admin.Security;

public class IndexModel(AdminAuth auth, SiteSettings settings, ILogger<IndexModel> logger) : PageModel
{
    /// <summary>
    /// Holds the candidate secret between showing the QR code and confirming the first code.
    /// Session-scoped rather than a form field so it is never replayed from the browser.
    /// </summary>
    private const string PendingSecretKey = "ncodec.totp.pending";

    public AdminUser? Admin { get; private set; }
    public int RecoveryCodesRemaining { get; private set; }

    public string? PendingSecret { get; private set; }
    public string? PendingSecretDisplay { get; private set; }
    public string? QrCodeSvg { get; private set; }

    /// <summary>Shown once, immediately after enrolling or regenerating.</summary>
    public IReadOnlyList<string>? NewRecoveryCodes { get; private set; }

    [BindProperty]
    public string? Code { get; set; }

    [TempData]
    public string? RecoveryCodesPayload { get; set; }

    public async Task<IActionResult> OnGetAsync(bool? setup, CancellationToken ct)
    {
        Admin = await auth.GetAdminAsync(ct);
        if (Admin is null) return NotFound();

        RecoveryCodesRemaining = Admin.RecoveryCodes.Count(r => r.UsedAt is null);

        if (!string.IsNullOrEmpty(RecoveryCodesPayload))
            NewRecoveryCodes = RecoveryCodesPayload.Split('\n', StringSplitOptions.RemoveEmptyEntries);

        if (setup == true && !Admin.TwoFactorEnabled)
            BeginEnrolment(Admin);

        return Page();
    }

    public async Task<IActionResult> OnPostEnableAsync(CancellationToken ct)
    {
        Admin = await auth.GetAdminAsync(ct);
        if (Admin is null) return NotFound();

        var secret = HttpContext.Session.GetString(PendingSecretKey);
        if (string.IsNullOrWhiteSpace(secret))
        {
            TempData["Error"] = "That enrolment expired. Start again.";
            return RedirectToPage(new { setup = true });
        }

        // Confirm the app is actually generating matching codes before locking 2FA on.
        if (!Totp.Validate(secret, Code, TimeProvider.System.GetUtcNow(), lastUsedStep: 0, out _))
        {
            TempData["Error"] = "That code did not match. Check the time on your device and try again.";
            RestoreEnrolment(Admin, secret);
            return Page();
        }

        var codes = await auth.EnableTwoFactorAsync(Admin, secret, ct);
        HttpContext.Session.Remove(PendingSecretKey);

        // The stamp changed, so this session's own cookie must be reissued.
        await auth.SignInAsync(Admin, persistent: false, ct);

        RecoveryCodesPayload = string.Join('\n', codes);
        TempData["Success"] = "Two-factor authentication is on. Save your recovery codes now.";
        logger.LogWarning("Two-factor authentication enabled for the admin account.");

        return RedirectToPage();
    }

    public async Task<IActionResult> OnPostDisableAsync(CancellationToken ct)
    {
        Admin = await auth.GetAdminAsync(ct);
        if (Admin is null) return NotFound();

        // Turning 2FA off is exactly what an attacker on a stolen session would do, so it needs
        // a current code first.
        if (!await auth.VerifyTotpAsync(Admin, Code, ct))
        {
            TempData["Error"] = "Enter a current code from your authenticator app to turn two-factor off.";
            return RedirectToPage();
        }

        await auth.DisableTwoFactorAsync(Admin, ct);
        await auth.SignInAsync(Admin, persistent: false, ct);

        TempData["Success"] = "Two-factor authentication is off.";
        logger.LogWarning("Two-factor authentication disabled for the admin account.");
        return RedirectToPage();
    }

    public async Task<IActionResult> OnPostRegenerateCodesAsync(CancellationToken ct)
    {
        Admin = await auth.GetAdminAsync(ct);
        if (Admin is null) return NotFound();

        if (!Admin.TwoFactorEnabled)
        {
            TempData["Error"] = "Turn two-factor authentication on first.";
            return RedirectToPage();
        }

        if (!await auth.VerifyTotpAsync(Admin, Code, ct))
        {
            TempData["Error"] = "Enter a current code from your authenticator app to generate new codes.";
            return RedirectToPage();
        }

        var codes = await auth.ReplaceRecoveryCodesAsync(Admin, ct);
        RecoveryCodesPayload = string.Join('\n', codes);
        TempData["Success"] = "New recovery codes generated. The previous set no longer works.";
        return RedirectToPage();
    }

    private void BeginEnrolment(AdminUser admin)
    {
        var secret = auth.GenerateTotpSecret();
        HttpContext.Session.SetString(PendingSecretKey, secret);
        RestoreEnrolment(admin, secret);
    }

    private void RestoreEnrolment(AdminUser admin, string secret)
    {
        PendingSecret = secret;
        PendingSecretDisplay = Totp.FormatForDisplay(secret);

        var uri = Totp.BuildUri(settings.SiteName, admin.Username, secret);

        using var generator = new QRCodeGenerator();
        using var data = generator.CreateQrCode(uri, QRCodeGenerator.ECCLevel.Q);
        QrCodeSvg = new SvgQRCode(data).GetGraphic(4, "#201e1d", "#ffffff", drawQuietZones: true);
    }
}
