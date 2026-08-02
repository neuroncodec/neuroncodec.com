using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using NeuronCodec.Web.Data;
using NeuronCodec.Web.Data.Entities;
using NeuronCodec.Web.Services;

namespace NeuronCodec.Web.Pages.Admin.Seo;

public class SharingModel(SiteSettings settings, AppDbContext db) : SeoSettingsPage(settings)
{
    [BindProperty] public int? DefaultOgImageId { get; set; }
    [BindProperty] public string? TwitterSite { get; set; }
    [BindProperty] public string? TwitterCreator { get; set; }

    public MediaFile? DefaultOgImage { get; private set; }

    public async Task OnGetAsync(CancellationToken ct)
    {
        SetViewData();
        DefaultOgImageId = Settings.GetInt(SettingKeys.DefaultOgImageId);
        TwitterSite = Settings.Get(SettingKeys.TwitterSite);
        TwitterCreator = Settings.Get(SettingKeys.TwitterCreator);
        await LoadImageAsync(ct);
    }

    public async Task<IActionResult> OnPostAsync(CancellationToken ct)
    {
        SetViewData();

        await SaveAsync(new Dictionary<string, string?>
        {
            [SettingKeys.DefaultOgImageId] = DefaultOgImageId?.ToString(),
            [SettingKeys.TwitterSite] = NormalizeHandle(TwitterSite),
            [SettingKeys.TwitterCreator] = NormalizeHandle(TwitterCreator),
        }, ct);

        return RedirectToPage();
    }

    private async Task LoadImageAsync(CancellationToken ct)
    {
        if (DefaultOgImageId is { } id)
            DefaultOgImage = await db.MediaFiles.AsNoTracking().FirstOrDefaultAsync(m => m.Id == id, ct);
    }

    /// <summary>Twitter handles are stored with the leading @ that the meta tag expects.</summary>
    private static string? NormalizeHandle(string? value)
    {
        var trimmed = Trimmed(value);
        if (trimmed is null) return null;
        return trimmed.StartsWith('@') ? trimmed : "@" + trimmed;
    }

    private void SetViewData()
    {
        ViewData["AdminSection"] = "seo";
        ViewData["AdminPage"] = "seo-sharing";
    }
}
