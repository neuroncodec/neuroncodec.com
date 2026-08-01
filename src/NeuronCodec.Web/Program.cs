using System.Security.Claims;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.FileProviders;
using NeuronCodec.Web.Data;
using NeuronCodec.Web.Services;

var builder = WebApplication.CreateBuilder(args);

builder.Services.Configure<NeuronCodecOptions>(
    builder.Configuration.GetSection(NeuronCodecOptions.SectionName));

var siteOptions = builder.Configuration
    .GetSection(NeuronCodecOptions.SectionName)
    .Get<NeuronCodecOptions>() ?? new NeuronCodecOptions();

var dataPath = Path.GetFullPath(siteOptions.DataPath);
Directory.CreateDirectory(dataPath);

// ── Persistence ──────────────────────────────────────────────────────────────
var connectionString = builder.Configuration.GetConnectionString("Default")
                       ?? $"Data Source={Path.Combine(dataPath, "neuroncodec.db")}";

builder.Services.AddDbContext<AppDbContext>(options =>
    options.UseSqlite(connectionString, sqlite => sqlite.CommandTimeout(30)));

// The key ring lives on the data volume so auth cookies and the protected TOTP secret survive
// a container rebuild.
builder.Services.AddDataProtection()
    .PersistKeysToFileSystem(new DirectoryInfo(Path.Combine(dataPath, "keys")))
    .SetApplicationName("NeuronCodec");

// ── Authentication ───────────────────────────────────────────────────────────
builder.Services
    .AddAuthentication(CookieAuthenticationDefaults.AuthenticationScheme)
    .AddCookie(CookieAuthenticationDefaults.AuthenticationScheme, options =>
    {
        options.Cookie.Name = "ncodec.auth";
        options.Cookie.HttpOnly = true;
        options.Cookie.SameSite = SameSiteMode.Lax;
        options.Cookie.SecurePolicy = CookieSecurePolicy.SameAsRequest;
        options.LoginPath = "/admin/login";
        options.LogoutPath = "/admin/logout";
        options.AccessDeniedPath = "/admin/login";
        options.SlidingExpiration = true;
        options.ExpireTimeSpan = TimeSpan.FromHours(8);
        options.Events.OnValidatePrincipal = ValidateSecurityStampAsync;
    })
    .AddCookie(AdminAuth.TwoFactorPendingScheme, options =>
    {
        options.Cookie.Name = "ncodec.2fa";
        options.Cookie.HttpOnly = true;
        options.Cookie.SameSite = SameSiteMode.Lax;
        options.Cookie.SecurePolicy = CookieSecurePolicy.SameAsRequest;
        options.ExpireTimeSpan = TimeSpan.FromMinutes(10);
    });

builder.Services.AddAuthorization();

// Holds the candidate TOTP secret between rendering the QR code and confirming the first code,
// so it is never round-tripped through the browser.
builder.Services.AddSession(options =>
{
    options.Cookie.Name = "ncodec.session";
    options.Cookie.HttpOnly = true;
    options.Cookie.IsEssential = true;
    options.Cookie.SameSite = SameSiteMode.Lax;
    options.Cookie.SecurePolicy = CookieSecurePolicy.SameAsRequest;
    options.IdleTimeout = TimeSpan.FromMinutes(30);
});

// The media picker posts with fetch and sends the token as a header.
builder.Services.AddAntiforgery(options => options.HeaderName = "RequestVerificationToken");

// ── Application services ─────────────────────────────────────────────────────
builder.Services.AddSingleton(TimeProvider.System);
builder.Services.AddSingleton<MarkdownRenderer>();
builder.Services.AddMemoryCache();
builder.Services.AddHttpContextAccessor();
builder.Services.AddScoped<SiteSettings>();
builder.Services.AddScoped<SeoBuilder>();
builder.Services.AddScoped<AdminAuth>();
builder.Services.AddScoped<MediaStorage>();
builder.Services.AddHostedService<StartupTasks>();
builder.Services.AddHostedService<ScheduledPublisher>();

builder.Services.AddScoped<RequirePasswordChangeFilter>();

