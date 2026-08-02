namespace NeuronCodec.Web.Data.Entities;

/// <summary>
/// A category owns the tag pill shown on article cards. <see cref="TagClass"/> is one of the
/// design system's four pill variants, so the pill colour is content, not markup.
/// </summary>
public class Category
{
    public int Id { get; set; }
    public string Name { get; set; } = "";
    public string Slug { get; set; } = "";
    public string? Description { get; set; }

    /// <summary>One of: tag-accent, tag-accent-2, tag-neutral, tag-outline.</summary>
    public string TagClass { get; set; } = TagClasses.Accent;

    public List<Article> Articles { get; set; } = [];
}

public static class TagClasses
{
    public const string Accent = "tag-accent";
    public const string Accent2 = "tag-accent-2";
    public const string Neutral = "tag-neutral";
    public const string Outline = "tag-outline";

    public static readonly string[] All = [Accent, Accent2, Neutral, Outline];

    public static string Normalize(string? value) =>
        All.Contains(value) ? value! : Accent;

    public static string Label(string tagClass) => tagClass switch
    {
        Accent => "Terracotta",
        Accent2 => "Sage",
        Neutral => "Neutral",
        Outline => "Outline",
        _ => tagClass,
    };
}

public class Tag
{
    public int Id { get; set; }
    public string Name { get; set; } = "";
    public string Slug { get; set; } = "";

    public List<ArticleTag> ArticleTags { get; set; } = [];
}

public class ArticleTag
{
    public int ArticleId { get; set; }
    public Article Article { get; set; } = null!;

    public int TagId { get; set; }
    public Tag Tag { get; set; } = null!;
}
