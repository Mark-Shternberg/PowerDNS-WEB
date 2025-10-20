using Microsoft.AspNetCore.Localization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace PowerDNS_Web.Pages
{
    [IgnoreAntiforgeryToken]
    public class SetCultureModel : PageModel
    {
        public IActionResult OnGet(string culture = "ru", string? returnUrl = "/")
        {
            Response.Cookies.Append(
                CookieRequestCultureProvider.DefaultCookieName,
                CookieRequestCultureProvider.MakeCookieValue(new RequestCulture(culture)),
                new CookieOptions { Expires = DateTimeOffset.UtcNow.AddYears(1) });

            if (string.IsNullOrWhiteSpace(returnUrl) || !Url.IsLocalUrl(returnUrl))
                returnUrl = "/";

            return LocalRedirect(returnUrl);
        }
    }
}
