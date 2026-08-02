using Microsoft.AspNetCore.Mvc;
using NeuronCodec.Web.Data.Entities;
using NeuronCodec.Web.Services;

namespace NeuronCodec.Web.Pages.Admin.Seo;

public class IdentityModel(SiteSettings settings, SeoBuilder seo) : SeoSettingsPage(settings)
{
    [BindProperty] public string? SiteName { get; set; }
    [BindProperty] public string? Tagline { get; set; }
    [BindProperty] public string? BaseUrl { get; set; }
    [BindProperty] public string? DefaultMetaDescription { get; set; }

    public string ResolvedBaseUrl { get; private set; } = "";

    public void OnGet()
    {
        SetViewData();
        SiteName = Settings.Get(SettingKeys.SiteName);
        Tagline = Settings.Get(SettingKeys.SiteTagline);
        BaseUrl = Settings.Get(SettingKeys.BaseUrl);
        DefaultMetaDescription = Settings.Get(SettingKeys.DefaultMetaDescription);
        ResolvedBaseUrl = seo.BaseUrl;
    }

    public async Task<IActionResult> OnPostAsync(CancellationToken ct)
    {
        SetViewData();
        ResolvedBaseUrl = seo.BaseUrl;

        if (!string.IsNullOrWhiteSpace(BaseUrl) && !SeoBuilder.IsAbsoluteHttpUrl(BaseUrl.Trim()))
        {
            ModelState.AddModelError(nameof(BaseUrl), "Enter a full URL, including https://.");
            return Page();
        }

        await SaveAsync(new Dictionary<string, string?>
        {
            [SettingKeys.SiteName] = Trimmed(SiteName),
            [SettingKeys.SiteTagline] = Trimmed(Tagline),
            [SettingKeys.BaseUrl] = Trimmed(BaseUrl)?.TrimEnd('/'),
            [SettingKeys.DefaultMetaDescription] = Trimmed(DefaultMetaDescription),
        }, ct);

        return RedirectToPage();
    }

    private void SetViewData()
    {
        ViewData["AdminSection"] = "seo";
        ViewData["AdminPage"] = "seo-identity";
    }
}
