using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using NeuronCodec.Web.Data;
using NeuronCodec.Web.Data.Entities;
using NeuronCodec.Web.Services;

namespace NeuronCodec.Web.Pages.Admin.Seo;

public class IndexModel(AppDbContext db, SiteSettings settings, SeoBuilder seo, TimeProvider clock) : PageModel
{
    public class InputModel
    {
        public string? SiteName { get; set; }
        public string? Tagline { get; set; }
        public string? BaseUrl { get; set; }
        public string? DefaultMetaDescription { get; set; }
        public int? DefaultOgImageId { get; set; }
        public string? TwitterSite { get; set; }
        public string? TwitterCreator { get; set; }
        public string? OrganizationName { get; set; }
        public string? OrganizationLogoUrl { get; set; }
        public string? RobotsExtra { get; set; }
        public bool SitemapEnabled { get; set; } = true;
    }

    [BindProperty]
    public InputModel Input { get; set; } = new();

    public MediaFile? DefaultOgImage { get; private set; }
    public string ResolvedBaseUrl { get; private set; } = "";
    public int IndexableArticleCount { get; private set; }
    public int NoIndexArticleCount { get; private set; }
    public int MissingMetaDescriptionCount { get; private set; }

    public async Task OnGetAsync(CancellationToken ct)
    {
        Input = new InputModel
        {
            SiteName = settings.Get(SettingKeys.SiteName),
            Tagline = settings.Get(SettingKeys.SiteTagline),
            BaseUrl = settings.Get(SettingKeys.BaseUrl),
            DefaultMetaDescription = settings.Get(SettingKeys.DefaultMetaDescription),
            DefaultOgImageId = settings.GetInt(SettingKeys.DefaultOgImageId),
            TwitterSite = settings.Get(SettingKeys.TwitterSite),
            TwitterCreator = settings.Get(SettingKeys.TwitterCreator),
            OrganizationName = settings.Get(SettingKeys.OrganizationName),
            OrganizationLogoUrl = settings.Get(SettingKeys.OrganizationLogoUrl),
            RobotsExtra = settings.Get(SettingKeys.RobotsExtra),
            SitemapEnabled = settings.GetBool(SettingKeys.SitemapEnabled),
        };

        await LoadDiagnosticsAsync(ct);
    }

    public async Task<IActionResult> OnPostAsync(CancellationToken ct)
    {
        if (!string.IsNullOrWhiteSpace(Input.BaseUrl)
            && !SeoBuilder.IsAbsoluteHttpUrl(Input.BaseUrl.Trim()))
        {
            ModelState.AddModelError("Input.BaseUrl", "Enter a full URL, including https://.");
            await LoadDiagnosticsAsync(ct);
            return Page();
        }

        await settings.SetManyAsync(new Dictionary<string, string?>
        {
            [SettingKeys.SiteName] = Input.SiteName?.Trim(),
            [SettingKeys.SiteTagline] = Input.Tagline?.Trim(),
            [SettingKeys.BaseUrl] = Input.BaseUrl?.Trim().TrimEnd('/'),
            [SettingKeys.DefaultMetaDescription] = Input.DefaultMetaDescription?.Trim(),
            [SettingKeys.DefaultOgImageId] = Input.DefaultOgImageId?.ToString(),
            [SettingKeys.TwitterSite] = Input.TwitterSite?.Trim(),
            [SettingKeys.TwitterCreator] = Input.TwitterCreator?.Trim(),
            [SettingKeys.OrganizationName] = Input.OrganizationName?.Trim(),
            [SettingKeys.OrganizationLogoUrl] = Input.OrganizationLogoUrl?.Trim(),
            [SettingKeys.RobotsExtra] = Input.RobotsExtra?.Trim(),
            [SettingKeys.SitemapEnabled] = Input.SitemapEnabled.ToString(),
        }, ct);

        TempData["Success"] = "SEO settings saved.";
        return RedirectToPage();
    }

    private async Task LoadDiagnosticsAsync(CancellationToken ct)
    {
        ResolvedBaseUrl = seo.BaseUrl;

        if (Input.DefaultOgImageId is { } id)
            DefaultOgImage = await db.MediaFiles.AsNoTracking().FirstOrDefaultAsync(m => m.Id == id, ct);

        var now = clock.GetUtcNow().UtcDateTime;
        var live = db.Articles.AsNoTracking().Live(now);

        IndexableArticleCount = await live.CountAsync(a => !a.NoIndex, ct);
        NoIndexArticleCount = await live.CountAsync(a => a.NoIndex, ct);
        MissingMetaDescriptionCount = await live.CountAsync(
            a => (a.MetaDescription == null || a.MetaDescription == "") && (a.Excerpt == null || a.Excerpt == ""), ct);
    }
}
