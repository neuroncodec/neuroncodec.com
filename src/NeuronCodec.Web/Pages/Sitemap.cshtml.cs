using System.Text;
using System.Xml;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using NeuronCodec.Web.Data;
using NeuronCodec.Web.Data.Entities;
using NeuronCodec.Web.Services;

namespace NeuronCodec.Web.Pages;

/// <summary>
/// sitemap.xml, built from the live articles plus the static pages. Articles flagged
/// <c>NoIndex</c> are left out so the sitemap never contradicts the page's own meta tag.
/// </summary>
public class SitemapModel(AppDbContext db, SeoBuilder seo, SiteSettings settings, TimeProvider clock) : PageModel
{
    public async Task<IActionResult> OnGetAsync(CancellationToken ct)
    {
        if (!settings.GetBool(SettingKeys.SitemapEnabled))
            return NotFound();

        var now = clock.GetUtcNow().UtcDateTime;
        var articles = await db.Articles
            .AsNoTracking()
            .Live(now)
            .Where(a => !a.NoIndex)
            .OrderByDescending(a => a.PublishedAt)
            .Select(a => new { a.Slug, a.UpdatedAt })
            .ToListAsync(ct);

        var newestArticle = articles.Count > 0 ? articles.Max(a => a.UpdatedAt) : (DateTime?)null;

        var buffer = new StringBuilder();
        var xmlSettings = new XmlWriterSettings
        {
            Indent = true,
            Encoding = new UTF8Encoding(false),
            Async = true,
        };

        await using (var writer = XmlWriter.Create(buffer, xmlSettings))
        {
            await writer.WriteStartDocumentAsync();
            await writer.WriteStartElementAsync(null, "urlset", "http://www.sitemaps.org/schemas/sitemap/0.9");

            await WriteUrlAsync(writer, seo.Absolute("/"), newestArticle, "daily", "1.0");
            await WriteUrlAsync(writer, seo.Absolute("/articles"), newestArticle, "daily", "0.9");
            await WriteUrlAsync(writer, seo.Absolute("/about"), null, "monthly", "0.5");
            await WriteUrlAsync(writer, seo.Absolute("/projects"), null, "monthly", "0.5");

            foreach (var article in articles)
            {
                await WriteUrlAsync(writer, seo.Absolute($"/articles/{article.Slug}"),
                    article.UpdatedAt, "monthly", "0.8");
            }

            await writer.WriteEndElementAsync();
            await writer.WriteEndDocumentAsync();
        }

        return Content(buffer.ToString(), "application/xml; charset=utf-8");
    }

    private static async Task WriteUrlAsync(
        XmlWriter writer, string location, DateTime? lastModified, string changeFrequency, string priority)
    {
        await writer.WriteStartElementAsync(null, "url", null);
        await writer.WriteElementStringAsync(null, "loc", null, location);

        if (lastModified.HasValue)
            await writer.WriteElementStringAsync(null, "lastmod", null, lastModified.Value.ToString("yyyy-MM-dd"));

        await writer.WriteElementStringAsync(null, "changefreq", null, changeFrequency);
        await writer.WriteElementStringAsync(null, "priority", null, priority);
        await writer.WriteEndElementAsync();
    }
}
