using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.AspNetCore.Mvc.Rendering;
using Microsoft.EntityFrameworkCore;
using NeuronCodec.Web.Data;
using NeuronCodec.Web.Data.Entities;
using NeuronCodec.Web.Services;

namespace NeuronCodec.Web.Pages.Admin.Articles;

public class EditModel(AppDbContext db, MarkdownRenderer markdown, TimeProvider clock) : PageModel
{
    public class InputModel
    {
        public int? Id { get; set; }

        [Required(ErrorMessage = "Give the article a title.")]
        [StringLength(300)]
        public string Title { get; set; } = "";

        [StringLength(200)]
        public string? Slug { get; set; }

        [StringLength(1000, ErrorMessage = "Keep the excerpt under 1000 characters.")]
        public string? Excerpt { get; set; }

        public string? BodyMarkdown { get; set; }

        public ArticleStatus Status { get; set; } = ArticleStatus.Draft;

        /// <summary>Publish moment, entered and displayed in UTC.</summary>
        public DateTime? PublishedAt { get; set; }

        public int? CategoryId { get; set; }

        /// <summary>Comma-separated tag names; unknown ones are created on save.</summary>
        public string? Tags { get; set; }

        public int? FeaturedMediaId { get; set; }

        public int? ReadMinutes { get; set; }
        public bool ReadMinutesOverridden { get; set; }

        [StringLength(300)] public string? MetaTitle { get; set; }
        [StringLength(400)] public string? MetaDescription { get; set; }
        [StringLength(500)] public string? CanonicalUrl { get; set; }
        [StringLength(300)] public string? OgTitle { get; set; }
        [StringLength(400)] public string? OgDescription { get; set; }
        public int? OgImageId { get; set; }
        public string TwitterCardType { get; set; } = "summary_large_image";
        public bool NoIndex { get; set; }
    }

    [BindProperty]
    public InputModel Input { get; set; } = new();

    public bool IsNew => Input.Id is null or 0;
    public string? ExistingSlug { get; private set; }
    public DateTime? CreatedAt { get; private set; }
    public DateTime? UpdatedAt { get; private set; }

    public IReadOnlyList<Category> Categories { get; private set; } = [];
    public IReadOnlyList<string> AllTagNames { get; private set; } = [];
    public MediaFile? FeaturedMedia { get; private set; }
    public MediaFile? OgImage { get; private set; }

    public SelectList TwitterCardOptions { get; } = new(new[] { "summary_large_image", "summary" });

    public async Task<IActionResult> OnGetAsync(int? id, CancellationToken ct)
    {
        await LoadReferenceDataAsync(ct);

        if (id is null or 0)
        {
            Input = new InputModel
            {
                Status = ArticleStatus.Draft,
                PublishedAt = clock.GetUtcNow().UtcDateTime,
            };
            return Page();
        }

        var article = await db.Articles
            .Include(a => a.ArticleTags).ThenInclude(at => at.Tag)
            .Include(a => a.FeaturedMedia)
            .Include(a => a.OgImage)
            .FirstOrDefaultAsync(a => a.Id == id, ct);

        if (article is null) return NotFound();

        Input = new InputModel
        {
            Id = article.Id,
            Title = article.Title,
            Slug = article.Slug,
            Excerpt = article.Excerpt,
            BodyMarkdown = article.BodyMarkdown,
            Status = article.Status,
            PublishedAt = article.PublishedAt,
            CategoryId = article.CategoryId,
            Tags = string.Join(", ", article.ArticleTags.Select(at => at.Tag.Name).OrderBy(n => n)),
            FeaturedMediaId = article.FeaturedMediaId,
            ReadMinutes = article.ReadMinutes,
            ReadMinutesOverridden = article.ReadMinutesOverridden,
            MetaTitle = article.MetaTitle,
            MetaDescription = article.MetaDescription,
            CanonicalUrl = article.CanonicalUrl,
            OgTitle = article.OgTitle,
            OgDescription = article.OgDescription,
            OgImageId = article.OgImageId,
            TwitterCardType = article.TwitterCardType,
            NoIndex = article.NoIndex,
        };

        ExistingSlug = article.Slug;
        CreatedAt = article.CreatedAt;
        UpdatedAt = article.UpdatedAt;
        FeaturedMedia = article.FeaturedMedia;
        OgImage = article.OgImage;

        return Page();
    }

