using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using NeuronCodec.Web.Services;

namespace NeuronCodec.Web.Pages.Admin;

public class LoginModel(AdminAuth auth, SiteSettings settings, ILogger<LoginModel> logger) : PageModel
{
    public class InputModel
    {
        [Required]
        public string Username { get; set; } = "";

        [Required]
        public string Password { get; set; } = "";

        public bool RememberMe { get; set; }
    }

    [BindProperty]
    public InputModel Input { get; set; } = new();

    public string? ErrorMessage { get; private set; }
    public string SiteName => settings.SiteName;

    public IActionResult OnGet()
    {
        // A signed-in admin has no use for the login form.
        return User.Identity?.IsAuthenticated == true ? RedirectToPage("/Admin/Index") : Page();
    }

    public async Task<IActionResult> OnPostAsync(CancellationToken ct)
    {
        if (!ModelState.IsValid)
        {
            ErrorMessage = "Enter a username and password.";
            return Page();
        }

        var result = await auth.VerifyPasswordAsync(Input.Username.Trim(), Input.Password, ct);

        switch (result.Outcome)
        {
            case LoginOutcome.Success:
                await auth.SignInAsync(result.User!, Input.RememberMe, ct);
                return RedirectToPage("/Admin/Index");

            case LoginOutcome.TwoFactorRequired:
                await auth.StartTwoFactorAsync(result.User!, Input.RememberMe);
                return RedirectToPage("/Admin/TwoFactor");

            case LoginOutcome.LockedOut:
                var minutes = Math.Max(1, (int)Math.Ceiling(result.LockoutRemaining!.Value.TotalMinutes));
                ErrorMessage = $"Too many failed attempts. Try again in {minutes} minute{(minutes == 1 ? "" : "s")}.";
                return Page();

            default:
                logger.LogWarning("Failed admin sign-in for '{Username}' from {Ip}.",
                    Input.Username, HttpContext.Connection.RemoteIpAddress);
                ErrorMessage = "That username and password combination is not correct.";
                return Page();
        }
    }
}
