using System.Security.Claims;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.EntityFrameworkCore;
using NeuronCodec.Web.Data;

namespace NeuronCodec.Web.Services;

/// <summary>
/// While the admin still holds the password seeded from configuration, every admin page except
/// the change-password screen redirects there. The seed value is assumed compromised — it lives
/// in an env var or a compose file — so nothing else in the admin is reachable until it is replaced.
/// </summary>
public class RequirePasswordChangeFilter(AppDbContext db) : IAsyncPageFilter
{
    private const string ChangePasswordPath = "/Admin/Security/ChangePassword";

    public Task OnPageHandlerSelectionAsync(PageHandlerSelectedContext context) => Task.CompletedTask;

    public async Task OnPageHandlerExecutionAsync(PageHandlerExecutingContext context, PageHandlerExecutionDelegate next)
    {
        var user = context.HttpContext.User;

        if (user.Identity?.IsAuthenticated != true
            || string.Equals(context.ActionDescriptor.ViewEnginePath, ChangePasswordPath, StringComparison.OrdinalIgnoreCase))
        {
            await next();
            return;
        }

        if (!int.TryParse(user.FindFirstValue(ClaimTypes.NameIdentifier), out var id))
        {
            await next();
            return;
        }

        var mustChange = await db.AdminUsers
            .Where(u => u.Id == id)
            .Select(u => u.MustChangePassword)
            .FirstOrDefaultAsync(context.HttpContext.RequestAborted);

        if (mustChange)
        {
            context.Result = new RedirectToPageResult("/Admin/Security/ChangePassword");
            return;
        }

        await next();
    }
}
