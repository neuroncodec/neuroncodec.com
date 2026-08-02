using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using NeuronCodec.Web.Data.Entities;
using NeuronCodec.Web.Services;

namespace NeuronCodec.Web.Pages.Admin.Security;

public class RecoveryCodesModel(AdminAuth auth) : PageModel
{
    public AdminUser? Admin { get; private set; }
    public int Remaining { get; private set; }
    public int Total { get; private set; }

    /// <summary>Plaintext codes, shown once immediately after they are generated.</summary>
    public IReadOnlyList<string>? NewCodes { get; private set; }

    [BindProperty]
    public string? Code { get; set; }

    [TempData]
    public string? RecoveryCodes { get; set; }

    public async Task<IActionResult> OnGetAsync(CancellationToken ct)
    {
        SetViewData();

        Admin = await auth.GetAdminAsync(ct);
        if (Admin is null) return NotFound();

        Remaining = Admin.RecoveryCodes.Count(r => r.UsedAt is null);
        Total = Admin.RecoveryCodes.Count;

        if (!string.IsNullOrEmpty(RecoveryCodes))
            NewCodes = RecoveryCodes.Split('\n', StringSplitOptions.RemoveEmptyEntries);

        return Page();
    }

    public async Task<IActionResult> OnPostAsync(CancellationToken ct)
    {
        SetViewData();

        var admin = await auth.GetAdminAsync(ct);
        if (admin is null) return NotFound();

        if (!admin.TwoFactorEnabled)
        {
            TempData["Error"] = "Turn two-factor authentication on first.";
            return RedirectToPage();
        }

        if (!await auth.VerifyTotpAsync(admin, Code, ct))
        {
            TempData["Error"] = "Enter a current code from your authenticator app to generate new codes.";
            return RedirectToPage();
        }

        var codes = await auth.ReplaceRecoveryCodesAsync(admin, ct);
        RecoveryCodes = string.Join('\n', codes);
        TempData["Success"] = "New recovery codes generated. The previous set no longer works.";
        return RedirectToPage();
    }

    private void SetViewData()
    {
        ViewData["AdminSection"] = "security";
        ViewData["AdminPage"] = "security-recovery";
    }
}
