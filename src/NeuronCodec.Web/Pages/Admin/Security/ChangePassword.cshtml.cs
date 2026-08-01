using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using NeuronCodec.Web.Services;

namespace NeuronCodec.Web.Pages.Admin.Security;

public class ChangePasswordModel(AdminAuth auth, ILogger<ChangePasswordModel> logger) : PageModel
{
    public class InputModel
    {
        [Required(ErrorMessage = "Enter your current password.")]
        public string CurrentPassword { get; set; } = "";

        [Required(ErrorMessage = "Enter a new password.")]
        public string NewPassword { get; set; } = "";

        [Required(ErrorMessage = "Confirm the new password.")]
        public string ConfirmPassword { get; set; } = "";
    }

    [BindProperty]
    public InputModel Input { get; set; } = new();

    /// <summary>True while the account still carries the password seeded from configuration.</summary>
    public bool IsForced { get; private set; }

    public string Username { get; private set; } = "";

    public async Task<IActionResult> OnGetAsync(CancellationToken ct)
    {
        var admin = await auth.GetAdminAsync(ct);
        if (admin is null) return NotFound();

        IsForced = admin.MustChangePassword;
        Username = admin.Username;
        return Page();
    }

    public async Task<IActionResult> OnPostAsync(CancellationToken ct)
    {
        var admin = await auth.GetAdminAsync(ct);
        if (admin is null) return NotFound();

        IsForced = admin.MustChangePassword;
        Username = admin.Username;

        if (!ModelState.IsValid) return Page();

        if (!PasswordHasher.Verify(Input.CurrentPassword, admin.PasswordHash))
        {
            ModelState.AddModelError("Input.CurrentPassword", "That is not your current password.");
            return Page();
        }

        if (AdminAuth.ValidatePassword(Input.NewPassword) is { } problem)
        {
            ModelState.AddModelError("Input.NewPassword", problem);
            return Page();
        }

        if (Input.NewPassword != Input.ConfirmPassword)
        {
            ModelState.AddModelError("Input.ConfirmPassword", "The two passwords do not match.");
            return Page();
        }

        if (Input.NewPassword == Input.CurrentPassword)
        {
            ModelState.AddModelError("Input.NewPassword", "Choose a password you have not used here before.");
            return Page();
        }

        await auth.ChangePasswordAsync(admin, Input.NewPassword, ct);

        // The security stamp moved, so re-issue this session's cookie rather than logging the
        // admin out of the browser they just used.
        await auth.SignInAsync(admin, persistent: false, ct);

        logger.LogWarning("Admin password changed.");
        TempData["Success"] = "Password changed. Any other signed-in browser has been signed out.";
        return RedirectToPage("/Admin/Security/Index");
    }
}
