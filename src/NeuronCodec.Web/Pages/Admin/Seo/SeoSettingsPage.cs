using Microsoft.AspNetCore.Mvc.RazorPages;
using NeuronCodec.Web.Services;

namespace NeuronCodec.Web.Pages.Admin.Seo;

/// <summary>
/// Shared plumbing for the SEO sub-pages. Each one edits a small, related group of settings and
/// saves only its own keys, so two people (or two tabs) editing different sections cannot
/// clobber each other's values.
/// </summary>
public abstract class SeoSettingsPage(SiteSettings settings) : PageModel
{
    protected SiteSettings Settings { get; } = settings;

    protected async Task SaveAsync(IReadOnlyDictionary<string, string?> values, CancellationToken ct)
    {
        await Settings.SetManyAsync(values, ct);
        TempData["Success"] = "Settings saved.";
    }

    protected static string? Trimmed(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}
