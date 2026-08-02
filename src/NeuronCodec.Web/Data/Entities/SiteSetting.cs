namespace NeuronCodec.Web.Data.Entities;

/// <summary>
/// Site-wide key/value settings, edited under Admin → SEO. Kept as a key/value table so new
/// settings do not need a migration.
/// </summary>
public class SiteSetting
{
    public string Key { get; set; } = "";
    public string Value { get; set; } = "";
}

public static class SettingKeys
{
    public const string SiteName = "site.name";
    public const string SiteTagline = "site.tagline";
    public const string BaseUrl = "site.baseUrl";
    public const string DefaultMetaDescription = "seo.defaultMetaDescription";
    public const string DefaultOgImageId = "seo.defaultOgImageId";
    public const string TwitterSite = "seo.twitterSite";
    public const string TwitterCreator = "seo.twitterCreator";
    public const string OrganizationName = "seo.organizationName";
    public const string OrganizationLogoUrl = "seo.organizationLogoUrl";
    public const string RobotsExtra = "seo.robotsExtra";
    public const string SitemapEnabled = "seo.sitemapEnabled";
}
