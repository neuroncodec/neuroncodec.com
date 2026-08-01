using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using NeuronCodec.Web.Data;
using NeuronCodec.Web.Data.Entities;
using NeuronCodec.Web.Services;

namespace NeuronCodec.Web.Pages.Admin;

public class IndexModel(AppDbContext db, AdminAuth auth, TimeProvider clock) : PageModel
{
    public int PublishedCount { get; private set; }
    public int DraftCount { get; private set; }
    public int ScheduledCount { get; private set; }
    public int MediaCount { get; private set; }

    public IReadOnlyList<Article> Recent { get; private set; } = [];
    public IReadOnlyList<Article> UpcomingScheduled { get; private set; } = [];

    public string Username { get; private set; } = "";
    public bool TwoFactorEnabled { get; private set; }
    public int RecoveryCodesRemaining { get; private set; }

    public async Task OnGetAsync(CancellationToken ct)
    {
        var now = clock.GetUtcNow().UtcDateTime;

        PublishedCount = await db.Articles.CountAsync(a => a.Status == ArticleStatus.Published, ct);
        DraftCount = await db.Articles.CountAsync(a => a.Status == ArticleStatus.Draft, ct);
        ScheduledCount = await db.Articles.CountAsync(a => a.Status == ArticleStatus.Scheduled, ct);
        MediaCount = await db.MediaFiles.CountAsync(ct);

        Recent = await db.Articles
            .AsNoTracking()
            .OrderByDescending(a => a.UpdatedAt)
            .Take(8)
            .ToListAsync(ct);

        UpcomingScheduled = await db.Articles
            .AsNoTracking()
            .Where(a => a.Status == ArticleStatus.Scheduled && a.PublishedAt > now)
            .OrderBy(a => a.PublishedAt)
            .Take(5)
            .ToListAsync(ct);

        var admin = await auth.GetAdminAsync(ct);
        if (admin is not null)
        {
            Username = admin.Username;
            TwoFactorEnabled = admin.TwoFactorEnabled;
            RecoveryCodesRemaining = admin.RecoveryCodes.Count(r => r.UsedAt is null);
        }
    }
}
