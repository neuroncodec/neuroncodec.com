using Microsoft.AspNetCore.Mvc;
using NeuronCodec.Web.Data.Entities;
using NeuronCodec.Web.Services;

namespace NeuronCodec.Web.Pages.Admin.Seo;

public class StructuredDataModel(SiteSettings settings) : SeoSettingsPage(settings)
{
    [BindProperty] public string? OrganizationName { get; set; }
    [BindProperty] public string? OrganizationLogoUrl { get; set; }
    [BindProperty] public string? SameAsUrls { get; set; }

    public void OnGet()
    {
        SetViewData();
        OrganizationName = Settings.Get(SettingKeys.OrganizationName);
        OrganizationLogoUrl = Settings.Get(SettingKeys.OrganizationLogoUrl);
        SameAsUrls = Settings.Get(SettingKeys.OrganizationSameAs);
    }

    public async Task<IActionResult> OnPostAsync(CancellationToken ct)
    {
        SetViewData();

        // One profile URL per line; anything that is not a full http(s) URL is rejected rather
        // than silently emitted into the JSON-LD.
        var cleaned = (SameAsUrls ?? "")
            .Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .ToList();

        var invalid = cleaned.Where(u => !SeoBuilder.IsAbsoluteHttpUrl(u)).ToList();
        if (invalid.Count > 0)
        {
            ModelState.AddModelError(nameof(SameAsUrls),
                $"These are not full URLs: {string.Join(", ", invalid)}");
            return Page();
        }

        await SaveAsync(new Dictionary<string, string?>
        {
            [SettingKeys.OrganizationName] = Trimmed(OrganizationName),
            [SettingKeys.OrganizationLogoUrl] = Trimmed(OrganizationLogoUrl),
            [SettingKeys.OrganizationSameAs] = cleaned.Count > 0 ? string.Join('\n', cleaned) : null,
        }, ct);

        return RedirectToPage();
    }

    private void SetViewData()
    {
        ViewData["AdminSection"] = "seo";
        ViewData["AdminPage"] = "seo-structured";
    }
}
