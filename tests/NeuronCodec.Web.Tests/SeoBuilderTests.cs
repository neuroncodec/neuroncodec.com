using NeuronCodec.Web.Services;

namespace NeuronCodec.Web.Tests;

public class AbsoluteUrlDetectionTests
{
    [Theory]
    [InlineData("http://neuroncodec.com")]
    [InlineData("https://neuroncodec.com")]
    [InlineData("https://neuroncodec.com/articles/decoding")]
    [InlineData("HTTPS://NEURONCODEC.COM/x")]
    public void Full_http_urls_are_absolute(string value)
    {
        Assert.True(SeoBuilder.IsAbsoluteHttpUrl(value));
    }

    /// <remarks>
    /// The rooted-path cases are the regression this guards. On Unix, a plain
    /// <c>Uri.TryCreate(value, UriKind.Absolute, …)</c> parses "/articles" as an absolute file
    /// URI, so treating that as "already absolute" silently stripped the origin from every
    /// canonical URL and sitemap entry in the Linux container.
    /// </remarks>
    [Theory]
    [InlineData("/")]
    [InlineData("/articles")]
    [InlineData("/articles/decoding-synaptic-spike-trains")]
    [InlineData("/sitemap.xml")]
    [InlineData("/uploads/logo.png")]
    [InlineData("articles")]
    [InlineData("")]
    [InlineData(null)]
    public void Site_relative_paths_are_not_absolute(string? value)
    {
        Assert.False(SeoBuilder.IsAbsoluteHttpUrl(value));
    }

    [Theory]
    [InlineData("file:///etc/passwd")]
    [InlineData("javascript:alert(1)")]
    [InlineData("mailto:hello@neuroncodec.com")]
    [InlineData("ftp://neuroncodec.com")]
    public void Non_http_schemes_are_not_treated_as_absolute_urls(string value)
    {
        Assert.False(SeoBuilder.IsAbsoluteHttpUrl(value));
    }
}
