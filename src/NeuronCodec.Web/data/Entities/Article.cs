namespace NeuronCodec.Web.Data.Entities;

public enum ArticleStatus
{
    Draft = 0,
    Published = 1,
    Scheduled = 2,
}

/// <remarks>
/// Every timestamp on this model is UTC. SQLite cannot order <c>DateTimeOffset</c> columns, and
/// the site has one author working in one timezone, so UTC <c>DateTime</c> is both translatable
/// and readable when inspecting the database file directly.
/// </remarks>
public class Article
{
    public int Id { get; set; }

    public string Slug { get; set; } = "";
    public string Title { get; set; } = "";

    /// <summary>Short summary. Rendered as the card body and as the article lede.</summary>
    public string Excerpt { get; set; } = "";

    /// <summary>Article source, authored in the admin markdown editor.</summary>
    public string BodyMarkdown { get; set; } = "";

    /// <summary>Cached HTML render of <see cref="BodyMarkdown"/>, refreshed on every save.</summary>
    public string BodyHtml { get; set; } = "";

    public ArticleStatus Status { get; set; } = ArticleStatus.Draft;

    /// <summary>
    /// When the article becomes publicly visible, in UTC. Required for Published and Scheduled;
    /// a Scheduled article whose date has passed is treated as published.
    /// </summary>
    public DateTime? PublishedAt { get; set; }

    /// <summary>Estimated reading time in minutes. Auto-computed on save unless overridden.</summary>
    public int ReadMinutes { get; set; }
    public bool ReadMinutesOverridden { get; set; }

    /// <summary>Optional hero image, shown on the article page and used as the default OG image.</summary>
    public int? FeaturedMediaId { get; set; }
    public MediaFile? FeaturedMedia { get; set; }

    /// <summary>The single category whose tag pill appears on article cards.</summary>
    public int? CategoryId { get; set; }
    public Category? Category { get; set; }

    public List<ArticleTag> ArticleTags { get; set; } = [];

    // ── SEO ──────────────────────────────────────────────────────────────────
    public string? MetaTitle { get; set; }
    public string? MetaDescription { get; set; }
    public string? CanonicalUrl { get; set; }
    public string? OgTitle { get; set; }
    public string? OgDescription { get; set; }
    public int? OgImageId { get; set; }
    public MediaFile? OgImage { get; set; }
    public string TwitterCardType { get; set; } = "summary_large_image";
    public bool NoIndex { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;

    /// <summary>
    /// True when the article should be visible to the public at <paramref name="now"/>.
    /// Mirrors the filter in <see cref="Services.ArticleQueries.Live"/>.
    /// </summary>
    public bool IsLive(DateTime now) =>
        Status != ArticleStatus.Draft && PublishedAt.HasValue && PublishedAt.Value <= now;
}
