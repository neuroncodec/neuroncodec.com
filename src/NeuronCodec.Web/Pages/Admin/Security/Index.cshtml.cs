using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using NeuronCodec.Web.Data.Entities;
using NeuronCodec.Web.Services;

namespace NeuronCodec.Web.Pages.Admin.Security;

/// <summary>Security landing page: current posture plus links into each task.</summary>
public class IndexModel(AdminAuth auth) : PageModel
{
    public AdminUser? Admin { get; private set; }
    public int RecoveryCodesRemaining { get; private set; }
    public int RecoveryCodesTotal { get; private set; }

    public async Task<IActionResult> OnGetAsync(CancellationToken ct)
    {
        ViewData["AdminSection"] = "security";
        ViewData["AdminPage"] = "security-overview";

        Admin = await auth.GetAdminAsync(ct);
        if (Admin is null) return NotFound();

        RecoveryCodesRemaining = Admin.RecoveryCodes.Count(r => r.UsedAt is null);
        RecoveryCodesTotal = Admin.RecoveryCodes.Count;
        return Page();
    }
}
