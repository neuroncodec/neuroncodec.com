using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using NeuronCodec.Web.Services;

namespace NeuronCodec.Web.Pages.Admin;

public class LogoutModel(AdminAuth auth) : PageModel
{
    /// <summary>A GET must not sign anyone out; send them to the login form instead.</summary>
    public IActionResult OnGet() => RedirectToPage("/Admin/Login");

    public async Task<IActionResult> OnPostAsync()
    {
        await auth.SignOutAsync();
        return RedirectToPage("/Admin/Login");
    }
}
