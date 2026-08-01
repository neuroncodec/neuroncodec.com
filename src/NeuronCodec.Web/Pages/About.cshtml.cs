using Microsoft.AspNetCore.Mvc.RazorPages;
using NeuronCodec.Web.Services;

namespace NeuronCodec.Web.Pages;

public class AboutModel(SeoBuilder seo) : PageModel
{
    public void OnGet()
    {
        ViewData["NavCurrent"] = "about";
        ViewData["Meta"] = seo.ForPage(
            "About",
            "NeuronCodec is an open-source foundation team focusing on neuroscience, biotech, and brain-computer interfaces.",
            "/about");
    }
}