builder.Services.AddRazorPages(options =>
{
    options.Conventions.AuthorizeFolder("/Admin");
    // Everything a signed-out admin needs in order to get in.
    options.Conventions.AllowAnonymousToPage("/Admin/Login");
    options.Conventions.AllowAnonymousToPage("/Admin/TwoFactor");
    options.Conventions.AllowAnonymousToPage("/Admin/Logout");

    options.Conventions.AddFolderApplicationModelConvention("/Admin",
        model => model.Filters.Add(new ServiceFilterAttribute(typeof(RequirePasswordChangeFilter))));
});

builder.Services.Configure<FormOptions>(options =>
{
    options.MultipartBodyLengthLimit = siteOptions.Uploads.MaxBytes;
});

builder.Services.Configure<ForwardedHeadersOptions>(options =>
{
    options.ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto;
    // The reverse proxy in front of the container is not known at build time.
    options.KnownIPNetworks.Clear();
    options.KnownProxies.Clear();
});

builder.Services.AddResponseCompression(options => options.EnableForHttps = true);

var app = builder.Build();

app.UseForwardedHeaders();

if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/error");
    app.UseHsts();
}

app.UseStatusCodePagesWithReExecute("/error", "?code={0}");
app.UseResponseCompression();

app.Use(async (context, next) =>
{
    var headers = context.Response.Headers;
    headers["X-Content-Type-Options"] = "nosniff";
    headers["Referrer-Policy"] = "strict-origin-when-cross-origin";
    headers["X-Frame-Options"] = "SAMEORIGIN";
    await next();
});

app.UseStaticFiles();

// Uploaded media lives on its own volume, outside wwwroot, and is served read-only from /uploads.
var uploadsPath = Path.GetFullPath(siteOptions.Uploads.Path);
Directory.CreateDirectory(uploadsPath);
app.UseStaticFiles(new StaticFileOptions
{
    FileProvider = new PhysicalFileProvider(uploadsPath),
    RequestPath = "/uploads",
    ServeUnknownFileTypes = false,
    OnPrepareResponse = ctx =>
    {
        ctx.Context.Response.Headers.CacheControl = "public,max-age=31536000,immutable";
        // Uploads are author-supplied; never let the browser sniff one into script.
        ctx.Context.Response.Headers.XContentTypeOptions = "nosniff";
        ctx.Context.Response.Headers.ContentSecurityPolicy = "sandbox";
    },
});

app.UseRouting();
app.UseAuthentication();
app.UseAuthorization();
app.UseSession();

app.MapRazorPages();

// Liveness probe for the container healthcheck. Touches the database so a broken or unmounted
// data volume shows up as unhealthy rather than as a silently serving container.
app.MapGet("/healthz", async (AppDbContext db, CancellationToken ct) =>
{
    try
    {
        return await db.Database.CanConnectAsync(ct)
            ? Results.Ok(new { status = "healthy" })
            : Results.Problem("The database is not reachable.", statusCode: StatusCodes.Status503ServiceUnavailable);
    }
    catch (Exception ex)
    {
        return Results.Problem(ex.Message, statusCode: StatusCodes.Status503ServiceUnavailable);
    }
}).ExcludeFromDescription();

app.Run();

// Rejects a cookie whose embedded security stamp no longer matches the account — the effect of a
// password change or a 2FA change on every other session.
static async Task ValidateSecurityStampAsync(CookieValidatePrincipalContext context)
{
    var db = context.HttpContext.RequestServices.GetRequiredService<AppDbContext>();

    var idClaim = context.Principal?.FindFirstValue(ClaimTypes.NameIdentifier);
    var stampClaim = context.Principal?.FindFirstValue(AdminAuth.SecurityStampClaim);

    if (!int.TryParse(idClaim, out var id) || !int.TryParse(stampClaim, out var stamp))
    {
        context.RejectPrincipal();
        return;
    }

    var current = await db.AdminUsers
        .Where(u => u.Id == id)
        .Select(u => (int?)u.SecurityStamp)
        .FirstOrDefaultAsync();

    if (current is null || current != stamp)
    {
        context.RejectPrincipal();
        await context.HttpContext.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);
    }
}
