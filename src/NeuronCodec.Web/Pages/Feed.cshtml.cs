using System.Text;
using System.Xml;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using NeuronCodec.Web.Data;
using NeuronCodec.Web.Data.Entities;
using NeuronCodec.Web.Services;

namespace NeuronCodec.Web.Pages;

/// <summary>RSS 2.0 feed of the 20 newest live articles.</summary>
public class FeedModel(AppDbContext db, SeoBuilder seo, SiteSettings settings, TimeProvider clock) : PageModel
{
    private const int MaxItems = 20;

    public async Task<IActionResult> OnGetAsync(CancellationToken ct)
    {
        var articles = await db.Articles
            .AsNoTracking()
            .Live(clock.GetUtcNow().UtcDateTime)
            .Include(a => a.Category)
            .NewestFirst()
            .Take(MaxItems)
            .ToListAsync(ct);

        var buffer = new StringBuilder();
        var xmlSettings = new XmlWriterSettings { Indent = true, Encoding = new UTF8Encoding(false), Async = true };

        await using (var writer = XmlWriter.Create(buffer, xmlSettings))
        {
            await writer.WriteStartDocumentAsync();
            await writer.WriteStartElementAsync(null, "rss", null);
            await writer.WriteAttributeStringAsync(null, "version", null, "2.0");
            await writer.WriteAttributeStringAsync("xmlns", "atom", null, "http://www.w3.org/2005/Atom");
            await writer.WriteStartElementAsync(null, "channel", null);

            await writer.WriteElementStringAsync(null, "title", null, settings.SiteName);
            await writer.WriteElementStringAsync(null, "link", null, seo.BaseUrl);
            await writer.WriteElementStringAsync(null, "description", null,
                settings.Get(SettingKeys.DefaultMetaDescription,
                    "Research notes on neuroscience, biotech, and brain-computer interfaces."));
            await writer.WriteElementStringAsync(null, "language", null, "en");

            await writer.WriteStartElementAsync("atom", "link", "http://www.w3.org/2005/Atom");
            await writer.WriteAttributeStringAsync(null, "href", null, seo.Absolute("/feed.xml"));
            await writer.WriteAttributeStringAsync(null, "rel", null, "self");
            await writer.WriteAttributeStringAsync(null, "type", null, "application/rss+xml");
            await writer.WriteEndElementAsync();

            foreach (var article in articles)
            {
                var url = seo.Absolute($"/articles/{article.Slug}");

                await writer.WriteStartElementAsync(null, "item", null);
                await writer.WriteElementStringAsync(null, "title", null, article.Title);
                await writer.WriteElementStringAsync(null, "link", null, url);
                await writer.WriteElementStringAsync(null, "description", null, article.Excerpt);

                await writer.WriteStartElementAsync(null, "guid", null);
                await writer.WriteAttributeStringAsync(null, "isPermaLink", null, "true");
                await writer.WriteStringAsync(url);
                await writer.WriteEndElementAsync();

                if (article.PublishedAt is { } published)
                {
                    await writer.WriteElementStringAsync(null, "pubDate", null,
                        published.ToString("r", System.Globalization.CultureInfo.InvariantCulture));
                }

                if (article.Category is not null)
                    await writer.WriteElementStringAsync(null, "category", null, article.Category.Name);

                await writer.WriteEndElementAsync();
            }

            await writer.WriteEndElementAsync(); // channel
            await writer.WriteEndElementAsync(); // rss
            await writer.WriteEndDocumentAsync();
        }

        return Content(buffer.ToString(), "application/rss+xml; charset=utf-8");
    }
}
