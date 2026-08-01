using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using NeuronCodec.Web.Data;
using NeuronCodec.Web.Data.Entities;
using NeuronCodec.Web.Services;

namespace NeuronCodec.Web.Pages.Articles;

public class DetailModel(AppDbContext db, SeoBuilder seo, TimeProvider clock) : PageModel
{
    public Article? Article { get; private set; }
    public IReadOnlyList<string> TagNames { get; private set; } = [];

    /// <summary>True when a signed-in admin is viewing an article the public cannot see yet.</summary>
    public bool IsPreview { get; private set; }

    public async Task<IActionResult> OnGetAsync(string slug, CancellationToken ct)
    {
        var article = await db.Articles
            .AsNoTracking()
            .Include(a => a.Category)
            .Include(a => a.FeaturedMedia)
            .Include(a => a.OgImage)
            .Include(a => a.ArticleTags).ThenInclude(at => at.Tag)
            .FirstOrDefaultAsync(a => a.Slug == slug, ct);

        if (article is null) return NotFound();

        var isLive = article.IsLive(clock.GetUtcNow().UtcDateTime);
        if (!isLive)
        {
            // Drafts and not-yet-due scheduled posts stay reachable for a signed-in admin so the
            // editor's "preview" link works, but never for anyone else.
            if (User.Identity?.IsAuthenticated != true) return NotFound();
            IsPreview = true;
        }

        Article = article;
        TagNames = [.. article.ArticleTags.Select(at => at.Tag.Name).OrderBy(n => n)];

        ViewData["NavCurrent"] = "article";
        var meta = seo.ForArticle(article, TagNames);
        if (IsPreview) meta.NoIndex = true;
        ViewData["Meta"] = meta;

        return Page();
    }
}
