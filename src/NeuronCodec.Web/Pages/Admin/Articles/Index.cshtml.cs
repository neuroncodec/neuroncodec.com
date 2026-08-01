using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using NeuronCodec.Web.Data;
using NeuronCodec.Web.Data.Entities;
using NeuronCodec.Web.Services;

namespace NeuronCodec.Web.Pages.Admin.Articles;

public class IndexModel(AppDbContext db, TimeProvider clock) : PageModel
{
    private const int PerPage = 20;

    [BindProperty(SupportsGet = true, Name = "q")]
    public string? Query { get; set; }

    [BindProperty(SupportsGet = true, Name = "status")]
    public string? Status { get; set; }

    [BindProperty(SupportsGet = true, Name = "category")]
    public int? CategoryId { get; set; }

    [BindProperty(SupportsGet = true, Name = "page")]
    public int RequestedPage { get; set; } = 1;

    public IReadOnlyList<Article> Items { get; private set; } = [];
    public IReadOnlyList<Category> Categories { get; private set; } = [];
    public int TotalCount { get; private set; }
    public int CurrentPage { get; private set; } = 1;
    public int TotalPages { get; private set; } = 1;

    public string[] StatusOptions => Enum.GetNames<ArticleStatus>();

    public bool HasFilters =>
        !string.IsNullOrWhiteSpace(Query) || !string.IsNullOrWhiteSpace(Status) || CategoryId is not null;

    public string PageHref(int page)
    {
        var parts = new List<string>();
        if (!string.IsNullOrWhiteSpace(Query)) parts.Add("q=" + Uri.EscapeDataString(Query));
        if (!string.IsNullOrWhiteSpace(Status)) parts.Add("status=" + Uri.EscapeDataString(Status));
        if (CategoryId is { } id) parts.Add("category=" + id);
        if (page > 1) parts.Add("page=" + page);
        return "/admin/articles" + (parts.Count > 0 ? "?" + string.Join("&", parts) : "");
    }

    public async Task OnGetAsync(CancellationToken ct)
    {
        Categories = await db.Categories.AsNoTracking().OrderBy(c => c.Name).ToListAsync(ct);

        var query = db.Articles.AsNoTracking().Include(a => a.Category).AsQueryable();

        if (!string.IsNullOrWhiteSpace(Query))
        {
            var term = Query.Trim();
            query = query.Where(a =>
                EF.Functions.Like(a.Title, $"%{term}%") ||
                EF.Functions.Like(a.Slug, $"%{term}%") ||
                EF.Functions.Like(a.Excerpt, $"%{term}%"));
        }

        if (Enum.TryParse<ArticleStatus>(Status, ignoreCase: true, out var status))
            query = query.Where(a => a.Status == status);

        if (CategoryId is { } categoryId)
            query = query.Where(a => a.CategoryId == categoryId);

        TotalCount = await query.CountAsync(ct);
        TotalPages = Math.Max(1, (int)Math.Ceiling(TotalCount / (double)PerPage));
        CurrentPage = Math.Clamp(RequestedPage < 1 ? 1 : RequestedPage, 1, TotalPages);

        Items = await query
            // Newest activity first: scheduled and published by date, drafts by last edit.
            .OrderByDescending(a => a.PublishedAt ?? a.UpdatedAt)
            .ThenByDescending(a => a.Id)
            .Skip((CurrentPage - 1) * PerPage)
            .Take(PerPage)
            .ToListAsync(ct);
    }

    public async Task<IActionResult> OnPostDeleteAsync(int id, CancellationToken ct)
    {
        var article = await db.Articles.FindAsync([id], ct);
        if (article is null)
        {
            TempData["Error"] = "That article no longer exists.";
            return RedirectToPage();
        }

        db.Articles.Remove(article);
        await db.SaveChangesAsync(ct);
        TempData["Success"] = $"Deleted “{article.Title}”.";
        return RedirectToPage();
    }

    /// <summary>Copies an article into a fresh draft — the usual way to start from a past piece.</summary>
    public async Task<IActionResult> OnPostDuplicateAsync(int id, CancellationToken ct)
    {
        var source = await db.Articles
            .Include(a => a.ArticleTags)
            .FirstOrDefaultAsync(a => a.Id == id, ct);

        if (source is null)
        {
            TempData["Error"] = "That article no longer exists.";
            return RedirectToPage();
        }

        var existingSlugs = await db.Articles.Select(a => a.Slug).ToListAsync(ct);
        var slugSet = existingSlugs.ToHashSet(StringComparer.OrdinalIgnoreCase);
        var now = clock.GetUtcNow().UtcDateTime;

        var copy = new Article
        {
            Title = source.Title + " (copy)",
            Slug = Slug.Unique(source.Slug + "-copy", slugSet.Contains),
            Excerpt = source.Excerpt,
            BodyMarkdown = source.BodyMarkdown,
            BodyHtml = source.BodyHtml,
            Status = ArticleStatus.Draft,
            PublishedAt = null,
            ReadMinutes = source.ReadMinutes,
            ReadMinutesOverridden = source.ReadMinutesOverridden,
            CategoryId = source.CategoryId,
            FeaturedMediaId = source.FeaturedMediaId,
            MetaTitle = source.MetaTitle,
            MetaDescription = source.MetaDescription,
            OgTitle = source.OgTitle,
            OgDescription = source.OgDescription,
            OgImageId = source.OgImageId,
            TwitterCardType = source.TwitterCardType,
            NoIndex = source.NoIndex,
            CreatedAt = now,
            UpdatedAt = now,
            // A canonical URL points at one specific page, so it is never copied.
        };

        foreach (var tag in source.ArticleTags)
            copy.ArticleTags.Add(new ArticleTag { TagId = tag.TagId });

        db.Articles.Add(copy);
        await db.SaveChangesAsync(ct);

        TempData["Success"] = "Created a draft copy.";
        return Redirect($"/admin/articles/edit?id={copy.Id}");
    }
}
