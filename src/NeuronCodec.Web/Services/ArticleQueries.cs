using Microsoft.EntityFrameworkCore;
using NeuronCodec.Web.Data.Entities;

namespace NeuronCodec.Web.Services;

public static class ArticleQueries
{
    /// <summary>
    /// The single definition of "publicly visible": not a draft, and its publish moment has
    /// passed. A Scheduled article therefore goes live on time without anything having to run.
    /// </summary>
    public static IQueryable<Article> Live(this IQueryable<Article> source, DateTime now) =>
        source.Where(a => a.Status != ArticleStatus.Draft
                          && a.PublishedAt != null
                          && a.PublishedAt <= now);

    public static IQueryable<Article> NewestFirst(this IQueryable<Article> source) =>
        source.OrderByDescending(a => a.PublishedAt).ThenByDescending(a => a.Id);

    public static IQueryable<Article> WithCardData(this IQueryable<Article> source) =>
        source.Include(a => a.Category);
}

/// <summary>Everything an article card needs, matching the design's card markup.</summary>
public record ArticleCard(
    string Slug,
    string Title,
    string Excerpt,
    string TagName,
    string TagClass,
    DateTime PublishedAt,
    int ReadMinutes)
{
    public string DateLabel => Format.LongDate(PublishedAt);
    public string ReadLabel => $"{ReadMinutes} min read";
    public string Href => $"/articles/{Slug}";

    public static ArticleCard From(Article a) => new(
        a.Slug,
        a.Title,
        a.Excerpt,
        a.Category?.Name ?? "Article",
        a.Category?.TagClass ?? TagClasses.Neutral,
        a.PublishedAt ?? a.CreatedAt,
        a.ReadMinutes);
}

public static class Format
{
    private static readonly System.Globalization.CultureInfo Culture = new("en-US");

    /// <summary>"June 2, 2026" — the format the design's card meta and article header use.</summary>
    public static string LongDate(DateTime value) => value.ToString("MMMM d, yyyy", Culture);

    public static string ShortDateTime(DateTime value) => value.ToString("yyyy-MM-dd HH:mm", Culture);

    /// <summary>
    /// Formatted against the invariant culture, so the decimal separator does not follow the
    /// server's locale on an English-language site.
    /// </summary>
    public static string FileSize(long bytes) => bytes switch
    {
        < 1024 => string.Create(Invariant, $"{bytes} B"),
        < 1024 * 1024 => string.Create(Invariant, $"{bytes / 1024.0:0.#} KB"),
        < 1024L * 1024 * 1024 => string.Create(Invariant, $"{bytes / (1024.0 * 1024):0.#} MB"),
        _ => string.Create(Invariant, $"{bytes / (1024.0 * 1024 * 1024):0.##} GB"),
    };

    private static readonly System.Globalization.CultureInfo Invariant =
        System.Globalization.CultureInfo.InvariantCulture;
}
