using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using NeuronCodec.Web.Data;
using NeuronCodec.Web.Services;

namespace NeuronCodec.Web.Pages.Articles;

public class IndexModel(AppDbContext db, SeoBuilder seo, TimeProvider clock) : PageModel
{
    /// <summary>The design paginates the article index three cards to a page.</summary>
    private const int PerPage = 3;

    [BindProperty(SupportsGet = true, Name = "page")]
    public int RequestedPage { get; set; } = 1;

    public IReadOnlyList<ArticleCard> Items { get; private set; } = [];
    public int CurrentPage { get; private set; } = 1;
    public int TotalPages { get; private set; } = 1;

    public bool HasPrevious => CurrentPage > 1;
    public bool HasNext => CurrentPage < TotalPages;

    public string PageHref(int page) => page <= 1 ? "/articles" : $"/articles?page={page}";

    public async Task<IActionResult> OnGetAsync(CancellationToken ct)
    {
        var live = db.Articles.AsNoTracking().Live(clock.GetUtcNow().UtcDateTime);

        var total = await live.CountAsync(ct);
        TotalPages = Math.Max(1, (int)Math.Ceiling(total / (double)PerPage));
        CurrentPage = Math.Clamp(RequestedPage < 1 ? 1 : RequestedPage, 1, TotalPages);

        // Keep one canonical URL per page: ?page=1 and any out-of-range value redirect.
        if (RequestedPage != CurrentPage && !(RequestedPage == 0 && CurrentPage == 1))
            return Redirect(PageHref(CurrentPage));

        var articles = await live
            .WithCardData()
            .NewestFirst()
            .Skip((CurrentPage - 1) * PerPage)
            .Take(PerPage)
            .ToListAsync(ct);

        Items = [.. articles.Select(ArticleCard.From)];

        ViewData["NavCurrent"] = "articles";
        var meta = seo.ForPage(
            "Articles",
            "Research notes on neuroscience, biotech, and brain-computer interfaces.",
            PageHref(CurrentPage));

        if (CurrentPage > 1) meta.Title = $"Articles — page {CurrentPage} — NeuronCodec";
        ViewData["Meta"] = meta;

        return Page();
    }
}