    public async Task<IActionResult> OnPostAsync(string? action, CancellationToken ct)
    {
        await LoadReferenceDataAsync(ct);

        var now = clock.GetUtcNow().UtcDateTime;

        // Publishing needs a moment to publish at; default it rather than blocking the save.
        if (Input.Status != ArticleStatus.Draft && Input.PublishedAt is null)
            Input.PublishedAt = now;

        if (Input.Status == ArticleStatus.Scheduled && Input.PublishedAt <= now)
        {
            ModelState.AddModelError("Input.PublishedAt",
                "A scheduled article needs a date in the future. Use Published to publish it now.");
        }

        if (!ModelState.IsValid)
        {
            await ReloadPreviewStateAsync(ct);
            return Page();
        }

        var article = IsNew
            ? new Article { CreatedAt = now }
            : await db.Articles.Include(a => a.ArticleTags).FirstOrDefaultAsync(a => a.Id == Input.Id, ct);

        if (article is null) return NotFound();

        article.Title = Input.Title.Trim();
        article.Slug = await ResolveSlugAsync(article, ct);
        article.BodyMarkdown = Input.BodyMarkdown ?? "";
        article.BodyHtml = markdown.ToHtml(article.BodyMarkdown);

        article.Excerpt = string.IsNullOrWhiteSpace(Input.Excerpt)
            // An empty excerpt would leave a blank card body, so fall back to the opening text.
            ? MarkdownRenderer.Summarize(article.BodyMarkdown)
            : Input.Excerpt.Trim();

        article.Status = Input.Status;
        article.PublishedAt = Input.Status == ArticleStatus.Draft
            ? Input.PublishedAt
            : Input.PublishedAt!.Value;

        article.ReadMinutesOverridden = Input.ReadMinutesOverridden;
        article.ReadMinutes = Input.ReadMinutesOverridden && Input.ReadMinutes is > 0
            ? Input.ReadMinutes.Value
            : MarkdownRenderer.EstimateReadMinutes(article.BodyMarkdown);

        article.CategoryId = Input.CategoryId;
        article.FeaturedMediaId = Input.FeaturedMediaId;

        article.MetaTitle = Trimmed(Input.MetaTitle);
        article.MetaDescription = Trimmed(Input.MetaDescription);
        article.CanonicalUrl = Trimmed(Input.CanonicalUrl);
        article.OgTitle = Trimmed(Input.OgTitle);
        article.OgDescription = Trimmed(Input.OgDescription);
        article.OgImageId = Input.OgImageId;
        article.TwitterCardType = string.IsNullOrWhiteSpace(Input.TwitterCardType)
            ? "summary_large_image"
            : Input.TwitterCardType;
        article.NoIndex = Input.NoIndex;
        article.UpdatedAt = now;

        if (IsNew) db.Articles.Add(article);
        await db.SaveChangesAsync(ct);

        await SyncTagsAsync(article, ct);
        await db.SaveChangesAsync(ct);

        TempData["Success"] = IsNew ? "Article created." : "Article saved.";

        return action switch
        {
            "view" => Redirect($"/articles/{article.Slug}"),
            "close" => Redirect("/admin/articles"),
            _ => Redirect($"/admin/articles/edit?id={article.Id}"),
        };
    }

    public async Task<IActionResult> OnPostDeleteAsync(CancellationToken ct)
    {
        var article = await db.Articles.FindAsync([Input.Id], ct);
        if (article is null) return NotFound();

        db.Articles.Remove(article);
        await db.SaveChangesAsync(ct);

        TempData["Success"] = $"Deleted “{article.Title}”.";
        return Redirect("/admin/articles");
    }

    /// <summary>Renders markdown server-side so the editor preview can match the published page exactly.</summary>
    public IActionResult OnPostPreview([FromBody] PreviewRequest request) =>
        new JsonResult(new { html = markdown.ToHtml(request.Markdown) });

    public record PreviewRequest(string? Markdown);

    private async Task LoadReferenceDataAsync(CancellationToken ct)
    {
        Categories = await db.Categories.AsNoTracking().OrderBy(c => c.Name).ToListAsync(ct);
        AllTagNames = await db.Tags.AsNoTracking().OrderBy(t => t.Name).Select(t => t.Name).ToListAsync(ct);
    }

    /// <summary>Re-attaches the media rows the form references, so a failed post still renders their thumbnails.</summary>
    private async Task ReloadPreviewStateAsync(CancellationToken ct)
    {
        if (Input.FeaturedMediaId is { } featuredId)
            FeaturedMedia = await db.MediaFiles.AsNoTracking().FirstOrDefaultAsync(m => m.Id == featuredId, ct);

        if (Input.OgImageId is { } ogId)
            OgImage = await db.MediaFiles.AsNoTracking().FirstOrDefaultAsync(m => m.Id == ogId, ct);
    }

    /// <summary>
    /// Uses the typed slug when given, otherwise derives one from the title. Either way the result
    /// is made unique across every other article.
    /// </summary>
    private async Task<string> ResolveSlugAsync(Article article, CancellationToken ct)
    {
        var candidate = Slug.From(
            string.IsNullOrWhiteSpace(Input.Slug) ? Input.Title : Input.Slug,
            "article");

        if (candidate == article.Slug) return candidate;

        var taken = (await db.Articles
                .Where(a => a.Id != article.Id)
                .Select(a => a.Slug)
                .ToListAsync(ct))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        return Slug.Unique(candidate, taken.Contains);
    }

    /// <summary>Reconciles the article's tags with the comma-separated input, creating new tags as needed.</summary>
    private async Task SyncTagsAsync(Article article, CancellationToken ct)
    {
        var wanted = (Input.Tags ?? "")
            .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(name => (Name: name, Slug: Slug.From(name, "")))
            .Where(t => t.Slug.Length > 0)
            .DistinctBy(t => t.Slug)
            .ToList();

        var wantedSlugs = wanted.Select(t => t.Slug).ToHashSet();

        var existing = await db.Tags
            .Where(t => wantedSlugs.Contains(t.Slug))
            .ToDictionaryAsync(t => t.Slug, ct);

        foreach (var (name, slug) in wanted.Where(t => !existing.ContainsKey(t.Slug)))
        {
            var tag = new Tag { Name = name, Slug = slug };
            db.Tags.Add(tag);
            existing[slug] = tag;
        }

        if (wanted.Count > 0) await db.SaveChangesAsync(ct);

        var currentLinks = await db.ArticleTags
            .Where(at => at.ArticleId == article.Id)
            .ToListAsync(ct);

        var wantedIds = wanted.Select(t => existing[t.Slug].Id).ToHashSet();

        db.ArticleTags.RemoveRange(currentLinks.Where(link => !wantedIds.Contains(link.TagId)));

        var currentIds = currentLinks.Select(link => link.TagId).ToHashSet();
        foreach (var tagId in wantedIds.Where(id => !currentIds.Contains(id)))
            db.ArticleTags.Add(new ArticleTag { ArticleId = article.Id, TagId = tagId });
    }

    private static string? Trimmed(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}
