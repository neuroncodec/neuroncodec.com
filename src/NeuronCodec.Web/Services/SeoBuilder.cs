using System.Text.Json;
using System.Text.Json.Serialization;
using NeuronCodec.Web.Data;
using NeuronCodec.Web.Data.Entities;

namespace NeuronCodec.Web.Services;

/// <summary>
/// Turns site settings and an article into the page's meta tags and JSON-LD. Per-article SEO
/// fields win; anything left blank falls back to the article's own content, then to site defaults.
/// </summary>
public class SeoBuilder(SiteSettings settings, AppDbContext db, IHttpContextAccessor accessor)
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        // The default encoder escapes <, > and &, which is what makes the result safe to drop
        // straight into a <script> block — a title containing "</script>" cannot break out.
        Encoder = System.Text.Encodings.Web.JavaScriptEncoder.Default,
        WriteIndented = false,
    };

    /// <summary>
    /// The site's absolute origin: the configured base URL when set, otherwise the origin of the
    /// current request. Never has a trailing slash.
    /// </summary>
    public string BaseUrl
    {
        get
        {
            var configured = settings.ConfiguredBaseUrl;
            if (!string.IsNullOrWhiteSpace(configured)) return configured;

            var request = accessor.HttpContext?.Request;
            return request is null ? "" : $"{request.Scheme}://{request.Host}";
        }
    }

    public string Absolute(string relativePath)
    {
        if (string.IsNullOrWhiteSpace(relativePath)) return BaseUrl;
        if (IsAbsoluteHttpUrl(relativePath)) return relativePath;
        return BaseUrl + "/" + relativePath.TrimStart('/');
    }

    /// <summary>
    /// True only for a full http(s) URL.
    /// <para>
    /// Deliberately not a bare <c>Uri.TryCreate(value, UriKind.Absolute, …)</c>: on Unix that
    /// call succeeds for a rooted path like "/articles", parsing it as an absolute *file* URI.
    /// Relying on it made every canonical URL, sitemap entry and share tag drop its origin in
    /// the Linux container while looking correct on Windows.
    /// </para>
    /// </summary>
    public static bool IsAbsoluteHttpUrl(string? value) =>
        Uri.TryCreate(value, UriKind.Absolute, out var parsed)
        && (parsed.Scheme == Uri.UriSchemeHttp || parsed.Scheme == Uri.UriSchemeHttps);

    public PageMeta ForPage(string title, string? description, string path, string? ogImageUrl = null)
    {
        var meta = new PageMeta
        {
            Title = $"{title} — {settings.SiteName}",
            Description = description ?? settings.Get(SettingKeys.DefaultMetaDescription),
            CanonicalUrl = Absolute(path),
            OgImageUrl = ogImageUrl,
            TwitterSite = settings.Get(SettingKeys.TwitterSite),
            TwitterCreator = settings.Get(SettingKeys.TwitterCreator),
        };

        ApplyDefaultOgImage(meta);
        return meta;
    }

    public PageMeta ForHome(string title, string? description)
    {
        var meta = ForPage(title, description, "/");
        // The home page carries the site title verbatim rather than "Home — NeuronCodec".
        meta.Title = title;
        meta.JsonLd.Add(WebSiteJsonLd());
        meta.JsonLd.Add(OrganizationJsonLd());
        return meta;
    }

    public PageMeta ForArticle(Article article, IEnumerable<string> tagNames)
    {
        // Materialised once — the list is read by both the meta tags and the JSON-LD below.
        var tags = tagNames as IReadOnlyList<string> ?? [.. tagNames];
        var path = $"/articles/{article.Slug}";
        var description = FirstNonBlank(article.MetaDescription, article.Excerpt,
            settings.Get(SettingKeys.DefaultMetaDescription));

        var meta = new PageMeta
        {
            Title = FirstNonBlank(article.MetaTitle, $"{article.Title} — {settings.SiteName}")!,
            Description = description,
            CanonicalUrl = string.IsNullOrWhiteSpace(article.CanonicalUrl)
                ? Absolute(path)
                : article.CanonicalUrl,
            NoIndex = article.NoIndex,
            OgType = "article",
            OgTitle = FirstNonBlank(article.OgTitle, article.Title),
            OgDescription = FirstNonBlank(article.OgDescription, description),
            TwitterCardType = string.IsNullOrWhiteSpace(article.TwitterCardType)
                ? "summary_large_image"
                : article.TwitterCardType,
            TwitterSite = settings.Get(SettingKeys.TwitterSite),
            TwitterCreator = settings.Get(SettingKeys.TwitterCreator),
        };

        var image = article.OgImage ?? article.FeaturedMedia;
        if (image is not null)
        {
            meta.OgImageUrl = Absolute(image.Url);
            meta.OgImageAlt = image.AltText;
        }

        ApplyDefaultOgImage(meta);

        meta.PublishedTime = article.PublishedAt;
        meta.ModifiedTime = article.UpdatedAt;
        meta.Section = article.Category?.Name;
        meta.Tags = tags;

        meta.JsonLd.Add(ArticleJsonLd(article, tags, Absolute(path), meta.OgImageUrl));
        meta.JsonLd.Add(BreadcrumbJsonLd(article));
        return meta;
    }

    /// <summary>Falls back to the site-wide OG image when the page did not supply one.</summary>
    private void ApplyDefaultOgImage(PageMeta meta)
    {
        if (string.IsNullOrWhiteSpace(meta.OgImageUrl) && DefaultOgImageUrl is not null)
            meta.OgImageUrl = Absolute(DefaultOgImageUrl);
    }

    private bool _defaultOgResolved;
    private string? _defaultOgImageUrl;

    /// <summary>
    /// Relative URL of the media file chosen as the site default OG image, or null when unset.
    /// Resolved once per request.
    /// </summary>
    private string? DefaultOgImageUrl
    {
        get
        {
            if (_defaultOgResolved) return _defaultOgImageUrl;
            _defaultOgResolved = true;

            var id = settings.GetInt(SettingKeys.DefaultOgImageId);
            if (id is null) return null;

            var storedName = db.MediaFiles
                .Where(m => m.Id == id)
                .Select(m => m.StoredName)
                .FirstOrDefault();

            _defaultOgImageUrl = storedName is null ? null : "/uploads/" + storedName;
            return _defaultOgImageUrl;
        }
    }

    private string WebSiteJsonLd() => Node(new Dictionary<string, object?>
    {
        ["@context"] = "https://schema.org",
        ["@type"] = "WebSite",
        ["name"] = settings.SiteName,
        ["url"] = BaseUrl,
        ["description"] = settings.Get(SettingKeys.DefaultMetaDescription),
        ["potentialAction"] = new Dictionary<string, object?>
        {
            ["@type"] = "SearchAction",
            ["target"] = $"{BaseUrl}/articles?q={{search_term_string}}",
            ["query-input"] = "required name=search_term_string",
        },
    });

    private string OrganizationJsonLd()
    {
        var logo = settings.Get(SettingKeys.OrganizationLogoUrl);
        var sameAs = (settings.Get(SettingKeys.OrganizationSameAs) ?? "")
            .Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Where(IsAbsoluteHttpUrl)
            .ToArray();

        return Node(new Dictionary<string, object?>
        {
            ["@context"] = "https://schema.org",
            ["@type"] = "Organization",
            ["name"] = settings.Get(SettingKeys.OrganizationName, settings.SiteName),
            ["url"] = BaseUrl,
            ["logo"] = string.IsNullOrWhiteSpace(logo) ? Absolute("/logo/mark.svg") : Absolute(logo),
            // Ties this site to the same organisation on other platforms.
            ["sameAs"] = sameAs.Length > 0 ? sameAs : null,
        });
    }

    private string ArticleJsonLd(Article article, IEnumerable<string> tagNames, string url, string? imageUrl)
    {
        var organization = settings.Get(SettingKeys.OrganizationName, settings.SiteName);
        var keywords = tagNames.ToArray();

        return Node(new Dictionary<string, object?>
        {
            ["@context"] = "https://schema.org",
            ["@type"] = "BlogPosting",
            ["mainEntityOfPage"] = new Dictionary<string, object?>
            {
                ["@type"] = "WebPage",
                ["@id"] = url,
            },
            ["headline"] = article.Title,
            ["description"] = FirstNonBlank(article.MetaDescription, article.Excerpt),
            ["image"] = imageUrl is null ? null : new[] { imageUrl },
            ["datePublished"] = article.PublishedAt?.ToString("o"),
            ["dateModified"] = article.UpdatedAt.ToString("o"),
            ["author"] = new Dictionary<string, object?>
            {
                ["@type"] = "Organization",
                ["name"] = organization,
                ["url"] = BaseUrl,
            },
            ["publisher"] = new Dictionary<string, object?>
            {
                ["@type"] = "Organization",
                ["name"] = organization,
                ["logo"] = new Dictionary<string, object?>
                {
                    ["@type"] = "ImageObject",
                    ["url"] = Absolute(settings.Get(SettingKeys.OrganizationLogoUrl, "/logo/mark.svg")),
                },
            },
            ["articleSection"] = article.Category?.Name,
            ["keywords"] = keywords.Length > 0 ? string.Join(", ", keywords) : null,
            ["wordCount"] = CountWords(article.BodyMarkdown),
            ["timeRequired"] = $"PT{Math.Max(1, article.ReadMinutes)}M",
        });
    }

    private string BreadcrumbJsonLd(Article article) => Node(new Dictionary<string, object?>
    {
        ["@context"] = "https://schema.org",
        ["@type"] = "BreadcrumbList",
        ["itemListElement"] = new object[]
        {
            new Dictionary<string, object?>
            {
                ["@type"] = "ListItem", ["position"] = 1, ["name"] = "Home", ["item"] = BaseUrl,
            },
            new Dictionary<string, object?>
            {
                ["@type"] = "ListItem", ["position"] = 2, ["name"] = "Articles",
                ["item"] = Absolute("/articles"),
            },
            new Dictionary<string, object?>
            {
                ["@type"] = "ListItem", ["position"] = 3, ["name"] = article.Title,
                ["item"] = Absolute($"/articles/{article.Slug}"),
            },
        },
    });

    /// <summary>
    /// Serialises a JSON-LD node with its null-valued entries dropped.
    /// <para>
    /// <c>DefaultIgnoreCondition.WhenWritingNull</c> only applies to object properties, not to
    /// dictionary values, so without this the output carries entries like
    /// <c>"sameAs":null</c> for every unset field.
    /// </para>
    /// </summary>
    private static string Node(Dictionary<string, object?> values) =>
        JsonSerializer.Serialize(
            values.Where(pair => pair.Value is not null).ToDictionary(pair => pair.Key, pair => pair.Value),
            JsonOptions);

    private static int CountWords(string? markdown) =>
        MarkdownRenderer.ToPlainText(markdown)
            .Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries)
            .Length;

    private static string? FirstNonBlank(params string?[] values) =>
        values.FirstOrDefault(v => !string.IsNullOrWhiteSpace(v));
}
