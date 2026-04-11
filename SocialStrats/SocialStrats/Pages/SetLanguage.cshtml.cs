using Microsoft.AspNetCore.Localization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

public class SetLanguageModel : PageModel
{
    public IActionResult OnPost(string culture, string returnUrl = "/")
    {
        var supported = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "en", "sk", "de" };
        if (!supported.Contains(culture)) culture = "en";

        Response.Cookies.Append(
            CookieRequestCultureProvider.DefaultCookieName,
            CookieRequestCultureProvider.MakeCookieValue(new RequestCulture(culture)),
            new CookieOptions { Expires = DateTimeOffset.UtcNow.AddYears(1), IsEssential = true, SameSite = SameSiteMode.Lax }
        );

        if (string.IsNullOrWhiteSpace(returnUrl) || !Url.IsLocalUrl(returnUrl)) returnUrl = "/";
        return LocalRedirect(returnUrl);
    }
}

