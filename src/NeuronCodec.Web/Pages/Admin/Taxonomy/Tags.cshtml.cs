using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using NeuronCodec.Web.Data;
using NeuronCodec.Web.Data.Entities;
using NeuronCodec.Web.Services;

namespace NeuronCodec.Web.Pages.Admin.Taxonomy;

public class TagsModel(AppDbContext db) : PageModel
{
    public record Row(Tag Tag, int ArticleCount);

    public IReadOnlyList<Row> Tags { get; private set; } = [];

    public async Task OnGetAsync(CancellationToken ct)
    {
        SetViewData();
        await LoadAsync(ct);
    }

    public async Task<IActionResult> OnPostCreateAsync(string name, CancellationToken ct)
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

    public async Task<IActionResult> OnPostRenameAsync(int id, string name, CancellationToken ct)
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

    public async Task<IActionResult> OnPostDeleteAsync(int id, CancellationToken ct)
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

    private async Task LoadAsync(CancellationToken ct) =>
        Tags =
        [
            .. await db.Tags
                .AsNoTracking()
                .OrderBy(t => t.Name)
                .Select(t => new Row(t, t.ArticleTags.Count))
                .ToListAsync(ct)
        ];

    private void SetViewData()
    {
        ViewData["AdminSection"] = "taxonomy";
        ViewData["AdminPage"] = "tags";
    }
}
