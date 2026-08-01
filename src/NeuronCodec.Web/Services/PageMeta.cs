namespace NeuronCodec.Web.Services;

/// <summary>
/// The SEO block for one page. Pages fill this in; <c>_Layout</c> renders it into the head.
/// </summary>
public class PageMeta
{
    /// <summary>Contents of &lt;title&gt;. Already includes the site suffix.</summary>
    public string Title { get; set; } = "NeuronCodec";

    public string? Description { get; set; }
    public string? CanonicalUrl { get; set; }
    public bool NoIndex { get; set; }

    public string? OgTitle { get; set; }
    public string? OgDescription { get; set; }
    public string? OgImageUrl { get; set; }
    public string OgType { get; set; } = "website";

    public string TwitterCardType { get; set; } = "summary_large_image";
    public string? TwitterSite { get; set; }
    public string? TwitterCreator { get; set; }

    /// <summary>Pre-serialised JSON-LD blocks, each emitted in its own script tag.</summary>
    public List<string> JsonLd { get; } = [];

    public string ResolvedOgTitle => OgTitle ?? Title;
    public string? ResolvedOgDescription => OgDescription ?? Description;
}
