using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using NeuronCodec.Web.Services;

namespace NeuronCodec.Web.Pages.Admin;

/// <summary>
/// The second step of sign-in. Reachable only with the short-lived pending cookie issued after a
/// correct password, so it cannot be used to probe codes on its own.
/// </summary>
public class TwoFactorModel(AdminAuth auth, ILogger<TwoFactorModel> logger) : PageModel
{
    [BindProperty]
    public string? Code { get; set; }

    [BindProperty(SupportsGet = true, Name = "recovery")]
    public bool UseRecoveryCode { get; set; }

    public string? ErrorMessage { get; private set; }

    public async Task<IActionResult> OnGetAsync(CancellationToken ct)
    {
        var (user, _) = await auth.GetTwoFactorPendingAsync(ct);
        return user is null ? RedirectToPage("/Admin/Login") : Page();
    }

    public async Task<IActionResult> OnPostAsync(CancellationToken ct)
    {
        var (user, persistent) = await auth.GetTwoFactorPendingAsync(ct);
        if (user is null)
        {
            TempData["Error"] = "That sign-in attempt expired. Please start again.";
            return RedirectToPage("/Admin/Login");
        }

        var verified = UseRecoveryCode
            ? await auth.RedeemRecoveryCodeAsync(user, Code, ct)
            : await auth.VerifyTotpAsync(user, Code, ct);

        if (!verified)
        {
            logger.LogWarning("Failed second factor for admin '{Username}' from {Ip}.",
                user.Username, HttpContext.Connection.RemoteIpAddress);
            ErrorMessage = UseRecoveryCode
                ? "That recovery code is not valid, or it has already been used."
                : "That code is not valid. Check your authenticator app and try again.";
            return Page();
        }

        await auth.SignInAsync(user, persistent, ct);

        if (UseRecoveryCode)
        {
            var remaining = await auth.CountUnusedRecoveryCodesAsync(user.Id, ct);
            TempData["Info"] = $"You signed in with a recovery code. {remaining} remain — " +
                               "generate a new set under Security if you are running low.";
        }

        return RedirectToPage("/Admin/Index");
    }
}
