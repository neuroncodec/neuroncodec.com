using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using NeuronCodec.Web.Data;
using NeuronCodec.Web.Data.Entities;
using NeuronCodec.Web.Services;

namespace NeuronCodec.Web.Pages.Admin.Taxonomy;

public class CategoriesModel(AppDbContext db) : PageModel
{
    public record Row(Category Category, int ArticleCount);

    public IReadOnlyList<Row> Categories { get; private set; } = [];
    public string[] TagClassOptions => TagClasses.All;

    public async Task OnGetAsync(CancellationToken ct)
    {
        SetViewData();
        await LoadAsync(ct);
    }

    public async Task<IActionResult> OnPostCreateAsync(
        string name, string? tagClass, string? description, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            TempData["Error"] = "Give the category a name.";
            return RedirectToPage();
        }

        db.Categories.Add(new Category
        {
            Name = name.Trim(),
            Slug = await UniqueSlugAsync(Slug.From(name, "category"), null, ct),
            TagClass = TagClasses.Normalize(tagClass),
            Description = string.IsNullOrWhiteSpace(description) ? null : description.Trim(),
        });

        await db.SaveChangesAsync(ct);
        TempData["Success"] = $"Added the category “{name.Trim()}”.";
        return RedirectToPage();
    }

    public async Task<IActionResult> OnPostUpdateAsync(
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
            category.Slug = await UniqueSlugAsync(candidate, id, ct);

        await db.SaveChangesAsync(ct);
        TempData["Success"] = "Category updated.";
        return RedirectToPage();
    }

    /// <summary>
    /// Deleting a category leaves its articles in place; the foreign key nulls out, and an
    /// article without a category simply shows no pill.
    /// </summary>
    public async Task<IActionResult> OnPostDeleteAsync(int id, CancellationToken ct)
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

    private async Task LoadAsync(CancellationToken ct) =>
        Categories =
        [
            .. await db.Categories
                .AsNoTracking()
                .OrderBy(c => c.Name)
                .Select(c => new Row(c, c.Articles.Count))
                .ToListAsync(ct)
        ];

    private async Task<string> UniqueSlugAsync(string candidate, int? exceptId, CancellationToken ct)
    {
        var taken = (await db.Categories
                .Where(c => exceptId == null || c.Id != exceptId)
                .Select(c => c.Slug)
                .ToListAsync(ct))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        return Slug.Unique(candidate, taken.Contains);
    }

    private void SetViewData()
    {
        ViewData["AdminSection"] = "taxonomy";
        ViewData["AdminPage"] = "categories";
    }
}
