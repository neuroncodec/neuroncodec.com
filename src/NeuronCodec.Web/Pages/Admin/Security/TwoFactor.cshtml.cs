using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using NeuronCodec.Web.Data.Entities;
using NeuronCodec.Web.Services;
using QRCoder;

namespace NeuronCodec.Web.Pages.Admin.Security;

public class TwoFactorModel(AdminAuth auth, SiteSettings settings, TimeProvider clock, ILogger<TwoFactorModel> logger)
    : PageModel
{
    /// <summary>
    /// Holds the candidate secret between showing the QR code and confirming the first code.
    /// Session-scoped rather than a form field, so it is never replayed from the browser.
    /// </summary>
    private const string PendingSecretKey = "ncodec.totp.pending";

    public AdminUser? Admin { get; private set; }

    public string? PendingSecretDisplay { get; private set; }
    public string? QrCodeSvg { get; private set; }
    public bool IsEnrolling { get; private set; }

    [BindProperty]
    public string? Code { get; set; }

    public async Task<IActionResult> OnGetAsync(bool? setup, CancellationToken ct)
    {
        SetViewData();

        Admin = await auth.GetAdminAsync(ct);
        if (Admin is null) return NotFound();

        if (setup == true && !Admin.TwoFactorEnabled) BeginEnrolment(Admin);
        return Page();
    }

    public async Task<IActionResult> OnPostEnableAsync(CancellationToken ct)
    {
        SetViewData();

        Admin = await auth.GetAdminAsync(ct);
        if (Admin is null) return NotFound();

        var secret = HttpContext.Session.GetString(PendingSecretKey);
        if (string.IsNullOrWhiteSpace(secret))
        {
            TempData["Error"] = "That enrolment expired. Start again.";
            return RedirectToPage(new { setup = true });
        }

        // Confirm the app is generating matching codes before locking 2FA on.
        if (!Totp.Validate(secret, Code, clock.GetUtcNow(), lastUsedStep: 0, out _))
        {
            TempData["Error"] = "That code did not match. Check the time on your device and try again.";
            RestoreEnrolment(Admin, secret);
            return Page();
        }

        var codes = await auth.EnableTwoFactorAsync(Admin, secret, ct);
        HttpContext.Session.Remove(PendingSecretKey);

        // The security stamp changed, so this session's own cookie must be reissued.
        await auth.SignInAsync(Admin, persistent: false, ct);

        TempData["RecoveryCodes"] = string.Join('\n', codes);
        TempData["Success"] = "Two-factor authentication is on. Save your recovery codes now.";
        logger.LogWarning("Two-factor authentication enabled for the admin account.");

        return RedirectToPage("/Admin/Security/RecoveryCodes");
    }

    public async Task<IActionResult> OnPostDisableAsync(CancellationToken ct)
    {
        SetViewData();

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

    private void BeginEnrolment(AdminUser admin)
    {
        var secret = auth.GenerateTotpSecret();
        HttpContext.Session.SetString(PendingSecretKey, secret);
        RestoreEnrolment(admin, secret);
    }

    private void RestoreEnrolment(AdminUser admin, string secret)
    {
        IsEnrolling = true;
        PendingSecretDisplay = Totp.FormatForDisplay(secret);

        var uri = Totp.BuildUri(settings.SiteName, admin.Username, secret);

        using var generator = new QRCodeGenerator();
        using var data = generator.CreateQrCode(uri, QRCodeGenerator.ECCLevel.Q);
        QrCodeSvg = new SvgQRCode(data).GetGraphic(4, "#201e1d", "#ffffff", drawQuietZones: true);
    }

    private void SetViewData()
    {
        ViewData["AdminSection"] = "security";
        ViewData["AdminPage"] = "security-2fa";
    }
}
