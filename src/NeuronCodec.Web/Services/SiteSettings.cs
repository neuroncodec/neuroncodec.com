using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Options;
using NeuronCodec.Web.Data;
using NeuronCodec.Web.Data.Entities;

namespace NeuronCodec.Web.Services;

/// <summary>
/// Reads and writes the <c>SiteSettings</c> key/value table, caching the whole (tiny) set so the
/// public pages do not hit the database for every meta tag.
/// </summary>
public class SiteSettings(AppDbContext db, IMemoryCache cache, IOptions<NeuronCodecOptions> options)
{
    private const string CacheKey = "site-settings";
    private readonly NeuronCodecOptions _options = options.Value;

    private Dictionary<string, string> Load() =>
        cache.GetOrCreate(CacheKey, entry =>
        {
            entry.AbsoluteExpirationRelativeToNow = TimeSpan.FromMinutes(10);
            return db.SiteSettings.AsNoTracking().ToDictionary(s => s.Key, s => s.Value);
        })!;

    public string? Get(string key) => Load().GetValueOrDefault(key);

    public string Get(string key, string fallback)
    {
        var value = Get(key);
        return string.IsNullOrWhiteSpace(value) ? fallback : value;
    }

    public bool GetBool(string key, bool fallback = true) =>
        bool.TryParse(Get(key), out var parsed) ? parsed : fallback;

    public int? GetInt(string key) =>
        int.TryParse(Get(key), out var parsed) ? parsed : null;

    public async Task SetAsync(string key, string? value, CancellationToken ct = default)
    {
        var existing = await db.SiteSettings.FindAsync([key], ct);
        if (string.IsNullOrWhiteSpace(value))
        {
            if (existing is not null) db.SiteSettings.Remove(existing);
        }
        else if (existing is null)
        {
            db.SiteSettings.Add(new SiteSetting { Key = key, Value = value });
        }
        else
        {
            existing.Value = value;
        }

        await db.SaveChangesAsync(ct);
        cache.Remove(CacheKey);
    }

    public async Task SetManyAsync(IReadOnlyDictionary<string, string?> values, CancellationToken ct = default)
    {
        foreach (var (key, value) in values)
        {
            var existing = await db.SiteSettings.FindAsync([key], ct);
            if (string.IsNullOrWhiteSpace(value))
            {
                if (existing is not null) db.SiteSettings.Remove(existing);
            }
            else if (existing is null)
            {
                db.SiteSettings.Add(new SiteSetting { Key = key, Value = value });
            }
            else
            {
                existing.Value = value;
            }
        }

        await db.SaveChangesAsync(ct);
        cache.Remove(CacheKey);
    }

    public string SiteName => Get(SettingKeys.SiteName, _options.Site.Name);
    public string Tagline => Get(SettingKeys.SiteTagline, _options.Site.Tagline);

    /// <summary>
    /// Configured absolute origin, without a trailing slash. Empty when unset, in which case
    /// callers fall back to the current request's origin.
    /// </summary>
    public string ConfiguredBaseUrl =>
        (Get(SettingKeys.BaseUrl) ?? _options.Site.BaseUrl ?? "").TrimEnd('/');
}
