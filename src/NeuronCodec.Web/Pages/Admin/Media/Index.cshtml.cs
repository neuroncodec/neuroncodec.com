using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using NeuronCodec.Web.Data;
using NeuronCodec.Web.Data.Entities;
using NeuronCodec.Web.Services;

namespace NeuronCodec.Web.Pages.Admin.Media;

/// <summary>
/// The media library. The same page backs the picker dialog inside the article editor through
/// its JSON handlers, so there is one implementation of listing and uploading.
/// </summary>
public class IndexModel(AppDbContext db, MediaStorage storage) : PageModel
{
    private const int PerPage = 36;

    [BindProperty(SupportsGet = true, Name = "q")]
    public string? Query { get; set; }

    [BindProperty(SupportsGet = true, Name = "page")]
    public int RequestedPage { get; set; } = 1;

    public IReadOnlyList<MediaFile> Items { get; private set; } = [];
    public int TotalCount { get; private set; }
    public int CurrentPage { get; private set; } = 1;
    public int TotalPages { get; private set; } = 1;
    public long TotalBytes { get; private set; }

    public string PageHref(int page)
    {
        var parts = new List<string>();
        if (!string.IsNullOrWhiteSpace(Query)) parts.Add("q=" + Uri.EscapeDataString(Query));
        if (page > 1) parts.Add("page=" + page);
        return "/admin/media" + (parts.Count > 0 ? "?" + string.Join("&", parts) : "");
    }

    public async Task OnGetAsync(CancellationToken ct)
    {
        var query = Filtered();

        TotalCount = await query.CountAsync(ct);
        TotalBytes = await db.MediaFiles.SumAsync(m => m.SizeBytes, ct);
        TotalPages = Math.Max(1, (int)Math.Ceiling(TotalCount / (double)PerPage));
        CurrentPage = Math.Clamp(RequestedPage < 1 ? 1 : RequestedPage, 1, TotalPages);

        Items = await query
            .OrderByDescending(m => m.UploadedAt)
            .Skip((CurrentPage - 1) * PerPage)
            .Take(PerPage)
            .ToListAsync(ct);
    }

    /// <summary>JSON feed for the picker dialog.</summary>
    public async Task<IActionResult> OnGetListAsync(string? q, bool imagesOnly, CancellationToken ct)
    {
        Query = q;

        var query = Filtered();
        if (imagesOnly) query = query.Where(m => m.ContentType.StartsWith("image/"));

        var items = await query
            .OrderByDescending(m => m.UploadedAt)
            .Take(120)
            .Select(m => new
            {
                m.Id,
                m.OriginalName,
                m.StoredName,
                m.ContentType,
                m.SizeBytes,
                m.AltText,
                m.Width,
                m.Height,
                url = "/uploads/" + m.StoredName,
                isImage = m.ContentType.StartsWith("image/"),
            })
            .ToListAsync(ct);

        return new JsonResult(new { items });
    }

    public async Task<IActionResult> OnPostUploadAsync(List<IFormFile> files, string? altText, CancellationToken ct)
    {
        if (files.Count == 0)
        {
            if (WantsJson()) return BadRequest(new { error = "No file was selected." });

            TempData["Error"] = "No file was selected.";
            return RedirectToPage();
        }

        var saved = new List<object>();
        var errors = new List<string>();

        foreach (var file in files)
        {
            var result = await storage.SaveAsync(file, files.Count == 1 ? altText : null, ct);
            if (result is { Ok: true, File: not null })
            {
                saved.Add(new
                {
                    result.File.Id,
                    result.File.OriginalName,
                    url = result.File.Url,
                    isImage = result.File.IsImage,
                    result.File.AltText,
                });
            }
            else
            {
                errors.Add($"{file.FileName}: {result.Error}");
            }
        }

        if (WantsJson())
            return new JsonResult(new { saved, errors });

        if (saved.Count > 0) TempData["Success"] = $"Uploaded {saved.Count} file(s).";
        if (errors.Count > 0) TempData["Error"] = string.Join(" ", errors);
        return RedirectToPage();
    }

    public async Task<IActionResult> OnPostRenameAsync(int id, string name, CancellationToken ct)
    {
        var result = await storage.RenameAsync(id, name, ct);
        if (result.Ok) TempData["Success"] = "Renamed.";
        else TempData["Error"] = result.Error;

        return RedirectToPage(new { q = Query, page = RequestedPage });
    }

    public async Task<IActionResult> OnPostAltTextAsync(int id, string? altText, CancellationToken ct)
    {
        await storage.UpdateAltTextAsync(id, altText, ct);
        TempData["Success"] = "Alt text updated.";
        return RedirectToPage(new { q = Query, page = RequestedPage });
    }

    public async Task<IActionResult> OnPostDeleteAsync(int id, CancellationToken ct)
    {
        var result = await storage.DeleteAsync(id, ct);
        if (result.Ok) TempData["Success"] = "File deleted.";
        else TempData["Error"] = $"Could not delete that file. {result.Error}";

        return RedirectToPage(new { q = Query, page = RequestedPage });
    }

    private IQueryable<MediaFile> Filtered()
    {
        var query = db.MediaFiles.AsNoTracking().AsQueryable();

        if (!string.IsNullOrWhiteSpace(Query))
        {
            var term = Query.Trim();
            query = query.Where(m =>
                EF.Functions.Like(m.OriginalName, $"%{term}%") ||
                EF.Functions.Like(m.StoredName, $"%{term}%") ||
                (m.AltText != null && EF.Functions.Like(m.AltText, $"%{term}%")));
        }

        return query;
    }

    /// <summary>The picker posts with fetch and expects JSON; the library page posts a plain form.</summary>
    private bool WantsJson() =>
        Request.Headers.Accept.ToString().Contains("application/json", StringComparison.OrdinalIgnoreCase);
}
