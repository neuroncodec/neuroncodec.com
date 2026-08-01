using NeuronCodec.Web.Data.Entities;
using NeuronCodec.Web.Services;

namespace NeuronCodec.Web.Tests;

public class SlugTests
{
    [Theory]
    [InlineData("Decoding Synaptic Spike Trains", "decoding-synaptic-spike-trains")]
    [InlineData("Writing to Memory: The Case for BCIs", "writing-to-memory-the-case-for-bcis")]
    [InlineData("  leading and trailing  ", "leading-and-trailing")]
    [InlineData("multiple---separators", "multiple-separators")]
    [InlineData("AI & Neuroscience", "ai-neuroscience")]
    [InlineData("Décodage à 100%", "decodage-a-100")]
    public void From_produces_a_url_safe_slug(string input, string expected)
    {
        Assert.Equal(expected, Slug.From(input));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("!!!")]
    public void From_falls_back_when_nothing_survives(string? input)
    {
        Assert.Equal("untitled", Slug.From(input));
    }

    [Fact]
    public void Unique_returns_the_candidate_when_it_is_free()
    {
        Assert.Equal("free-slug", Slug.Unique("free-slug", _ => false));
    }

    [Fact]
    public void Unique_appends_a_counter_on_collision()
    {
        var taken = new HashSet<string> { "taken", "taken-2" };

        Assert.Equal("taken-3", Slug.Unique("taken", taken.Contains));
    }
}

public class MarkdownRendererTests
{
    private readonly MarkdownRenderer _renderer = new();

    [Fact]
    public void ToHtml_renders_headings_and_paragraphs()
    {
        var html = _renderer.ToHtml("## The pipeline\n\nA decoder is a chain of problems.");

        Assert.Contains("<h2", html);
        Assert.Contains("The pipeline", html);
        Assert.Contains("<p>A decoder is a chain of problems.</p>", html);
    }

    [Fact]
    public void ToHtml_keeps_the_language_hint_that_drives_highlighting()
    {
        var html = _renderer.ToHtml("```python\nprint(1)\n```");

        Assert.Contains("language-python", html);
    }

    [Fact]
    public void ToHtml_strips_script_tags()
    {
        var html = _renderer.ToHtml("Before\n\n<script>alert('xss')</script>\n\nAfter");

        Assert.DoesNotContain("<script", html, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("alert(", html);
    }

    [Fact]
    public void ToHtml_strips_inline_event_handlers_and_javascript_urls()
    {
        var html = _renderer.ToHtml("<img src=\"x\" onerror=\"alert(1)\"> [x](javascript:alert(1))");

        Assert.DoesNotContain("onerror", html, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("javascript:", html, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void ToHtml_returns_empty_for_blank_input()
    {
        Assert.Equal("", _renderer.ToHtml(null));
        Assert.Equal("", _renderer.ToHtml("   "));
    }

    [Fact]
    public void EstimateReadMinutes_uses_200_words_per_minute()
    {
        var thousandWords = string.Join(' ', Enumerable.Repeat("word", 1000));

        Assert.Equal(5, MarkdownRenderer.EstimateReadMinutes(thousandWords));
    }

    [Fact]
    public void EstimateReadMinutes_never_returns_zero()
    {
        Assert.Equal(1, MarkdownRenderer.EstimateReadMinutes("one"));
        Assert.Equal(1, MarkdownRenderer.EstimateReadMinutes(""));
    }

    [Fact]
    public void Summarize_trims_at_a_word_boundary()
    {
        var summary = MarkdownRenderer.Summarize("## Heading\n\n" + string.Join(' ', Enumerable.Repeat("word", 200)), 60);

        Assert.True(summary.Length <= 61, $"Summary was {summary.Length} characters.");
        Assert.EndsWith("…", summary);
        Assert.DoesNotContain("#", summary);
    }

    [Fact]
    public void Summarize_returns_short_text_unchanged()
    {
        Assert.Equal("Short enough.", MarkdownRenderer.Summarize("Short enough."));
    }
}

public class ArticleVisibilityTests
{
    private static readonly DateTime Now = new(2026, 6, 2, 12, 0, 0, DateTimeKind.Utc);

    [Fact]
    public void A_draft_is_never_live()
    {
        var article = new Article { Status = ArticleStatus.Draft, PublishedAt = Now.AddDays(-1) };

        Assert.False(article.IsLive(Now));
    }

    [Fact]
    public void A_published_article_with_a_past_date_is_live()
    {
        var article = new Article { Status = ArticleStatus.Published, PublishedAt = Now.AddMinutes(-1) };

        Assert.True(article.IsLive(Now));
    }

    [Fact]
    public void A_published_article_with_no_date_is_not_live()
    {
        var article = new Article { Status = ArticleStatus.Published, PublishedAt = null };

        Assert.False(article.IsLive(Now));
    }

    [Fact]
    public void A_scheduled_article_goes_live_once_its_moment_passes()
    {
        var article = new Article { Status = ArticleStatus.Scheduled, PublishedAt = Now.AddMinutes(1) };

        Assert.False(article.IsLive(Now));
        Assert.True(article.IsLive(Now.AddMinutes(2)));
    }
}

public class TagClassTests
{
    [Theory]
    [InlineData("tag-accent")]
    [InlineData("tag-accent-2")]
    [InlineData("tag-neutral")]
    [InlineData("tag-outline")]
    public void Normalize_keeps_the_design_systems_pill_variants(string tagClass)
    {
        Assert.Equal(tagClass, TagClasses.Normalize(tagClass));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("tag-made-up")]
    [InlineData("' onmouseover='alert(1)")]
    public void Normalize_falls_back_for_anything_else(string? tagClass)
    {
        Assert.Equal(TagClasses.Accent, TagClasses.Normalize(tagClass));
    }
}

public class FormatTests
{
    [Fact]
    public void LongDate_matches_the_designs_card_meta_format()
    {
        Assert.Equal("June 2, 2026", Format.LongDate(new DateTime(2026, 6, 2, 0, 0, 0, DateTimeKind.Utc)));
    }

    [Theory]
    [InlineData(512L, "512 B")]
    [InlineData(2048L, "2 KB")]
    [InlineData(34603L, "33.8 KB")]
    [InlineData(5L * 1024 * 1024, "5 MB")]
    public void FileSize_scales_to_a_readable_unit(long bytes, string expected)
    {
        Assert.Equal(expected, Format.FileSize(bytes));
    }
}
