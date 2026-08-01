namespace NeuronCodec.Web.Services;

/// <summary>Bound from the <c>NeuronCodec</c> configuration section.</summary>
public class NeuronCodecOptions
{
    public const string SectionName = "NeuronCodec";

    public AdminSeedOptions Admin { get; set; } = new();
    public UploadOptions Uploads { get; set; } = new();
    public SiteOptions Site { get; set; } = new();

    /// <summary>
    /// Directory holding the SQLite file and the Data Protection key ring. Deliberately not
    /// "data" — on a case-insensitive filesystem that collides with the project's own Data/ folder.
    /// </summary>
    public string DataPath { get; set; } = ".runtime/data";
}

public class AdminSeedOptions
{
    /// <summary>Username for the account created on an empty database.</summary>
    public string Username { get; set; } = "admin";

    /// <summary>
    /// Seed password. Only used when no admin exists yet; the account is then flagged
    /// <c>MustChangePassword</c> so the value cannot stay in service.
    /// </summary>
    public string? Password { get; set; }
}

public class UploadOptions
{
    public string Path { get; set; } = ".runtime/uploads";

    /// <summary>Rejected above this size. 25 MB by default.</summary>
    public long MaxBytes { get; set; } = 25 * 1024 * 1024;

    public string[] AllowedExtensions { get; set; } =
    [
        ".jpg", ".jpeg", ".png", ".gif", ".webp", ".avif", ".svg",
        ".pdf", ".txt", ".md", ".csv", ".json", ".zip",
        ".mp4", ".webm", ".mp3", ".wav",
    ];
}

public class SiteOptions
{
    /// <summary>
    /// Absolute origin used for canonical URLs, sitemap entries and OG tags. Falls back to the
    /// request's own origin when left empty.
    /// </summary>
    public string? BaseUrl { get; set; }

    public string Name { get; set; } = "NeuronCodec";
    public string Tagline { get; set; } = "open-source foundation";
}
