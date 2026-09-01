using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Localization;
using Microsoft.Extensions.Options;
using PowerDNS_Web;
using Serilog;
using System.Globalization;

var builder = WebApplication.CreateBuilder(args);

var logDirectory = Path.Combine(Directory.GetCurrentDirectory(), "logs");
if (!Directory.Exists(logDirectory))
{
    Directory.CreateDirectory(logDirectory);
}

// LOGGING
Log.Logger = new LoggerConfiguration()
    .MinimumLevel.Warning()
    .WriteTo.File(Path.Combine(logDirectory, "error_log_.log"), rollingInterval: RollingInterval.Minute)
    .CreateLogger();

builder.Host.UseSerilog();

// ===== Localization =====
builder.Services.AddLocalization(opts => opts.ResourcesPath = "Resources");

var supportedCultures = new[] { new CultureInfo("ru"), new CultureInfo("en") };
builder.Services.Configure<RequestLocalizationOptions>(opts =>
{
    opts.DefaultRequestCulture = new RequestCulture("ru");
    opts.SupportedCultures = supportedCultures;
    opts.SupportedUICultures = supportedCultures;

    opts.RequestCultureProviders.Insert(0, new CookieRequestCultureProvider());
});

// ===== Auth through Cookies =====
builder.Services.AddAuthentication(CookieAuthenticationDefaults.AuthenticationScheme)
    .AddCookie(options =>
    {
        options.LoginPath = "/login";
        options.AccessDeniedPath = "/access-denied";
    });

// ===== Razor Pages =====
builder.Services
    .AddRazorPages(options =>
    {
        options.Conventions.AllowAnonymousToPage("/login");
        options.Conventions.AllowAnonymousToPage("/access-denied");
        options.Conventions.AllowAnonymousToPage("/Error");
        options.Conventions.AllowAnonymousToPage("/SetCulture");
    })
    .AddViewLocalization()
    .AddDataAnnotationsLocalization();

// ===== App services =====
builder.Services.AddSingleton<Functions>();
builder.Services.AddHttpClient();

// ===== Authorization =====
builder.Services.AddAuthorization(options =>
{
    options.FallbackPolicy = new AuthorizationPolicyBuilder()
        .RequireAuthenticatedUser()
        .Build();
});

var app = builder.Build();

// ===== Init DB =====
using (var scope = app.Services.CreateScope())
{
    var fn = scope.ServiceProvider.GetRequiredService<Functions>();
    fn.CheckDTExist();
}

var locOptions = app.Services.GetRequiredService<IOptions<RequestLocalizationOptions>>().Value;
app.UseRequestLocalization(locOptions);

app.UseStaticFiles();

app.UseRouting();

app.UseAuthentication();
app.UseAuthorization();

app.MapPost("/set-culture/{culture}", (string culture, HttpContext ctx) =>
{
    if (!supportedCultures.Any(item =>
            string.Equals(item.Name, culture, StringComparison.OrdinalIgnoreCase)))
    {
        return Results.BadRequest();
    }

    ctx.Response.Cookies.Append(
        CookieRequestCultureProvider.DefaultCookieName,
        CookieRequestCultureProvider.MakeCookieValue(new RequestCulture(culture)),
        new CookieOptions { Expires = DateTimeOffset.UtcNow.AddYears(1) });

    var referer = ctx.Request.Headers.Referer.ToString();
    if (!Uri.TryCreate(referer, UriKind.Absolute, out var refererUri) ||
        !string.Equals(refererUri.Scheme, ctx.Request.Scheme, StringComparison.OrdinalIgnoreCase) ||
        !string.Equals(refererUri.Authority, ctx.Request.Host.Value, StringComparison.OrdinalIgnoreCase))
    {
        return Results.Redirect("/");
    }

    return Results.Redirect(refererUri.PathAndQuery);
}).AllowAnonymous();

app.MapRazorPages();

app.Run();
