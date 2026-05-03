# Chat History — Rystats / SocialStrats Development Session 18

**Date:** 2026-04-28  
**Topic:** Razor Pages localization with language switcher (SK / EN / DE) using culture-suffixed views (`Index.en.cshtml`, `Index.sk.cshtml`, `Index.de.cshtml`) + optional culture-suffixed layout.

\---

## Context

The user is building **Rystats / SocialStrats** (Razor Pages) and wants the app to be offered in **Slovak**, **English**, and **German**. The user specifically asked for a solution where selecting a language:

* shows a language menu with **flags** (Slovakia / English / Germany),
* updates the whole UI (including layout if desired),
* and routes Razor to **culture-suffixed views** like `Index.en.cshtml`, `Index.sk.cshtml`, `Index.de.cshtml`.

\---

## User Requests (verbatim intent)

1. “how do i do localisations so i can offer the app in slovak and german as easy as possible”
2. “is there a different way to do it”
3. “give me a language options … with attached correct flags slovak english and deutch … and transfers them to index.en.cshtml etc”
4. “give me please exact instructions where to put what”
5. “what do i do here” (user showed their `\_Layout.cshtml`)

\---

## Architecture Chosen

### Primary strategy

**View-per-language** using ASP.NET Core view localization **suffix format**:

* `Pages/Index.en.cshtml`
* `Pages/Index.sk.cshtml`
* `Pages/Index.de.cshtml`

Razor selects the correct file based on the active culture.

### Culture persistence

A **cookie-based** culture selection set by a small Razor Page endpoint (`/SetLanguage`) so the choice persists across reloads and sessions.

### Optional extension

Culture-specific layouts:

* `Pages/Shared/\_Layout.en.cshtml`
* `Pages/Shared/\_Layout.sk.cshtml`
* `Pages/Shared/\_Layout.de.cshtml`

This allows the navbar/footer text to be fully translated without `.resx` resources.

\---

## Implementation Plan (exact steps)

### 1\) Program.cs — enable culture selection + suffix views

**Add usings**

```csharp
using System.Globalization;
using Microsoft.AspNetCore.Localization;
using Microsoft.AspNetCore.Mvc.Razor;
```

**Replace**

```csharp
builder.Services.AddRazorPages();
```

**With**

```csharp
builder.Services
    .AddRazorPages()
    .AddViewLocalization(LanguageViewLocationExpanderFormat.Suffix);
```

**After `var app = builder.Build();` insert:**

```csharp
var supportedCultures = new\[] { "en", "sk", "de" }
    .Select(c => new CultureInfo(c))
    .ToList();

var localizationOptions = new RequestLocalizationOptions
{
    DefaultRequestCulture = new RequestCulture("en"),
    SupportedCultures = supportedCultures,
    SupportedUICultures = supportedCultures,
    FallBackToParentCultures = true,
    FallBackToParentUICultures = true
};

localizationOptions.RequestCultureProviders = new IRequestCultureProvider\[]
{
    new CookieRequestCultureProvider(),
    new QueryStringRequestCultureProvider(),
    new AcceptLanguageHeaderRequestCultureProvider()
};
```

**In the middleware pipeline, add (after `UseHttpsRedirection` and before `UseStaticFiles`):**

```csharp
app.UseRequestLocalization(localizationOptions);
```

\---

### 2\) Add culture switch endpoint — `Pages/SetLanguage`

Create:

**`Pages/SetLanguage.cshtml`**

```cshtml
@page
@model SetLanguageModel
```

**`Pages/SetLanguage.cshtml.cs`**

```csharp
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
            new CookieOptions
            {
                Expires = DateTimeOffset.UtcNow.AddYears(1),
                IsEssential = true,
                SameSite = SameSiteMode.Lax
            }
        );

        if (string.IsNullOrWhiteSpace(returnUrl) || !Url.IsLocalUrl(returnUrl))
            returnUrl = "/";

        return LocalRedirect(returnUrl);
    }
}
```

\---

### 3\) Split the Index page into culture-suffixed files

1. Rename `Pages/Index.cshtml` → `Pages/Index.en.cshtml`
2. Copy `Pages/Index.en.cshtml` → `Pages/Index.sk.cshtml`
3. Copy `Pages/Index.en.cshtml` → `Pages/Index.de.cshtml`
4. Translate text inside `.sk` and `.de`.

**Keep one shared code-behind:** `Pages/Index.cshtml.cs` stays unchanged.

\---

### 4\) Optional: culture-suffixed layout files

If you want navbar/footer to be fully translated without `.resx`:

1. Rename `Pages/Shared/\_Layout.cshtml` → `Pages/Shared/\_Layout.en.cshtml`
2. Copy `\_Layout.en.cshtml` → `\_Layout.sk.cshtml`
3. Copy `\_Layout.en.cshtml` → `\_Layout.de.cshtml`
4. Translate layout text in SK/DE.

In `Pages/\_ViewStart.cshtml`, keep:

```cshtml
@{
    Layout = "\_Layout";
}
```

Razor will automatically choose `\_Layout.sk.cshtml` / `\_Layout.de.cshtml` when the culture changes.

\---

## Language Picker UI (flags + dropdown)

### Placement decision

The switcher was inserted into the existing navbar markup inside `\_Layout.cshtml`, near the right side of the nav content (after the existing mobile-only links block).

### Fix: dynamic `<html lang="">`

Replace:

```html
<html lang="en">
```

With:

```cshtml
<html lang="@System.Globalization.CultureInfo.CurrentUICulture.TwoLetterISOLanguageName">
```

### Markup (form posts to `/SetLanguage`)

* Uses a **button + dropdown**.
* Stores `returnUrl` so the user stays on the same page after switching.

### Flags

Flags were implemented as inline **SVG** (no external images needed).

### Dropdown JS

A small JS handler opens/closes the dropdown and closes it on outside click + Escape.

### Important bug fix (layout JS)

The user’s layout contained an invalid pattern: a `<script>` tag nested inside another `<script>`.  
Instruction: remove nested `<script>` and keep one valid script block.

\---

## UX Notes / Gotchas

* Use a cookie provider first so user choice overrides browser language.
* Culture suffix views scale well but can duplicate markup; recommended only when the user explicitly wants `Index.en.cshtml` style separation.
* For JS formatting (dates/times), prefer:

  * `document.documentElement.lang` as the locale key, not hard-coded `"en-US"`.

\---

## Result

After implementing:

* User can switch language via a flag dropdown.
* Culture persists via cookie.
* Razor Pages automatically serve:

  * `Index.en.cshtml` / `Index.sk.cshtml` / `Index.de.cshtml`
* (Optional) Layout also switches via `\_Layout.en/.sk/.de`.

\---

## Suggested Commit Messages

* `feat: enable request localization + culture cookie`
* `feat: add SetLanguage endpoint for culture switching`
* `ui: add language flag dropdown to navbar`
* `refactor: split Index into culture-suffixed views (en/sk/de)`
* `fix: remove nested script tag in layout`

\---

## Files Touched / Created

* `Program.cs` (services + middleware)
* `Pages/SetLanguage.cshtml` (new)
* `Pages/SetLanguage.cshtml.cs` (new)
* `Pages/Index.en.cshtml` (rename)
* `Pages/Index.sk.cshtml` (new copy)
* `Pages/Index.de.cshtml` (new copy)
* `Pages/Shared/\_Layout.cshtml` (or `\_Layout.en/.sk/.de` optional)
* `Pages/\_ViewStart.cshtml` (verify layout name stays `\_Layout`)

