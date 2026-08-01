using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using NeuronCodec.Web.Services;

namespace NeuronCodec.Web.Pages;

[ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
public class ErrorModel(SeoBuilder seo) : PageModel
{
    [BindProperty(SupportsGet = true, Name = "code")]
    public int? Code { get; set; }

    public string Heading { get; private set; } = "Something went wrong";
    public string Detail { get; private set; } = "";

    public void OnGet()
    {
        (Heading, Detail) = Code switch
        {
            404 => ("Page not found", "That page doesn't exist, or it may have moved."),
            403 => ("Not allowed", "You don't have access to that page."),
            _ => ("Something went wrong", "An unexpected error occurred. Please try again."),
        };

        ViewData["NavCurrent"] = "";
        var meta = seo.ForPage(Heading, null, "/error");
        meta.NoIndex = true;
        ViewData["Meta"] = meta;
    }
}
