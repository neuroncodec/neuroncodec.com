using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using NeuronCodec.Web.Data;
using NeuronCodec.Web.Data.Entities;

namespace NeuronCodec.Web.Services;

/// <summary>
/// Applies migrations, makes sure the data and uploads directories exist, and seeds the admin
/// account from configuration when the database has none.
/// </summary>
public class StartupTasks(
    IServiceProvider services,
    IOptions<NeuronCodecOptions> options,
    IHostEnvironment environment,
    ILogger<StartupTasks> logger) : IHostedService
{
    private readonly NeuronCodecOptions _options = options.Value;

    public async Task StartAsync(CancellationToken ct)
    {
        Directory.CreateDirectory(Path.GetFullPath(_options.DataPath));
        Directory.CreateDirectory(Path.GetFullPath(_options.Uploads.Path));

        using var scope = services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        await db.Database.MigrateAsync(ct);
        await SeedAdminAsync(db, ct);
    }

    public Task StopAsync(CancellationToken ct) => Task.CompletedTask;

    private async Task SeedAdminAsync(AppDbContext db, CancellationToken ct)
    {
        if (await db.AdminUsers.AnyAsync(ct))
        {
            logger.LogInformation("Admin account already present; skipping seed.");
            return;
        }

        var username = string.IsNullOrWhiteSpace(_options.Admin.Username) ? "admin" : _options.Admin.Username.Trim();
        var password = _options.Admin.Password;

        if (string.IsNullOrWhiteSpace(password))
        {
            if (environment.IsDevelopment())
            {
                // Local runs should not need a configured secret to get started.
                password = "ChangeMe!Dev123";
                logger.LogWarning(
                    "No admin password configured. Seeding the development default for user '{Username}'. " +
                    "Set NeuronCodec__Admin__Password before running anywhere else.", username);
            }
            else
            {
                throw new InvalidOperationException(
                    "No admin account exists and NeuronCodec__Admin__Password is not set. " +
                    "Set it so the first admin can be created, then change the password after signing in.");
            }
        }

        db.AdminUsers.Add(new AdminUser
        {
            Username = username,
            PasswordHash = PasswordHasher.Hash(password),
            // The seed value lives in an env var or compose file, so it is treated as compromised
            // from the start: the admin area blocks everything until it is replaced.
            MustChangePassword = true,
        });

        await db.SaveChangesAsync(ct);
        logger.LogWarning(
            "Seeded admin account '{Username}' from configuration. " +
            "You must change this password on first sign-in.", username);
    }
}

/// <summary>
/// Flips Scheduled articles to Published once their moment passes. Public visibility does not
/// depend on this running — <see cref="ArticleQueries.Live"/> already accounts for the date —
/// but it keeps the admin list showing the real state.
/// </summary>
public class ScheduledPublisher(IServiceProvider services, TimeProvider clock, ILogger<ScheduledPublisher> logger)
    : BackgroundService
{
    private static readonly TimeSpan Interval = TimeSpan.FromMinutes(1);

    protected override async Task ExecuteAsync(CancellationToken ct)
    {
        using var timer = new PeriodicTimer(Interval, clock);

        while (!ct.IsCancellationRequested)
        {
            try
            {
                await PromoteDueArticlesAsync(ct);
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Scheduled publishing sweep failed; will retry.");
            }

            if (!await timer.WaitForNextTickAsync(ct)) break;
        }
    }

    private async Task PromoteDueArticlesAsync(CancellationToken ct)
    {
        using var scope = services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        var now = clock.GetUtcNow().UtcDateTime;
        var due = await db.Articles
            .Where(a => a.Status == ArticleStatus.Scheduled && a.PublishedAt != null && a.PublishedAt <= now)
            .ToListAsync(ct);

        if (due.Count == 0) return;

        foreach (var article in due) article.Status = ArticleStatus.Published;

        await db.SaveChangesAsync(ct);
        logger.LogInformation("Published {Count} scheduled article(s).", due.Count);
    }
}
