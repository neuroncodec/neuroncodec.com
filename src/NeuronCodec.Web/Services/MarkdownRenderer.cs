using Ganss.Xss;
using Markdig;
using Markdig.Extensions.AutoIdentifiers;

namespace NeuronCodec.Web.Services;

/// <summary>
/// Renders article markdown to the HTML the public article page displays. Output is sanitised
/// even though the only author is the admin — it keeps a compromised session from turning into
/// stored XSS on every reader.
/// </summary>
public class MarkdownRenderer
{
    private readonly MarkdownPipeline _pipeline;
    private readonly HtmlSanitizer _sanitizer;

    public MarkdownRenderer()
    {
        _pipeline = new MarkdownPipelineBuilder()
            .UseAutoIdentifiers(AutoIdentifierOptions.GitHub)
            .UsePipeTables()
            .UseGridTables()
            .UseFootnotes()
            .UseAutoLinks()
            .UseTaskLists()
            .UseEmphasisExtras()
            .UseDefinitionLists()
            .UseAbbreviations()
            .UseGenericAttributes()
            .UseMediaLinks()
            .UseSmartyPants()
            .Build();

        _sanitizer = new HtmlSanitizer();
        _sanitizer.AllowedTags.Add("figure");
        _sanitizer.AllowedTags.Add("figcaption");
        // highlight.js writes its token classes onto <code>; the language-* hint comes from the fence.
        _sanitizer.AllowedAttributes.Add("class");
        _sanitizer.AllowedAttributes.Add("id");
        _sanitizer.AllowedAttributes.Add("loading");
        _sanitizer.AllowedSchemes.Add("mailto");
    }

    public string ToHtml(string? markdown) =>
        string.IsNullOrWhiteSpace(markdown)
            ? ""
            : _sanitizer.Sanitize(Markdown.ToHtml(markdown, _pipeline));

    /// <summary>Plain text of the markdown, used for excerpts and reading-time estimates.</summary>
    public static string ToPlainText(string? markdown) =>
        string.IsNullOrWhiteSpace(markdown) ? "" : Markdown.ToPlainText(markdown).Trim();

    /// <summary>
    /// Reading time at 200 words per minute, the figure the design's "N min read" label assumes.
    /// Always at least one minute.
    /// </summary>
    public static int EstimateReadMinutes(string? markdown)
    {
        var words = ToPlainText(markdown)
            .Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries)
            .Length;
        return Math.Max(1, (int)Math.Round(words / 200.0, MidpointRounding.AwayFromZero));
    }

    /// <summary>Trims plain text to a sentence-ish boundary, for auto-filling an empty excerpt.</summary>
    public static string Summarize(string? markdown, int maxChars = 240)
    {
        var text = ToPlainText(markdown).Replace('\n', ' ').Replace("  ", " ").Trim();
        if (text.Length <= maxChars) return text;

        var cut = text[..maxChars];
        var lastSpace = cut.LastIndexOf(' ');
        if (lastSpace > maxChars / 2) cut = cut[..lastSpace];
        return cut.TrimEnd(',', '.', ';', ':', ' ') + "…";
    }
}
