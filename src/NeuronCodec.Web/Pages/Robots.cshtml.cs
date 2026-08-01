using System.Text;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using NeuronCodec.Web.Data.Entities;
using NeuronCodec.Web.Services;

namespace NeuronCodec.Web.Pages;

/// <summary>
/// robots.txt. The admin area and the error page are always disallowed; anything the admin adds
/// under SEO → robots extra is appended verbatim.
/// </summary>
public class RobotsModel(SeoBuilder seo, SiteSettings settings) : PageModel
{
    public IActionResult OnGet()
    {
        var body = new StringBuilder()
            .AppendLine("User-agent: *")
            .AppendLine("Disallow: /admin")
            .AppendLine("Disallow: /error")
            .AppendLine("Allow: /");

        var extra = settings.Get(SettingKeys.RobotsExtra);
        if (!string.IsNullOrWhiteSpace(extra))
            body.AppendLine().AppendLine(extra.Trim());

        if (settings.GetBool(SettingKeys.SitemapEnabled))
            body.AppendLine().AppendLine($"Sitemap: {seo.Absolute("/sitemap.xml")}");

        return Content(body.ToString(), "text/plain; charset=utf-8");
    }
}
