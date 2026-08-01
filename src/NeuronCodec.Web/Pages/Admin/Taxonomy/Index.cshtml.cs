using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using NeuronCodec.Web.Data;
using NeuronCodec.Web.Data.Entities;
using NeuronCodec.Web.Services;

namespace NeuronCodec.Web.Pages.Admin.Taxonomy;

public class IndexModel(AppDbContext db) : PageModel
{
    public record CategoryRow(Category Category, int ArticleCount);
    public record TagRow(Tag Tag, int ArticleCount);

    public IReadOnlyList<CategoryRow> Categories { get; private set; } = [];
    public IReadOnlyList<TagRow> Tags { get; private set; } = [];

    public string[] TagClassOptions => TagClasses.All;

    public async Task OnGetAsync(CancellationToken ct) => await LoadAsync(ct);

    private async Task LoadAsync(CancellationToken ct)
    {
        Categories =
        [
            .. await db.Categories
                .AsNoTracking()
                .OrderBy(c => c.Name)
                .Select(c => new CategoryRow(c, c.Articles.Count))
                .ToListAsync(ct)
        ];

        Tags =
        [
            .. await db.Tags
                .AsNoTracking()
                .OrderBy(t => t.Name)
                .Select(t => new TagRow(t, t.ArticleTags.Count))
                .ToListAsync(ct)
        ];
    }

    // ── Categories ───────────────────────────────────────────────────────────

    public async Task<IActionResult> OnPostCreateCategoryAsync(string name, string? tagClass, string? description, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            TempData["Error"] = "Give the category a name.";
            return RedirectToPage();
        }

        var slug = await UniqueCategorySlugAsync(Slug.From(name, "category"), null, ct);

        db.Categories.Add(new Category
        {
            Name = name.Trim(),
            Slug = slug,
            TagClass = TagClasses.Normalize(tagClass),
            Description = string.IsNullOrWhiteSpace(description) ? null : description.Trim(),
        });

        await db.SaveChangesAsync(ct);
        TempData["Success"] = $"Added the category “{name.Trim()}”.";
        return RedirectToPage();
    }

    public async Task<IActionResult> OnPostUpdateCategoryAsync(
        int id, string name, string? slug, string? tagClass, string? description, CancellationToken ct)
    {
        var category = await db.Categories.FindAsync([id], ct);
        if (category is null)
        {
            TempData["Error"] = "That category no longer exists.";
            return RedirectToPage();
        }

        if (string.IsNullOrWhiteSpace(name))
        {
            TempData["Error"] = "A category needs a name.";
            return RedirectToPage();
        }

        category.Name = name.Trim();
        category.TagClass = TagClasses.Normalize(tagClass);
        category.Description = string.IsNullOrWhiteSpace(description) ? null : description.Trim();

        var candidate = Slug.From(string.IsNullOrWhiteSpace(slug) ? name : slug, "category");
        if (candidate != category.Slug)
            category.Slug = await UniqueCategorySlugAsync(candidate, id, ct);

        await db.SaveChangesAsync(ct);
        TempData["Success"] = "Category updated.";
        return RedirectToPage();
    }

    /// <summary>
    /// Deleting a category leaves its articles in place; the foreign key is configured to null
    /// out, and an article without a category simply shows no pill.
    /// </summary>
    public async Task<IActionResult> OnPostDeleteCategoryAsync(int id, CancellationToken ct)
    {
        var category = await db.Categories.FindAsync([id], ct);
        if (category is null)
        {
            TempData["Error"] = "That category no longer exists.";
            return RedirectToPage();
        }

        var affected = await db.Articles.CountAsync(a => a.CategoryId == id, ct);

        db.Categories.Remove(category);
        await db.SaveChangesAsync(ct);

        TempData["Success"] = affected > 0
            ? $"Deleted “{category.Name}”. {affected} article(s) now have no category."
            : $"Deleted “{category.Name}”.";
        return RedirectToPage();
    }

    // ── Tags ─────────────────────────────────────────────────────────────────

    public async Task<IActionResult> OnPostCreateTagAsync(string name, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            TempData["Error"] = "Give the tag a name.";
            return RedirectToPage();
        }

        var slug = Slug.From(name, "tag");
        if (await db.Tags.AnyAsync(t => t.Slug == slug, ct))
        {
            TempData["Error"] = "A tag with that name already exists.";
            return RedirectToPage();
        }

        db.Tags.Add(new Tag { Name = name.Trim(), Slug = slug });
        await db.SaveChangesAsync(ct);
        TempData["Success"] = $"Added the tag “{name.Trim()}”.";
        return RedirectToPage();
    }

    public async Task<IActionResult> OnPostRenameTagAsync(int id, string name, CancellationToken ct)
    {
        var tag = await db.Tags.FindAsync([id], ct);
        if (tag is null || string.IsNullOrWhiteSpace(name))
        {
            TempData["Error"] = "That tag could not be renamed.";
            return RedirectToPage();
        }

        var slug = Slug.From(name, "tag");
        if (slug != tag.Slug && await db.Tags.AnyAsync(t => t.Slug == slug && t.Id != id, ct))
        {
            TempData["Error"] = "Another tag already uses that name.";
            return RedirectToPage();
        }

        tag.Name = name.Trim();
        tag.Slug = slug;
        await db.SaveChangesAsync(ct);

        TempData["Success"] = "Tag renamed.";
        return RedirectToPage();
    }

    public async Task<IActionResult> OnPostDeleteTagAsync(int id, CancellationToken ct)
    {
        var tag = await db.Tags.FindAsync([id], ct);
        if (tag is null)
        {
            TempData["Error"] = "That tag no longer exists.";
            return RedirectToPage();
        }

        db.Tags.Remove(tag);
        await db.SaveChangesAsync(ct);
        TempData["Success"] = $"Deleted the tag “{tag.Name}”.";
        return RedirectToPage();
    }

    private async Task<string> UniqueCategorySlugAsync(string candidate, int? exceptId, CancellationToken ct)
    {
        var taken = (await db.Categories
                .Where(c => exceptId == null || c.Id != exceptId)
                .Select(c => c.Slug)
                .ToListAsync(ct))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        return Slug.Unique(candidate, taken.Contains);
    }
}
