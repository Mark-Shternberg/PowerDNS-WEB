using Microsoft.AspNetCore.Diagnostics;
using Serilog;
using Microsoft.AspNetCore.Authentication.Cookies;

var builder = WebApplication.CreateBuilder(args);

// Create folder if doesn't exist
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

// Добавляем сервисы для аутентификации через Cookies
builder.Services.AddAuthentication(CookieAuthenticationDefaults.AuthenticationScheme)
    .AddCookie(options =>
    {
        options.LoginPath = "/login";  // Путь к странице входа
        options.AccessDeniedPath = "/access-denied";  // Путь к странице отказа в доступе
    });

builder.Services.AddRazorPages(options =>
{
    options.Conventions.AuthorizeFolder("/");
    options.Conventions.AllowAnonymousToPage("/login");
    options.Conventions.AllowAnonymousToPage("/access-denied");
    options.Conventions.AllowAnonymousToPage("/logout");
});

builder.Services.AddRazorPages();

var app = builder.Build();

if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler(errorApp =>
    {
        errorApp.Run(async context =>
        {
            var exceptionHandlerFeature = context.Features.Get<IExceptionHandlerFeature>();
            if (exceptionHandlerFeature?.Error != null)
            {
                Log.Error(exceptionHandlerFeature.Error, "An unhandled exception has occurred.");
            }

            Log.Information("Redirecting to the error page.");
            context.Response.Redirect("/Error");
        });
    });
}
else
{
    app.UseDeveloperExceptionPage();
}


app.UseStaticFiles();
app.UseRouting();

// Включаем аутентификацию и авторизацию
app.UseAuthentication();
app.UseAuthorization();

app.MapRazorPages();

app.Run();
