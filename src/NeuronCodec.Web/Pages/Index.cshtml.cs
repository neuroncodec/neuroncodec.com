using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using NeuronCodec.Web.Data;
using NeuronCodec.Web.Services;

namespace NeuronCodec.Web.Pages;

public class IndexModel(AppDbContext db, SeoBuilder seo, TimeProvider clock) : PageModel
{
    /// <summary>The design's home grid shows the six newest articles.</summary>
    private const int LatestCount = 6;

    public IReadOnlyList<ArticleCard> Latest { get; private set; } = [];

    public async Task OnGetAsync(CancellationToken ct)
    {
        var articles = await db.Articles
            .AsNoTracking()
            .Live(clock.GetUtcNow().UtcDateTime)
            .WithCardData()
            .NewestFirst()
            .Take(LatestCount)
            .ToListAsync(ct);

        Latest = [.. articles.Select(ArticleCard.From)];

        ViewData["NavCurrent"] = "home";
        ViewData["Meta"] = seo.ForHome(
            "NeuronCodec — open-source neuroscience & BCI research",
            "Research notes and open-source work on neural decoding, memory-write interfaces, and laboratory automation with AI agents.");
    }
}
