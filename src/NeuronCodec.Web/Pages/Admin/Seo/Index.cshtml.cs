using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using NeuronCodec.Web.Data;
using NeuronCodec.Web.Data.Entities;
using NeuronCodec.Web.Services;

namespace NeuronCodec.Web.Pages.Admin.Seo;

/// <summary>
/// SEO landing page: the health numbers plus links into each settings group. Editing lives in
/// the sub-pages so no single screen carries every field.
/// </summary>
public class IndexModel(AppDbContext db, SiteSettings settings, SeoBuilder seo, TimeProvider clock) : PageModel
{
    public string ResolvedBaseUrl { get; private set; } = "";
    public bool BaseUrlConfigured { get; private set; }
    public bool SitemapEnabled { get; private set; }

    public int IndexableCount { get; private set; }
    public int NoIndexCount { get; private set; }
    public int MissingDescriptionCount { get; private set; }

    public string SiteName { get; private set; } = "";
    public string? DefaultMetaDescription { get; private set; }
    public bool HasDefaultShareImage { get; private set; }
    public bool HasOrganisation { get; private set; }

    public async Task OnGetAsync(CancellationToken ct)
    {
        ViewData["AdminSection"] = "seo";
        ViewData["AdminPage"] = "seo-overview";

        ResolvedBaseUrl = seo.BaseUrl;
        BaseUrlConfigured = !string.IsNullOrWhiteSpace(settings.ConfiguredBaseUrl);
        SitemapEnabled = settings.GetBool(SettingKeys.SitemapEnabled);
        SiteName = settings.SiteName;
        DefaultMetaDescription = settings.Get(SettingKeys.DefaultMetaDescription);
        HasDefaultShareImage = settings.GetInt(SettingKeys.DefaultOgImageId) is not null;
        HasOrganisation = !string.IsNullOrWhiteSpace(settings.Get(SettingKeys.OrganizationName));

        var live = db.Articles.AsNoTracking().Live(clock.GetUtcNow().UtcDateTime);

        IndexableCount = await live.CountAsync(a => !a.NoIndex, ct);
        NoIndexCount = await live.CountAsync(a => a.NoIndex, ct);
        MissingDescriptionCount = await live.CountAsync(
            a => (a.MetaDescription == null || a.MetaDescription == "")
                 && (a.Excerpt == null || a.Excerpt == ""), ct);
    }
}
