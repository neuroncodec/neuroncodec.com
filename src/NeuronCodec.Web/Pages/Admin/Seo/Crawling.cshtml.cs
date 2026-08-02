using Microsoft.AspNetCore.Mvc;
using NeuronCodec.Web.Data.Entities;
using NeuronCodec.Web.Services;

namespace NeuronCodec.Web.Pages.Admin.Seo;

public class CrawlingModel(SiteSettings settings, SeoBuilder seo) : SeoSettingsPage(settings)
{
    [BindProperty] public bool SitemapEnabled { get; set; } = true;
    [BindProperty] public string? RobotsExtra { get; set; }

    public string SitemapUrl { get; private set; } = "";

    public void OnGet()
    {
        SetViewData();
        SitemapEnabled = Settings.GetBool(SettingKeys.SitemapEnabled);
        RobotsExtra = Settings.Get(SettingKeys.RobotsExtra);
        SitemapUrl = seo.Absolute("/sitemap.xml");
    }

    public async Task<IActionResult> OnPostAsync(CancellationToken ct)
    {
        SetViewData();
        SitemapUrl = seo.Absolute("/sitemap.xml");

        await SaveAsync(new Dictionary<string, string?>
        {
            [SettingKeys.SitemapEnabled] = SitemapEnabled.ToString(),
            [SettingKeys.RobotsExtra] = Trimmed(RobotsExtra),
        }, ct);

        return RedirectToPage();
    }

    private void SetViewData()
    {
        ViewData["AdminSection"] = "seo";
        ViewData["AdminPage"] = "seo-crawling";
    }
}
