using Microsoft.AspNetCore.Mvc.RazorPages;
using NeuronCodec.Web.Services;

namespace NeuronCodec.Web.Pages;

public class ProjectsModel(SeoBuilder seo) : PageModel
{
    public void OnGet()
    {
        ViewData["NavCurrent"] = "projects";
        ViewData["Meta"] = seo.ForPage(
            "Projects",
            "NeuronCodec's open-source projects are in development. Repositories and releases will be listed here as they're published.",
            "/projects");
    }
}
