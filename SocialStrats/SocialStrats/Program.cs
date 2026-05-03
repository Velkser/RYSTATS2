using System.Globalization;
using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.AspNetCore.Localization;
using Microsoft.AspNetCore.Mvc.Razor;

LoadDotEnv(Path.Combine(Directory.GetCurrentDirectory(), ".env"));
var openAiApiKey = Environment.GetEnvironmentVariable("OPENAI_API_KEY")?.Trim();
var openAiModel = Environment.GetEnvironmentVariable("OPENAI_MODEL")?.Trim();
openAiModel = string.IsNullOrWhiteSpace(openAiModel) ? "gpt-4o-mini" : openAiModel;

var builder = WebApplication.CreateBuilder(args);

builder.Services
    .AddRazorPages()
    .AddViewLocalization(LanguageViewLocationExpanderFormat.Suffix);

builder.Services.AddMemoryCache();

// Optional but recommended: basic rate limiting to protect your server and reduce Ryanair blocks.
builder.Services.AddRateLimiter(options =>
{
    options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
    options.AddFixedWindowLimiter("api", limiterOptions =>
    {
        limiterOptions.PermitLimit = 60;                // 60 requests
        limiterOptions.Window = TimeSpan.FromMinutes(1);
        limiterOptions.QueueLimit = 0;
    });
});

builder.Services.AddHttpClient("ryanair", client =>
{
    client.Timeout = TimeSpan.FromSeconds(20);
    client.DefaultRequestHeaders.UserAgent.ParseAdd("Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) SocialStrats/1.0");
    client.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
    client.DefaultRequestHeaders.AcceptLanguage.ParseAdd("en,en-US;q=0.9");
});

builder.Services.AddHttpClient("openai", client =>
{
    client.Timeout = TimeSpan.FromSeconds(45);
    client.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
});

var app = builder.Build();
var supportedCultures = new[] { "en", "sk", "de" }
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

// We want: cookie (user choice) -> query string (optional) -> browser header
localizationOptions.RequestCultureProviders = new IRequestCultureProvider[]
{
    new CookieRequestCultureProvider(),
    new QueryStringRequestCultureProvider(),
    new AcceptLanguageHeaderRequestCultureProvider()
};

app.UseHttpsRedirection();
app.UseRequestLocalization(localizationOptions);
app.UseStaticFiles();
app.UseRouting();

app.UseRateLimiter();

app.MapRazorPages();

JsonSerializerOptions JsonOpts = new(JsonSerializerDefaults.Web)
{
    PropertyNameCaseInsensitive = true
};

static string PickBaseLocateDomain(string preferred)
{
    // Ryanair commonly exposes locate endpoints at www.ryanair.com/api/views/locate/...
    // Some code examples reference services-api.ryanair.com/views/locate/... (no /api prefix). :contentReference[oaicite:4]{index=4}
    // We keep primary as www.ryanair.com by default.
    return preferred?.Equals("services", StringComparison.OrdinalIgnoreCase) == true
        ? "https://services-api.ryanair.com"
        : "https://www.ryanair.com";
}

static double HaversineKm(double lat1, double lon1, double lat2, double lon2)
{
    const double R = 6371.0;
    double dLat = DegreesToRadians(lat2 - lat1);
    double dLon = DegreesToRadians(lon2 - lon1);
    double a =
        Math.Sin(dLat / 2) * Math.Sin(dLat / 2) +
        Math.Cos(DegreesToRadians(lat1)) * Math.Cos(DegreesToRadians(lat2)) *
        Math.Sin(dLon / 2) * Math.Sin(dLon / 2);
    double c = 2 * Math.Atan2(Math.Sqrt(a), Math.Sqrt(1 - a));
    return R * c;
}
static double DegreesToRadians(double deg) => deg * (Math.PI / 180.0);

static async Task<string> GetStringWithRetryAsync(HttpClient client, string url, CancellationToken ct)
{
    // Light retry on transient errors + 429.
    for (var attempt = 1; attempt <= 3; attempt++)
    {
        using var req = new HttpRequestMessage(HttpMethod.Get, url);
        using var resp = await client.SendAsync(req, HttpCompletionOption.ResponseHeadersRead, ct);

        if (resp.StatusCode == (HttpStatusCode)429)
        {
            // back off
            var delay = TimeSpan.FromMilliseconds(400 * attempt + Random.Shared.Next(0, 250));
            await Task.Delay(delay, ct);
            continue;
        }

        if ((int)resp.StatusCode >= 500 && attempt < 3)
        {
            var delay = TimeSpan.FromMilliseconds(250 * attempt + Random.Shared.Next(0, 150));
            await Task.Delay(delay, ct);
            continue;
        }

        resp.EnsureSuccessStatusCode();
        return await resp.Content.ReadAsStringAsync(ct);
    }

    throw new HttpRequestException("Upstream throttling or transient failure persisted after retries.");
}

static void LoadDotEnv(string envFilePath)
{
    if (!File.Exists(envFilePath))
        return;

    foreach (var rawLine in File.ReadAllLines(envFilePath))
    {
        var line = rawLine.Trim();
        if (line.Length == 0 || line.StartsWith("#", StringComparison.Ordinal))
            continue;

        var idx = line.IndexOf('=');
        if (idx <= 0)
            continue;

        var key = line[..idx].Trim();
        var value = line[(idx + 1)..].Trim();

        if (value.StartsWith('"') && value.EndsWith('"') && value.Length >= 2)
            value = value[1..^1];

        if (string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable(key)))
            Environment.SetEnvironmentVariable(key, value);
    }
}

static string AgentLanguage(string lang)
{
    return lang switch
    {
        "sk" => "Slovak",
        "de" => "German",
        _ => "English"
    };
}

static string BuildAgentSystemPrompt(string lang)
{
    var languageName = AgentLanguage(lang);

    return $"""
You are the SocialStrats flight-planning assistant.
Always respond in {languageName}.
Be concise and practical.

Goals:
1) If essential fields are missing, ask short follow-up questions.
2) If deals are provided, rank by absolute lowest price when the user explicitly asks for the cheapest flights.
3) Otherwise rank by fit to brief and explain tradeoffs: cheap pick vs smart value.

Important:
- Destination is optional. If user has no fixed destination, propose best-fit options from available deals.
- Ask for destination only if the user explicitly wants one specific city/region but it is ambiguous.

Output rules:
- Return valid JSON only.
- Use schema fields: assistantMessage (string), suggestedReplies (string array).
- Keep suggestedReplies to 2-4 short options.
- Do not include markdown code fences.
""";
}

static string DealTypeLabel(double pricePerKm, string lang)
{
    var smart = pricePerKm <= 0.06;
    return lang switch
    {
        "sk" => smart ? "smart value" : "cheap pick",
        "de" => smart ? "smart value" : "cheap pick",
        _ => smart ? "smart value" : "cheap pick"
    };
}

static bool DealMatchesDestination(DealResult deal, AgentTravelContext context)
{
    var raw = string.Join(" ", new[] { context.DestinationSearch, context.DestinationIdea }
        .Where(s => !string.IsNullOrWhiteSpace(s))
        .Select(s => s!.Trim()));

    if (string.IsNullOrWhiteSpace(raw))
        return true;

    var terms = raw
        .Split(new[] { ' ', ',', ';', '/', '-', '_', '.', ':' }, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
        .Where(t => t.Length >= 3)
        .Distinct(StringComparer.OrdinalIgnoreCase)
        .ToArray();

    if (terms.Length == 0)
        return true;

    var dstCode = deal.Destination?.Trim() ?? string.Empty;
    var dstName = deal.DestinationName?.Trim() ?? string.Empty;

    foreach (var term in terms)
    {
        if (term.Length == 3 && dstCode.Equals(term, StringComparison.OrdinalIgnoreCase))
            return true;

        if (dstName.Contains(term, StringComparison.OrdinalIgnoreCase))
            return true;
    }

    return false;
}

static bool WantsCheapestFirst(AgentTravelContext context, IEnumerable<AgentChatMessage>? messages)
{
    var parts = new List<string?>();
    parts.Add(context.Notes);
    parts.Add(context.DestinationIdea);
    parts.Add(context.DestinationSearch);

    if (messages is not null)
        parts.AddRange(messages.Select(m => m.Content));

    var text = string.Join(" ", parts.Where(p => !string.IsNullOrWhiteSpace(p))).ToLowerInvariant();
    if (string.IsNullOrWhiteSpace(text))
        return false;

    var markers = new[]
    {
        "najlac", "najlacne", "najlacnej", "lacne", "lacny", "lacna",
        "cheap", "cheapest", "lowest price", "low price",
        "billig", "guenstig", "günstig"
    };

    return markers.Any(marker => text.Contains(marker));
}

static string BuildDealReason(DealResult deal, AgentTravelContext context, string lang)
{
    var budgetOk = context.MaxBudget is null || deal.Price <= context.MaxBudget.Value;
    var distanceOk = context.MaxDistanceKm is null || deal.DistanceKm <= context.MaxDistanceKm.Value;
    var type = DealTypeLabel(deal.PricePerKm, lang);

    return lang switch
    {
        "sk" => $"{type}: cena {(budgetOk ? "sedí" : "je nad")}, vzdialenosť {(distanceOk ? "v limite" : "mimo limit")}.",
        "de" => $"{type}: Preis {(budgetOk ? "passt" : "liegt ueber Budget")}, Distanz {(distanceOk ? "im Rahmen" : "ueber Limit") }.",
        _ => $"{type}: price {(budgetOk ? "fits" : "is above")}, distance {(distanceOk ? "within range" : "outside range")}."
    };
}

static AgentDealCard[] BuildAgentDealShortlist(IReadOnlyCollection<DealResult> deals, AgentTravelContext context, string lang)
{
    return deals
        .Take(5)
        .Select(d => new AgentDealCard(
            Destination: d.Destination,
            DestinationName: d.DestinationName,
            Date: d.Date,
            Price: d.Price,
            Currency: d.Currency,
            DistanceKm: d.DistanceKm,
            PricePerKm: d.PricePerKm,
            Reason: BuildDealReason(d, context, lang),
            IsAlternative: false))
        .ToArray();
}

static string[] ExtractDestinationTerms(AgentTravelContext context)
{
    return string.Join(" ", new[] { context.DestinationSearch, context.DestinationIdea }
            .Where(s => !string.IsNullOrWhiteSpace(s))
            .Select(s => s!.Trim()))
        .Split(new[] { ' ', ',', ';', '/', '-', '_', '.', ':' }, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
        .Where(t => t.Length >= 3)
        .Distinct(StringComparer.OrdinalIgnoreCase)
        .ToArray();
}

static async Task<List<AirportMeta>> FetchAirportCatalogAsync(string lang, IHttpClientFactory http, CancellationToken ct)
{
    using var client = http.CreateClient("ryanair");
    var url = $"https://www.ryanair.com/api/views/locate/3/airports/{Uri.EscapeDataString(lang)}/active";
    var json = await GetStringWithRetryAsync(client, url, ct);

    using var doc = JsonDocument.Parse(json);
    var result = new List<AirportMeta>();

    foreach (var el in doc.RootElement.EnumerateArray())
    {
        var iata =
            el.TryGetProperty("iataCode", out var i1) ? i1.GetString() :
            el.TryGetProperty("code", out var i2) ? i2.GetString() :
            null;

        if (string.IsNullOrWhiteSpace(iata))
            continue;

        string name =
            el.TryGetProperty("name", out var n) ? (n.GetString() ?? iata!) :
            el.TryGetProperty("seoName", out var sn) ? (sn.GetString() ?? iata!) :
            iata!;

        string country =
            el.TryGetProperty("countryName", out var cn) ? (cn.GetString() ?? "") :
            el.TryGetProperty("country", out var c2) ? (c2.GetString() ?? "") :
            "";

        double? lat = null;
        double? lng = null;
        if (el.TryGetProperty("coordinates", out var coords) && coords.ValueKind == JsonValueKind.Object)
        {
            if (coords.TryGetProperty("latitude", out var la) && la.ValueKind == JsonValueKind.Number) lat = la.GetDouble();
            if (coords.TryGetProperty("longitude", out var lo) && lo.ValueKind == JsonValueKind.Number) lng = lo.GetDouble();
        }

        result.Add(new AirportMeta(iata!.Trim().ToUpperInvariant(), name, country, lat, lng));
    }

    return result;
}

static async Task<AirportMeta?> ResolveDestinationAnchorAsync(AgentTravelContext context, string lang, IHttpClientFactory http, CancellationToken ct)
{
    var terms = ExtractDestinationTerms(context);
    if (terms.Length == 0)
        return null;

    List<AirportMeta> airports;
    try
    {
        airports = await FetchAirportCatalogAsync(lang, http, ct);
    }
    catch
    {
        return null;
    }

    foreach (var term in terms)
    {
        if (term.Length == 3)
        {
            var byIata = airports.FirstOrDefault(a => a.Iata.Equals(term, StringComparison.OrdinalIgnoreCase));
            if (byIata is not null)
                return byIata;
        }
    }

    var byName = airports.FirstOrDefault(a =>
        terms.Any(t => a.Name.Contains(t, StringComparison.OrdinalIgnoreCase)));

    return byName;
}

static string BuildAlternativeReason(double nearKm, string lang)
{
    return lang switch
    {
        "sk" => $"Alternative: približne {nearKm:0} km od zvolenej destinácie.",
        "de" => $"Alternative: etwa {nearKm:0} km von deinem gewaehlten Ziel entfernt.",
        _ => $"Alternative: around {nearKm:0} km from your selected destination."
    };
}

static AgentDealCard[] BuildAlternativeDealShortlist(
    IReadOnlyCollection<DealResult> candidateDeals,
    AgentTravelContext context,
    string lang,
    AirportMeta? anchor)
{
    if (candidateDeals.Count == 0)
        return Array.Empty<AgentDealCard>();

    // Alternatives should exclude direct matches to the requested destination.
    var alternativePool = candidateDeals
        .Where(d => !DealMatchesDestination(d, context))
        .ToList();

    if (alternativePool.Count == 0)
        return Array.Empty<AgentDealCard>();

    var ranked = alternativePool
        .Select(d =>
        {
            double nearKm = double.MaxValue;
            if (anchor?.Lat is not null && anchor.Lng is not null)
                nearKm = HaversineKm(anchor.Lat.Value, anchor.Lng.Value, d.DestLat, d.DestLng);

            return new { Deal = d, NearKm = nearKm };
        })
        .OrderBy(x => x.NearKm)
        .ThenBy(x => x.Deal.Price)
        .ThenBy(x => x.Deal.PricePerKm)
        .Take(5)
        .ToList();

    return ranked
        .Select(x => new AgentDealCard(
            Destination: x.Deal.Destination,
            DestinationName: x.Deal.DestinationName,
            Date: x.Deal.Date,
            Price: x.Deal.Price,
            Currency: x.Deal.Currency,
            DistanceKm: x.Deal.DistanceKm,
            PricePerKm: x.Deal.PricePerKm,
            Reason: BuildAlternativeReason(x.NearKm, lang),
            IsAlternative: true))
        .ToArray();
}

static async Task<OpenAiAgentResult?> AskOpenAiAgentAsync(
    string apiKey,
    string model,
    string lang,
    AgentTravelContext context,
    string brief,
    IReadOnlyCollection<string> missingFields,
    IReadOnlyCollection<DealResult> deals,
    IReadOnlyCollection<AgentChatMessage> thread,
    IHttpClientFactory http,
    CancellationToken ct)
{
    var trimmedThread = thread
        .Where(m => !string.IsNullOrWhiteSpace(m.Content) && (m.Role == "user" || m.Role == "assistant"))
        .TakeLast(12)
        .ToArray();

    var payload = new
    {
        language = AgentLanguage(lang),
        travelBrief = brief,
        travelContext = context,
        missingFields,
        conversation = trimmedThread,
        deals = deals.Take(8).Select(d => new
        {
            d.Destination,
            d.DestinationName,
            d.Date,
            d.Price,
            d.Currency,
            d.DistanceKm,
            d.PricePerKm
        }).ToArray()
    };

    var requestBody = new
    {
        model,
        temperature = 0.35,
        response_format = new { type = "json_object" },
        messages = new object[]
        {
            new { role = "system", content = BuildAgentSystemPrompt(lang) },
            new { role = "user", content = JsonSerializer.Serialize(payload) }
        }
    };

    using var client = http.CreateClient("openai");
    using var req = new HttpRequestMessage(HttpMethod.Post, "https://api.openai.com/v1/chat/completions");
    req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", apiKey);
    req.Content = new StringContent(JsonSerializer.Serialize(requestBody), Encoding.UTF8, "application/json");

    using var resp = await client.SendAsync(req, ct);
    var raw = await resp.Content.ReadAsStringAsync(ct);
    if (!resp.IsSuccessStatusCode)
        return null;

    using var doc = JsonDocument.Parse(raw);
    var content = doc.RootElement
        .GetProperty("choices")[0]
        .GetProperty("message")
        .GetProperty("content")
        .GetString();

    if (string.IsNullOrWhiteSpace(content))
        return null;

    using var outDoc = JsonDocument.Parse(content);
    var root = outDoc.RootElement;

    var assistantMessage = root.TryGetProperty("assistantMessage", out var msgEl) && msgEl.ValueKind == JsonValueKind.String
        ? msgEl.GetString()
        : null;

    var suggestedReplies = new List<string>();
    if (root.TryGetProperty("suggestedReplies", out var repliesEl) && repliesEl.ValueKind == JsonValueKind.Array)
    {
        foreach (var item in repliesEl.EnumerateArray())
        {
            if (item.ValueKind == JsonValueKind.String && !string.IsNullOrWhiteSpace(item.GetString()))
                suggestedReplies.Add(item.GetString()!);
        }
    }

    if (string.IsNullOrWhiteSpace(assistantMessage) && suggestedReplies.Count == 0)
        return null;

    return new OpenAiAgentResult(
        AssistantMessage: assistantMessage,
        SuggestedReplies: suggestedReplies
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Take(4)
            .ToArray());
}

static async Task<AgentTravelContext?> ExtractTravelContextFromConversationAsync(
    string apiKey,
    string model,
    string lang,
    AgentTravelContext currentContext,
    IReadOnlyCollection<AgentChatMessage> thread,
    IHttpClientFactory http,
    CancellationToken ct)
{
    var latestUserMessage = thread
        .LastOrDefault(m => m.Role == "user" && !string.IsNullOrWhiteSpace(m.Content))
        ?.Content;

    if (string.IsNullOrWhiteSpace(latestUserMessage))
        return null;

    var parserPayload = new
    {
        language = AgentLanguage(lang),
        currentContext,
        latestUserMessage,
        instructions = "Return only JSON. Extract only values explicitly stated by the user in latestUserMessage. Use null for unknown fields. Month must be yyyy-MM. Origin must be 3-letter IATA code if explicit."
    };

    var requestBody = new
    {
        model,
        temperature = 0,
        response_format = new { type = "json_object" },
        messages = new object[]
        {
            new
            {
                role = "system",
                content = "You extract travel search constraints from user text. Return JSON object with keys: origin, month, currency, maxBudget, maxDeals, destinationIdea, destinationSearch, maxDistanceKm, minDistanceKm, preferWeekend, onlyHotDeals, notes. Use null when not explicitly provided. Do not invent values."
            },
            new { role = "user", content = JsonSerializer.Serialize(parserPayload) }
        }
    };

    using var client = http.CreateClient("openai");
    using var req = new HttpRequestMessage(HttpMethod.Post, "https://api.openai.com/v1/chat/completions");
    req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", apiKey);
    req.Content = new StringContent(JsonSerializer.Serialize(requestBody), Encoding.UTF8, "application/json");

    using var resp = await client.SendAsync(req, ct);
    if (!resp.IsSuccessStatusCode)
        return null;

    var raw = await resp.Content.ReadAsStringAsync(ct);
    using var doc = JsonDocument.Parse(raw);
    var content = doc.RootElement
        .GetProperty("choices")[0]
        .GetProperty("message")
        .GetProperty("content")
        .GetString();

    if (string.IsNullOrWhiteSpace(content))
        return null;

    AgentTravelContextPatch? patch;
    try
    {
        patch = JsonSerializer.Deserialize<AgentTravelContextPatch>(content, new JsonSerializerOptions(JsonSerializerDefaults.Web)
        {
            PropertyNameCaseInsensitive = true
        });
    }
    catch
    {
        return null;
    }

    if (patch is null)
        return null;

    var origin = currentContext.Origin;
    if (!string.IsNullOrWhiteSpace(patch.Origin))
    {
        var normalizedOrigin = patch.Origin.Trim().ToUpperInvariant();
        if (normalizedOrigin.Length == 3 && normalizedOrigin.All(char.IsLetter))
            origin = normalizedOrigin;
    }

    var month = currentContext.Month;
    if (!string.IsNullOrWhiteSpace(patch.Month))
    {
        var rawMonth = patch.Month.Trim();
        if (DateOnly.TryParseExact(rawMonth + "-01", "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out _))
            month = rawMonth;
    }

    var currency = currentContext.Currency;
    if (!string.IsNullOrWhiteSpace(patch.Currency))
    {
        var normalizedCurrency = patch.Currency.Trim().ToUpperInvariant();
        if (normalizedCurrency.Length == 3 && normalizedCurrency.All(char.IsLetter))
            currency = normalizedCurrency;
    }

    var maxBudget = patch.MaxBudget ?? currentContext.MaxBudget;
    var maxDeals = patch.MaxDeals ?? currentContext.MaxDeals;

    var clearsDestinationPreference = ContainsNoDestinationPreference(latestUserMessage);
    var destinationIdea = clearsDestinationPreference
        ? null
        : (string.IsNullOrWhiteSpace(patch.DestinationIdea) ? currentContext.DestinationIdea : patch.DestinationIdea.Trim());
    var destinationSearch = clearsDestinationPreference
        ? null
        : (string.IsNullOrWhiteSpace(patch.DestinationSearch) ? currentContext.DestinationSearch : patch.DestinationSearch.Trim());
    var maxDistanceKm = patch.MaxDistanceKm ?? currentContext.MaxDistanceKm;
    var minDistanceKm = patch.MinDistanceKm ?? currentContext.MinDistanceKm;
    var preferWeekend = patch.PreferWeekend ?? currentContext.PreferWeekend;
    var onlyHotDeals = patch.OnlyHotDeals ?? currentContext.OnlyHotDeals;
    var notes = string.IsNullOrWhiteSpace(patch.Notes) ? currentContext.Notes : patch.Notes.Trim();

    return new AgentTravelContext(
        Origin: origin,
        Month: month,
        Currency: currency,
        MaxBudget: maxBudget,
        MaxDeals: maxDeals,
        DestinationIdea: destinationIdea,
        DestinationSearch: destinationSearch,
        MaxDistanceKm: maxDistanceKm,
        MinDistanceKm: minDistanceKm,
        PreferWeekend: preferWeekend,
        OnlyHotDeals: onlyHotDeals,
        Notes: notes);
}

static bool ContainsNoDestinationPreference(string text)
{
    if (string.IsNullOrWhiteSpace(text))
        return false;

    var v = text.Trim().ToLowerInvariant();

    return v.Contains("no preference") ||
           v.Contains("no city preference") ||
           v.Contains("any city") ||
           v.Contains("anywhere") ||
           v.Contains("wherever") ||
           v.Contains("bez preferencie") ||
           v.Contains("bez preferencie mesta") ||
           v.Contains("lubovolne mesto") ||
           v.Contains("egal welche stadt") ||
           v.Contains("keine praeferenz") ||
           v.Contains("kein bevorzugtes ziel");
}

static async Task<(string? depIso, string? arrIso)> TryGetFlightTimesAsync(
    IMemoryCache cache,
    HttpClient client,
    string origin,
    string destination,
    string dateOutYmd,     // yyyy-MM-dd
    string market,         // e.g. "en-gb"
    CancellationToken ct)
{
    // Ryanair booking availability endpoint returns per-flight segment times in `segments[].time[]`
    // Example/shape documented in open-source scrapers. :contentReference[oaicite:3]{index=3}
    var url =
        $"https://www.ryanair.com/api/booking/v4/{Uri.EscapeDataString(market)}/availability" +
        $"?ADT=1&CHD=0&DateIn=&DateOut={Uri.EscapeDataString(dateOutYmd)}" +
        $"&Destination={Uri.EscapeDataString(destination)}&Disc=0&INF=0&Origin={Uri.EscapeDataString(origin)}&TEEN=0" +
        $"&ToUs=AGREED&IncludeConnectingFlights=false&RoundTrip=false";

    // Cache per corridor + date (times don’t need sub-minute freshness)
    var cacheKey = $"avail:{market}:{origin}:{destination}:{dateOutYmd}";
    string json;
    try
    {
        json = await GetCachedStringAsync(cache, client, cacheKey, url, TimeSpan.FromMinutes(30), ct);
    }
    catch
    {
        return (null, null);
    }

    try
    {
        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;

        if (!root.TryGetProperty("trips", out var trips) || trips.ValueKind != JsonValueKind.Array) return (null, null);

        // Find flights for our date
        foreach (var trip in trips.EnumerateArray())
        {
            if (!trip.TryGetProperty("dates", out var dates) || dates.ValueKind != JsonValueKind.Array) continue;

            foreach (var d in dates.EnumerateArray())
            {
                var dateOut =
                    d.TryGetProperty("dateOut", out var dout) && dout.ValueKind == JsonValueKind.String ? dout.GetString() : null;

                // dateOut is usually ISO like "2020-07-01T00:00:00.000"
                if (string.IsNullOrWhiteSpace(dateOut) || dateOut!.Length < 10) continue;
                if (!dateOut.AsSpan(0, 10).SequenceEqual(dateOutYmd.AsSpan())) continue;

                if (!d.TryGetProperty("flights", out var flights) || flights.ValueKind != JsonValueKind.Array) return (null, null);

                // Pick the cheapest flight on that day (fallback to first)
                decimal best = decimal.MaxValue;
                string? bestDep = null;
                string? bestArr = null;

                foreach (var f in flights.EnumerateArray())
                {
                    decimal? price = null;

                    // common: regularFare.fares[0].amount
                    if (f.TryGetProperty("regularFare", out var rf) && rf.ValueKind == JsonValueKind.Object &&
                        rf.TryGetProperty("fares", out var fares) && fares.ValueKind == JsonValueKind.Array)
                    {
                        foreach (var fare in fares.EnumerateArray())
                        {
                            if (fare.TryGetProperty("amount", out var amt) && amt.ValueKind == JsonValueKind.Number)
                            {
                                price = amt.GetDecimal();
                                break;
                            }
                        }
                    }

                    // extract segment times
                    string? dep = null, arr = null;
                    if (f.TryGetProperty("segments", out var segs) && segs.ValueKind == JsonValueKind.Array)
                    {
                        foreach (var s in segs.EnumerateArray())
                        {
                            // direct flights: first segment is enough
                            if (s.TryGetProperty("time", out var time) && time.ValueKind == JsonValueKind.Array)
                            {
                                var it = time.EnumerateArray();
                                if (it.MoveNext() && it.Current.ValueKind == JsonValueKind.String) dep = it.Current.GetString();
                                if (it.MoveNext() && it.Current.ValueKind == JsonValueKind.String) arr = it.Current.GetString();
                                break;
                            }
                        }
                    }

                    // choose cheapest (or first with valid times)
                    if (dep != null && arr != null)
                    {
                        var p = price ?? best; // if no price, treat as not better than best
                        if (p < best)
                        {
                            best = p;
                            bestDep = dep;
                            bestArr = arr;
                        }
                        else if (bestDep == null) // fallback if we never set best
                        {
                            bestDep = dep;
                            bestArr = arr;
                        }
                    }
                }

                return (bestDep, bestArr);
            }
        }
    }
    catch
    {
        // ignore parse errors safely
    }

    return (null, null);
}

static FareSummary? TryFindBestFare(JsonElement root)
{
    decimal? bestPrice = null;
    string? bestDate = null;
    string? bestDeparture = null;
    string? bestArrival = null;

    static string? ReadString(JsonElement element, params string[] names)
    {
        foreach (var name in names)
        {
            if (element.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String)
                return value.GetString();
        }

        return null;
    }

    static decimal? ReadPrice(JsonElement element)
    {
        return
            element.TryGetProperty("price", out var p) && p.ValueKind == JsonValueKind.Number ? p.GetDecimal() :
            (element.TryGetProperty("price", out var p2) && p2.ValueKind == JsonValueKind.Object &&
             p2.TryGetProperty("value", out var pv) && pv.ValueKind == JsonValueKind.Number) ? pv.GetDecimal() :
            (element.TryGetProperty("fare", out var f) && f.ValueKind == JsonValueKind.Object &&
             f.TryGetProperty("amount", out var fa) && fa.ValueKind == JsonValueKind.Number) ? fa.GetDecimal() :
            null;
    }

    static void Consider(
        JsonElement element,
        ref decimal? bestPrice,
        ref string? bestDate,
        ref string? bestDeparture,
        ref string? bestArrival)
    {
        var date = ReadString(element, "dateOut", "day");
        var price = ReadPrice(element);

        if (date == null || price == null)
            return;

        if (bestPrice == null || price.Value < bestPrice.Value)
        {
            bestPrice = price.Value;
            bestDate = date;
            bestDeparture = ReadString(element, "departureDate");
            bestArrival = ReadString(element, "arrivalDate");
        }
    }

    static void Walk(
        JsonElement element,
        ref decimal? bestPrice,
        ref string? bestDate,
        ref string? bestDeparture,
        ref string? bestArrival)
    {
        switch (element.ValueKind)
        {
            case JsonValueKind.Array:
                foreach (var item in element.EnumerateArray())
                    Walk(item, ref bestPrice, ref bestDate, ref bestDeparture, ref bestArrival);
                break;

            case JsonValueKind.Object:
                Consider(element, ref bestPrice, ref bestDate, ref bestDeparture, ref bestArrival);
                foreach (var prop in element.EnumerateObject())
                    Walk(prop.Value, ref bestPrice, ref bestDate, ref bestDeparture, ref bestArrival);
                break;
        }
    }

    Walk(root, ref bestPrice, ref bestDate, ref bestDeparture, ref bestArrival);

    return bestPrice != null && bestDate != null
        ? new FareSummary(bestDate, bestPrice.Value, bestDeparture, bestArrival)
        : null;
}

static async Task<string> GetCachedStringAsync(
    IMemoryCache cache,
    HttpClient client,
    string cacheKey,
    string url,
    TimeSpan ttl,
    CancellationToken ct)
{
    if (cache.TryGetValue(cacheKey, out string? cached) && !string.IsNullOrWhiteSpace(cached))
        return cached!;

    var json = await GetStringWithRetryAsync(client, url, ct);
    cache.Set(cacheKey, json, ttl);
    return json;
}

static List<DailyFare> ParseAllDailyFares(string json)
{
    var acc = new Dictionary<string, decimal>(StringComparer.OrdinalIgnoreCase);
    try
    {
        using var doc = JsonDocument.Parse(json);
        WalkFares(doc.RootElement, acc);
    }
    catch { }

    return acc
        .Select(kv => new DailyFare(kv.Key, kv.Value))
        .OrderBy(f => f.Date)
        .ToList();

    static void WalkFares(JsonElement el, Dictionary<string, decimal> acc)
    {
        switch (el.ValueKind)
        {
            case JsonValueKind.Array:
                foreach (var item in el.EnumerateArray())
                    WalkFares(item, acc);
                break;
            case JsonValueKind.Object:
                string? date = null;
                decimal? price = null;

                if (el.TryGetProperty("dateOut", out var dout) && dout.ValueKind == JsonValueKind.String)
                    date = dout.GetString();
                else if (el.TryGetProperty("day", out var day) && day.ValueKind == JsonValueKind.String)
                    date = day.GetString();

                if (el.TryGetProperty("price", out var p))
                {
                    if (p.ValueKind == JsonValueKind.Number) price = p.GetDecimal();
                    else if (p.ValueKind == JsonValueKind.Object && p.TryGetProperty("value", out var pv) && pv.ValueKind == JsonValueKind.Number)
                        price = pv.GetDecimal();
                }
                else if (el.TryGetProperty("fare", out var f) && f.ValueKind == JsonValueKind.Object &&
                         f.TryGetProperty("amount", out var fa) && fa.ValueKind == JsonValueKind.Number)
                {
                    price = fa.GetDecimal();
                }

                if (date != null && price != null)
                {
                    var key = date.Length >= 10 ? date[..10] : date;
                    if (!acc.TryGetValue(key, out var existing) || price.Value < existing)
                        acc[key] = price.Value;
                }

                foreach (var prop in el.EnumerateObject())
                    WalkFares(prop.Value, acc);
                break;
        }
    }
}

static string BuildAgentTravelBrief(AgentTravelContext context, bool isSk)
{
    var parts = new List<string>();

    if (!string.IsNullOrWhiteSpace(context.Origin))
        parts.Add(isSk ? $"odlet {context.Origin}" : $"origin {context.Origin}");

    if (!string.IsNullOrWhiteSpace(context.Month))
        parts.Add(isSk ? $"mesiac {context.Month}" : $"month {context.Month}");

    if (context.MaxBudget is not null && !string.IsNullOrWhiteSpace(context.Currency))
        parts.Add(isSk ? $"rozpočet do {context.MaxBudget:0} {context.Currency}" : $"budget up to {context.MaxBudget:0} {context.Currency}");

    var destinationIdea = !string.IsNullOrWhiteSpace(context.DestinationIdea)
        ? context.DestinationIdea
        : context.DestinationSearch;

    if (!string.IsNullOrWhiteSpace(destinationIdea))
        parts.Add(isSk ? $"cieľ alebo štýl: {destinationIdea}" : $"destination or vibe: {destinationIdea}");

    if (context.MaxDistanceKm is not null)
        parts.Add(isSk ? $"max. vzdialenosť {context.MaxDistanceKm} km" : $"max distance {context.MaxDistanceKm} km");

    if (context.PreferWeekend)
        parts.Add(isSk ? "preferuje víkend" : "prefers weekend trips");

    if (context.OnlyHotDeals)
        parts.Add(isSk ? "len horúce deals" : "hot deals only");

    if (!string.IsNullOrWhiteSpace(context.Notes))
        parts.Add(isSk ? $"poznámky: {context.Notes}" : $"notes: {context.Notes}");

    if (parts.Count == 0)
        return isSk ? "zatiaľ nemám žiadne cestovné preferencie" : "no travel preferences captured yet";

    return string.Join(", ", parts);
}

static string[] BuildAgentSuggestedReplies(AgentTravelContext context, IReadOnlyCollection<string> missingFields, bool isSk)
{
    var suggestions = new List<string>();
    var suggestedBudget = context.MaxBudget is not null
        ? Math.Max(80, (int)Math.Round(context.MaxBudget.Value))
        : 120;

    if (missingFields.Contains("origin"))
        suggestions.Add(isSk ? "Letím z BTS." : "I fly from BTS.");

    if (missingFields.Contains("destination"))
        suggestions.Add(isSk ? "Chcem teplo pri mori v máji." : "I want a warm beach trip in May.");

    if (missingFields.Contains("budget"))
        suggestions.Add(isSk
            ? $"Rozpočet mám {suggestedBudget} {(context.Currency ?? "EUR")}."
            : $"My budget is {suggestedBudget} {(context.Currency ?? "EUR")}.");

    if (missingFields.Contains("distance"))
        suggestions.Add(isSk ? "Nech je to do 1500 km." : "Keep it within 1500 km.");

    if (missingFields.Contains("month"))
        suggestions.Add(isSk ? "Môžem cestovať budúci mesiac." : "I can travel next month.");

    if (suggestions.Count == 0)
    {
        suggestions.Add(isSk ? "Chcem víkendový city break." : "I want a weekend city break.");
        suggestions.Add(isSk ? "Uprednostni priame lety." : "Prioritize direct flights.");
        suggestions.Add(isSk ? "Nájdi najlepší pomer cena / vzdialenosť." : "Find the best value by price per kilometer.");
    }

    return suggestions
        .Where(s => !string.IsNullOrWhiteSpace(s))
        .Distinct(StringComparer.OrdinalIgnoreCase)
        .Take(4)
        .ToArray();
}

static bool IsWeekendDate(string? date)
{
    if (string.IsNullOrWhiteSpace(date))
        return false;

    if (!DateOnly.TryParse(date, CultureInfo.InvariantCulture, DateTimeStyles.None, out var parsed))
        return false;

    return parsed.DayOfWeek is DayOfWeek.Friday or DayOfWeek.Saturday or DayOfWeek.Sunday;
}

static string[] BuildAgentDealSuggestions(bool isSk)
{
    return isSk
        ?
        [
            "Ukáž len víkendové možnosti.",
            "Uprednostni najlacnejšie lety.",
            "Nájdi smart value do 1200 km.",
            "Pridaj alternatívu s teplejším počasím."
        ]
        :
        [
            "Show only weekend-friendly options.",
            "Prioritize the absolute cheapest flights.",
            "Find smart value picks within 1200 km.",
            "Add a warmer-weather alternative."
        ];
}

static string BuildAgentDealsMessage(
    AgentTravelContext context,
    string brief,
    List<DealResult> deals,
    string lang)
{
    var isSk = lang == "sk";
    var isDe = lang == "de";

    if (deals.Count == 0)
    {
        if (isSk)
            return $"Brief je pripravený ({brief}), ale pre tento mesiac nevidím vhodné lety v danom rozpočte alebo vzdialenosti. Skús zvýšiť budget, vzdialenosť alebo zmeniť mesiac.";
        if (isDe)
            return $"Dein Brief ist bereit ({brief}), aber ich finde in diesem Monat keine passenden Fluege fuer Budget oder Distanz. Erhoehe Budget/Distanz oder aendere den Monat.";

        return $"Your brief is ready ({brief}), but I could not find matching flights this month within your budget or distance. Try increasing budget/distance or changing month.";
    }

    var lines = new List<string>
    {
        isSk
            ? $"Našiel som {deals.Count} najvhodnejších možností podľa briefu: {brief}."
            : isDe
                ? $"Ich habe {deals.Count} Optionen gefunden, die am besten zu deinem Brief passen: {brief}."
                : $"I found {deals.Count} best-fit options for your brief: {brief}."
    };

    for (var i = 0; i < deals.Count; i++)
    {
        var d = deals[i];
        var pickType = DealTypeLabel(d.PricePerKm, lang);

        lines.Add($"{i + 1}. {d.DestinationName} ({d.Destination}) - {d.Price:0} {d.Currency}, {d.DistanceKm:0} km, {d.Date} ({pickType}).");
    }

    lines.Add(isSk
        ? "Ak chceš, upravím shortlist na najlacnejšie, najbližšie alebo čisto víkendové lety."
        : isDe
            ? "Wenn du willst, passe ich die Shortlist auf guenstigste, naechste oder reine Wochenendfluege an."
            : "If you want, I can refine this shortlist for cheapest, closest, or weekend-only flights.");

    return string.Join("\n", lines);
}

static async Task<List<DealResult>> FetchAgentDealsAsync(
    AgentTravelContext context,
    string lang,
    IHttpClientFactory http,
    HttpContext httpContext,
    CancellationToken ct)
{
    var origin = (context.Origin ?? string.Empty).Trim().ToUpperInvariant();
    var month = (context.Month ?? string.Empty).Trim();
    var currency = string.IsNullOrWhiteSpace(context.Currency) ? "EUR" : context.Currency!.Trim().ToUpperInvariant();
    var requestedBudget = context.MaxBudget is null ? 150 : Math.Max(20, (int)Math.Round(context.MaxBudget.Value));
    // Fetch a wider envelope so the agent can still propose alternatives when strict budget has no hits.
    var budget = Math.Clamp(Math.Max(requestedBudget + 120, requestedBudget * 2), 120, 700);
    var maxDeals = context.MaxDeals is null ? 50 : Math.Clamp(context.MaxDeals.Value * 2, 10, 120);
    var dataLang = "en"; // Ryanair locate data is most reliable in English; UI language stays separate.

    var query = $"origin={Uri.EscapeDataString(origin)}" +
                $"&month={Uri.EscapeDataString(month)}" +
                $"&currency={Uri.EscapeDataString(currency)}" +
                $"&maxBudget={budget}" +
                $"&maxDeals={maxDeals}" +
                $"&lang={Uri.EscapeDataString(dataLang)}";

    var host = httpContext.Request.Host.Host;
    var isLocalHost = host.Equals("localhost", StringComparison.OrdinalIgnoreCase) || host.Equals("127.0.0.1");
    var requestScheme = isLocalHost ? httpContext.Request.Scheme : "https";
    var primaryBaseUrl = $"{requestScheme}://{httpContext.Request.Host}";
    var candidateBaseUrls = new List<string> { primaryBaseUrl };
    if (isLocalHost)
        candidateBaseUrls.Add($"https://{host}:7043");

    using var localHandler = isLocalHost
        ? new HttpClientHandler
        {
            ServerCertificateCustomValidationCallback = static (request, _, _, errors) =>
                (request?.RequestUri?.Host?.Equals("localhost", StringComparison.OrdinalIgnoreCase) == true ||
                 request?.RequestUri?.Host == "127.0.0.1")
                ? true
                : errors == System.Net.Security.SslPolicyErrors.None
        }
        : null;

    using var localClient = isLocalHost
        ? new HttpClient(localHandler!) { Timeout = TimeSpan.FromSeconds(20) }
        : null;

    using var factoryClient = !isLocalHost ? http.CreateClient("ryanair") : null;

    string? json = null;
    Exception? lastError = null;

    foreach (var baseUrl in candidateBaseUrls.Distinct(StringComparer.OrdinalIgnoreCase))
    {
        var url = $"{baseUrl}/api/ryanair/search?{query}";
        try
        {
            var client = isLocalHost ? localClient! : factoryClient!;
            json = await GetStringWithRetryAsync(client, url, ct);
            break;
        }
        catch (Exception ex)
        {
            lastError = ex;
        }
    }

    if (json is null)
        throw lastError ?? new InvalidOperationException("Unable to load agent deals from local search endpoint.");

    var response = JsonSerializer.Deserialize<RyanairSearchResponse>(json, new JsonSerializerOptions(JsonSerializerDefaults.Web)
    {
        PropertyNameCaseInsensitive = true
    });

    return response?.Deals ?? new List<DealResult>();
}

app.MapPost("/api/agent/chat", async (
    AgentChatRequest request,
    IHttpClientFactory http,
    HttpContext httpContext,
    CancellationToken ct) =>
{
    var lang = CultureInfo.CurrentUICulture.TwoLetterISOLanguageName.ToLowerInvariant();
    var isSk = lang == "sk";
    var isDe = lang == "de";

    var context = request.TravelContext ?? new AgentTravelContext(
        Origin: null,
        Month: null,
        Currency: "EUR",
        MaxBudget: null,
        MaxDeals: null,
        DestinationIdea: null,
        DestinationSearch: null,
        MaxDistanceKm: null,
        MinDistanceKm: null,
        PreferWeekend: false,
        OnlyHotDeals: false,
        Notes: null);

    if (!string.IsNullOrWhiteSpace(openAiApiKey))
    {
        var extractedContext = await ExtractTravelContextFromConversationAsync(
            openAiApiKey!,
            openAiModel!,
            lang,
            context,
            request.Messages ?? new List<AgentChatMessage>(),
            http,
            ct);

        if (extractedContext is not null)
            context = extractedContext;
    }

    var missingFields = new List<string>();

    if (string.IsNullOrWhiteSpace(context.Origin))
        missingFields.Add("origin");
    if (context.MaxBudget is null)
        missingFields.Add("budget");
    if (string.IsNullOrWhiteSpace(context.Month))
        missingFields.Add("month");

    var brief = BuildAgentTravelBrief(context, isSk);
    var missingLabels = missingFields.Select(field => field switch
    {
        "origin" => isSk ? "letisko odletu" : "origin airport",
        "destination" => isSk ? "kam alebo aký vibe tripu" : "where to go or the trip vibe",
        "budget" => isSk ? "rozpočet" : "budget",
        "distance" => isSk ? "maximálnu vzdialenosť" : "maximum distance",
        "month" => isSk ? "mesiac cesty" : "travel month",
        _ => field
    }).ToArray();
    var assistantText = missingFields.Count > 0
        ? (isSk
            ? $"Mám zatiaľ uložené: {brief}. Aby vedel reálny AI agent nájsť najlepšie trasy, ešte potrebujem doplniť: {string.Join(", ", missingLabels)}. Napíš mi kam chceš ísť alebo aký typ tripu hľadáš, aký máš rozpočet, ako ďaleko chceš letieť a kedy chceš cestovať."
            : isDe
                ? $"Ich habe bisher: {brief}. Damit der AI-Agent die besten Routen ranken kann, brauche ich noch: {string.Join(", ", missingLabels)}. Sag mir Ziel/Trip-Vibe, Budget, Distanzlimit und Reisezeitraum."
                : $"I've captured: {brief}. Before the real AI agent starts ranking routes, I still need: {string.Join(", ", missingLabels)}. Tell me where you want to go or the trip vibe, your budget, how far you're willing to fly, and when you want to travel.")
        : string.Empty;

    if (missingFields.Count > 0)
    {
        var suggestedReplies = BuildAgentSuggestedReplies(context, missingFields, isSk);
        var llmMissing = !string.IsNullOrWhiteSpace(openAiApiKey)
            ? await AskOpenAiAgentAsync(
                openAiApiKey!,
                openAiModel!,
                lang,
                context,
                brief,
                missingFields,
                Array.Empty<DealResult>(),
                request.Messages ?? new List<AgentChatMessage>(),
                http,
                ct)
            : null;

        var finalText = llmMissing?.AssistantMessage ?? assistantText;
        var missingReplies = llmMissing?.SuggestedReplies?.Length > 0
            ? llmMissing.SuggestedReplies
            : suggestedReplies;

        return Results.Ok(new AgentChatResponse(
            Status: "needs-more-info",
            AssistantMessage: new AgentChatMessage("assistant", finalText),
            TravelContext: context,
            TravelBrief: brief,
            MissingFields: missingFields.ToArray(),
            SuggestedReplies: missingReplies,
            DealShortlist: Array.Empty<AgentDealCard>()));
    }

    List<DealResult> deals;
    try
    {
        deals = await FetchAgentDealsAsync(context, lang, http, httpContext, ct);
    }
    catch
    {
        var fallbackMessage = isSk
            ? $"Brief je pripravený: {brief}. Momentálne sa mi nepodarilo načítať flight deals, skús to prosím znova o chvíľu."
            : isDe
                ? $"Dein Brief ist bereit: {brief}. Ich konnte Deals gerade nicht laden, bitte versuche es gleich noch einmal."
                : $"Your brief is ready: {brief}. I could not load flight deals right now, please try again in a moment.";

        return Results.Ok(new AgentChatResponse(
            Status: "ready-for-agent",
            AssistantMessage: new AgentChatMessage("assistant", fallbackMessage),
            TravelContext: context,
            TravelBrief: brief,
            MissingFields: Array.Empty<string>(),
            SuggestedReplies: BuildAgentDealSuggestions(isSk),
            DealShortlist: Array.Empty<AgentDealCard>()));
    }

    var cheapestFirst = WantsCheapestFirst(context, request.Messages);
    var filteredDeals = deals
        .Where(d => context.MaxDistanceKm is null || d.DistanceKm <= context.MaxDistanceKm.Value)
        .Where(d => context.MinDistanceKm is null || d.DistanceKm >= context.MinDistanceKm.Value);

    var candidateDeals = cheapestFirst
        ? filteredDeals.OrderBy(d => d.Price).ThenBy(d => d.PricePerKm).ToList()
        : filteredDeals.OrderBy(d => d.PricePerKm).ThenBy(d => d.Price).ToList();

    var budgetFilteredDeals = candidateDeals
        .Where(d => context.MaxBudget is null || d.Price <= context.MaxBudget.Value)
        .ToList();

    if (context.PreferWeekend)
    {
        var weekendDeals = budgetFilteredDeals.Where(d => IsWeekendDate(d.Date)).ToList();
        if (weekendDeals.Count > 0)
            budgetFilteredDeals = weekendDeals;
    }

    var hasDestinationConstraint =
        !string.IsNullOrWhiteSpace(context.DestinationSearch) ||
        !string.IsNullOrWhiteSpace(context.DestinationIdea);

    var constrainedDeals = hasDestinationConstraint
        ? budgetFilteredDeals.Where(d => DealMatchesDestination(d, context)).ToList()
        : budgetFilteredDeals;

    if (hasDestinationConstraint && constrainedDeals.Count == 0)
    {
        var relaxedDirectDeals = deals
            .Where(d => DealMatchesDestination(d, context))
            .OrderBy(d => d.PricePerKm)
            .ThenBy(d => d.Price)
            .Take(5)
            .ToList();

        if (relaxedDirectDeals.Count > 0)
        {
            var relaxedMessage = isSk
                ? $"Našiel som priame výsledky pre cieľ ({context.DestinationIdea ?? context.DestinationSearch}), ale nespĺňajú všetky aktuálne filtre (budget/vzdialenosť/víkend). Tu sú najlepšie priame možnosti mimo prísnych filtrov."
                : isDe
                    ? $"Ich habe direkte Treffer fuer ({context.DestinationIdea ?? context.DestinationSearch}) gefunden, aber sie erfuellen nicht alle aktuellen Filter (Budget/Distanz/Wochenende). Hier sind die besten direkten Optionen ausserhalb der strengen Filter."
                    : $"I found direct matches for ({context.DestinationIdea ?? context.DestinationSearch}), but they do not meet all current filters (budget/distance/weekend). Here are the best direct options outside strict filters.";

            return Results.Ok(new AgentChatResponse(
                Status: "direct-relaxed",
                AssistantMessage: new AgentChatMessage("assistant", relaxedMessage),
                TravelContext: context,
                TravelBrief: brief,
                MissingFields: Array.Empty<string>(),
                SuggestedReplies: BuildAgentDealSuggestions(isSk),
                DealShortlist: BuildAgentDealShortlist(relaxedDirectDeals, context, lang)));
        }
    }

    if (hasDestinationConstraint && constrainedDeals.Count == 0)
    {
        var anchor = await ResolveDestinationAnchorAsync(context, lang, http, ct);
        var alternatives = BuildAlternativeDealShortlist(candidateDeals, context, lang, anchor);

        if (alternatives.Length > 0)
        {
            var targetLabel = context.DestinationIdea ?? context.DestinationSearch ?? "destination";
            var altMessage = isSk
                ? $"Pre cieľ {targetLabel} som nenašiel priame deals. Ponúkam ALTERNATÍVY (blízke huby), ktoré sa najviac hodia k tvojmu briefu."
                : isDe
                    ? $"Fuer {targetLabel} habe ich keine direkten Deals gefunden. Hier sind ALTERNATIVEN (nahe Hubs), die am besten zu deinem Brief passen."
                    : $"I couldn't find direct deals for {targetLabel}. Here are ALTERNATIVES (nearby hubs) that best fit your brief.";

            var altReplies = isSk
                ? new[] { "Ukáž najbližšie alternatívy.", "Skús vyšší rozpočet pre pôvodný cieľ.", "Skús iný víkend.", "Hľadaj iba priame lety." }
                : isDe
                    ? new[] { "Zeig die naechsten Alternativen.", "Erhoehe Budget fuer das Originalziel.", "Anderes Wochenenddatum pruefen.", "Nur Direktfluege suchen." }
                    : new[] { "Show nearest alternatives", "Raise budget for original destination", "Try another weekend", "Search direct flights only" };

            return Results.Ok(new AgentChatResponse(
                Status: "alternatives",
                AssistantMessage: new AgentChatMessage("assistant", altMessage),
                TravelContext: context,
                TravelBrief: brief,
                MissingFields: Array.Empty<string>(),
                SuggestedReplies: altReplies,
                DealShortlist: alternatives));
        }
    }

    var topDeals = constrainedDeals.Take(5).ToList();
    var finalMessage = BuildAgentDealsMessage(context, brief, topDeals, lang);
    var finalStatus = topDeals.Count > 0 ? "ready-with-deals" : "no-deals";
    var shortlist = BuildAgentDealShortlist(topDeals, context, lang);
    var llmWithDeals = !string.IsNullOrWhiteSpace(openAiApiKey)
        ? await AskOpenAiAgentAsync(
            openAiApiKey!,
            openAiModel!,
            lang,
            context,
            brief,
            Array.Empty<string>(),
            topDeals,
            request.Messages ?? new List<AgentChatMessage>(),
            http,
            ct)
        : null;
    var finalReplies = llmWithDeals?.SuggestedReplies?.Length > 0
        ? llmWithDeals.SuggestedReplies
        : BuildAgentDealSuggestions(isSk);
    var finalAssistantMessage = llmWithDeals?.AssistantMessage ?? finalMessage;

    return Results.Ok(new AgentChatResponse(
        Status: finalStatus,
        AssistantMessage: new AgentChatMessage("assistant", finalAssistantMessage),
        TravelContext: context,
        TravelBrief: brief,
        MissingFields: Array.Empty<string>(),
        SuggestedReplies: finalReplies,
        DealShortlist: shortlist));
}).RequireRateLimiting("api");

// -------------------- Raw proxy endpoints (optional, useful for debugging) --------------------
app.MapGet("/api/ryanair/airports", async (
    string? lang,
    string? locateDomain,
    IHttpClientFactory http,
    IMemoryCache cache,
    CancellationToken ct) =>
{
    lang ??= "en";
    var baseDomain = PickBaseLocateDomain(locateDomain ?? "www");

    // Primary seen in site traffic: https://www.ryanair.com/api/views/locate/3/airports/{lang}/active :contentReference[oaicite:5]{index=5}
    var url = $"{baseDomain}/api/views/locate/3/airports/{Uri.EscapeDataString(lang)}/active";
    using var client = http.CreateClient("ryanair");

    var json = await GetCachedStringAsync(cache, client, $"airports:{baseDomain}:{lang}", url, TimeSpan.FromHours(12), ct);
    return Results.Text(json, "application/json");
}).RequireRateLimiting("api");

app.MapGet("/api/ryanair/routes/{origin}", async (
    string origin,
    string? lang,
    string? locateDomain,
    IHttpClientFactory http,
    IMemoryCache cache,
    CancellationToken ct) =>
{
    lang ??= "en";
    origin = origin.Trim().ToUpperInvariant();
    var baseDomain = PickBaseLocateDomain(locateDomain ?? "www");

    // Seen in site traffic: https://www.ryanair.com/api/views/locate/searchWidget/routes/{lang}/airport/{IATA} :contentReference[oaicite:6]{index=6}
    var url = $"{baseDomain}/api/views/locate/searchWidget/routes/{Uri.EscapeDataString(lang)}/airport/{Uri.EscapeDataString(origin)}";
    using var client = http.CreateClient("ryanair");

    var json = await GetCachedStringAsync(cache, client, $"routes:{baseDomain}:{lang}:{origin}", url, TimeSpan.FromHours(6), ct);
    return Results.Text(json, "application/json");
}).RequireRateLimiting("api");

app.MapGet("/api/ryanair/cheapest", async (
    string origin,
    string destination,
    string month,
    string? currency,
    IHttpClientFactory http,
    IMemoryCache cache,
    CancellationToken ct) =>
{
    origin = origin.Trim().ToUpperInvariant();
    destination = destination.Trim().ToUpperInvariant();
    currency ??= "EUR";

    // month expected: YYYY-MM-01 or YYYY-MM
    DateOnly from;
    if (month.Length == 7) // YYYY-MM
        from = DateOnly.ParseExact(month + "-01", "yyyy-MM-dd", CultureInfo.InvariantCulture);
    else
        from = DateOnly.Parse(month, CultureInfo.InvariantCulture);

    var to = from.AddMonths(1).AddDays(-1);

    // Seen in site traffic: https://www.ryanair.com/api/farfnd/3/oneWayFares/ORI/DST/cheapestPerDay?... :contentReference[oaicite:7]{index=7}
    var url =
        $"https://www.ryanair.com/api/farfnd/3/oneWayFares/{Uri.EscapeDataString(origin)}/{Uri.EscapeDataString(destination)}/cheapestPerDay" +
        $"?outboundDateFrom={from:yyyy-MM-dd}&outboundDateTo={to:yyyy-MM-dd}&currency={Uri.EscapeDataString(currency)}";

    using var client = http.CreateClient("ryanair");

    var cacheKey = $"cheapest:{origin}:{destination}:{from:yyyy-MM}:{currency}";
    var json = await GetCachedStringAsync(cache, client, cacheKey, url, TimeSpan.FromMinutes(20), ct);

    using var doc = JsonDocument.Parse(json);
    var fare = TryFindBestFare(doc.RootElement);

    if (fare == null)
        return Results.NotFound(new { message = "No fares found for this corridor/month." });

    return Results.Ok(new
    {
        origin,
        destination,
        date = fare.Date,
        departureTime = fare.DepartureTime,
        arrivalTime = fare.ArrivalTime,
        price = fare.Price,
        currency
    });
}).RequireRateLimiting("api");

app.MapGet("/api/ryanair/calendar", async (
    string origin,
    string destination,
    string month,
    string? currency,
    IHttpClientFactory http,
    IMemoryCache cache,
    CancellationToken ct) =>
{
    origin = origin.Trim().ToUpperInvariant();
    destination = destination.Trim().ToUpperInvariant();
    currency ??= "EUR";

    DateOnly from;
    if (month.Length == 7)
        from = DateOnly.ParseExact(month + "-01", "yyyy-MM-dd", CultureInfo.InvariantCulture);
    else
        from = DateOnly.Parse(month, CultureInfo.InvariantCulture);

    var to = from.AddMonths(1).AddDays(-1);
    var url =
        $"https://www.ryanair.com/api/farfnd/3/oneWayFares/{Uri.EscapeDataString(origin)}/{Uri.EscapeDataString(destination)}/cheapestPerDay" +
        $"?outboundDateFrom={from:yyyy-MM-dd}&outboundDateTo={to:yyyy-MM-dd}&currency={Uri.EscapeDataString(currency)}";

    using var client = http.CreateClient("ryanair");
    var cacheKey = $"calendar:{origin}:{destination}:{from:yyyy-MM}:{currency}";
    string json;
    try
    {
        json = await GetCachedStringAsync(cache, client, cacheKey, url, TimeSpan.FromMinutes(20), ct);
    }
    catch
    {
        return Results.Problem("Could not fetch fare calendar from Ryanair.");
    }

    var dailyFares = ParseAllDailyFares(json);
    return Results.Ok(new { origin, destination, month = from.ToString("yyyy-MM"), currency, dailyFares });
}).RequireRateLimiting("api");

// -------------------- High-level endpoint the UI uses --------------------
app.MapGet("/api/ryanair/search", async (
    string origin,
    string month,                 // YYYY-MM (recommended) or YYYY-MM-01
    string? currency,
    int? maxBudget,
    int? maxDeals,
    string? lang,
    string? locateDomain,         // "www" (default) or "services"
    IHttpClientFactory http,
    IMemoryCache cache,
    CancellationToken ct) =>
{
    origin = origin.Trim().ToUpperInvariant();
    currency ??= "EUR";
    lang ??= "en";
    maxBudget ??= 150;
    maxDeals ??= 30;

    DateOnly from;
    if (month.Length == 7) // YYYY-MM
        from = DateOnly.ParseExact(month + "-01", "yyyy-MM-dd", CultureInfo.InvariantCulture);
    else
        from = DateOnly.Parse(month, CultureInfo.InvariantCulture);
    var to = from.AddMonths(1).AddDays(-1);

    using var client = http.CreateClient("ryanair");

    // 1) Airports (for coordinates + names)
    var baseDomain = PickBaseLocateDomain(locateDomain ?? "www");
    var airportsUrl = $"{baseDomain}/api/views/locate/3/airports/{Uri.EscapeDataString(lang)}/active";
    var airportsJson = await GetCachedStringAsync(cache, client, $"airports:{baseDomain}:{lang}", airportsUrl, TimeSpan.FromHours(12), ct);

    using var airportsDoc = JsonDocument.Parse(airportsJson);

    // Build airport dictionary: IATA -> meta
    var airports = new Dictionary<string, AirportMeta>(StringComparer.OrdinalIgnoreCase);

    foreach (var el in airportsDoc.RootElement.EnumerateArray())
    {
        // Defensive: Ryanair shapes can vary by version; commonly iataCode + coordinates.
        var iata =
            el.TryGetProperty("iataCode", out var i1) ? i1.GetString() :
            el.TryGetProperty("code", out var i2) ? i2.GetString() :
            null;

        if (string.IsNullOrWhiteSpace(iata)) continue;
        iata = iata!.Trim().ToUpperInvariant();

        string name =
            el.TryGetProperty("name", out var n) ? (n.GetString() ?? iata) :
            el.TryGetProperty("seoName", out var sn) ? (sn.GetString() ?? iata) :
            iata;

        string country =
            el.TryGetProperty("countryName", out var cn) ? (cn.GetString() ?? "") :
            el.TryGetProperty("country", out var c2) ? (c2.GetString() ?? "") :
            "";

        double? lat = null;
        double? lng = null;

        if (el.TryGetProperty("coordinates", out var coords) && coords.ValueKind == JsonValueKind.Object)
        {
            if (coords.TryGetProperty("latitude", out var la) && la.ValueKind == JsonValueKind.Number) lat = la.GetDouble();
            if (coords.TryGetProperty("longitude", out var lo) && lo.ValueKind == JsonValueKind.Number) lng = lo.GetDouble();
        }
        else
        {
            // fallback fields
            if (el.TryGetProperty("latitude", out var la2) && la2.ValueKind == JsonValueKind.Number) lat = la2.GetDouble();
            if (el.TryGetProperty("longitude", out var lo2) && lo2.ValueKind == JsonValueKind.Number) lng = lo2.GetDouble();
        }

        airports[iata] = new AirportMeta(iata, name, country, lat, lng);
    }

    if (!airports.ContainsKey(origin))
    {
        return Results.BadRequest(new { message = $"Unknown origin airport '{origin}'. Try another IATA code." });
    }

    // 2) Routes for origin
    var routesUrl = $"{baseDomain}/api/views/locate/searchWidget/routes/{Uri.EscapeDataString(lang)}/airport/{Uri.EscapeDataString(origin)}";
    var routesJson = await GetCachedStringAsync(cache, client, $"routes:{baseDomain}:{lang}:{origin}", routesUrl, TimeSpan.FromHours(6), ct);

    using var routesDoc = JsonDocument.Parse(routesJson);

    var destinations = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

    // Routes commonly come as an array of objects with arrivalAirport/iataCode-like fields.
    // Routes JSON varies: destination fields may be strings OR objects.
// This parser safely extracts IATA codes from either shape.
static IEnumerable<string> ExtractIatas(JsonElement el)
{
    switch (el.ValueKind)
    {
        case JsonValueKind.String:
        {
            var s = el.GetString();
            if (!string.IsNullOrWhiteSpace(s)) yield return s!;
            yield break;
        }

        case JsonValueKind.Object:
        {
            // Common nested keys seen in various Ryanair locate payloads
            string[] keys = { "iataCode", "code", "airportCode", "arrivalAirport", "arrivalAirportCode", "destination", "to", "value" };

            foreach (var k in keys)
            {
                if (el.TryGetProperty(k, out var v))
                {
                    foreach (var x in ExtractIatas(v))
                        yield return x;
                }
            }

            // Also walk all properties defensively
            foreach (var p in el.EnumerateObject())
            {
                foreach (var x in ExtractIatas(p.Value))
                    yield return x;
            }

            yield break;
        }

        case JsonValueKind.Array:
        {
            foreach (var item in el.EnumerateArray())
            {
                foreach (var x in ExtractIatas(item))
                    yield return x;
            }
            yield break;
        }

        default:
            yield break;
    }
}

void WalkRoutes(JsonElement node)
{
    if (node.ValueKind == JsonValueKind.Array)
    {
        foreach (var x in node.EnumerateArray())
            WalkRoutes(x);
        return;
    }

    if (node.ValueKind != JsonValueKind.Object)
        return;

    // Try extracting from typical top-level keys first
    string[] directKeys = { "arrivalAirport", "arrivalAirportCode", "destination", "to" };
    foreach (var k in directKeys)
    {
        if (node.TryGetProperty(k, out var v))
        {
            foreach (var raw in ExtractIatas(v))
            {
                var dst = raw.Trim().ToUpperInvariant();
                if (dst.Length == 3) destinations.Add(dst);
            }
        }
    }

    // Then walk nested content
    foreach (var prop in node.EnumerateObject())
        WalkRoutes(prop.Value);
}

WalkRoutes(routesDoc.RootElement);


    // keep only destinations we have coordinates for
    var destList = destinations.Where(d => airports.ContainsKey(d)).ToList();

    // 3) For each destination: fetch best fare in month (bounded concurrency)
    var originMeta = airports[origin];
    var semaphore = new SemaphoreSlim(4); // keep low to reduce 429 risk
    var tasks = destList.Select(async dst =>
    {
        await semaphore.WaitAsync(ct);
        try
        {
            var cacheKey = $"cheapest:{origin}:{dst}:{from:yyyy-MM}:{currency}";
            var cheapestUrl =
                $"https://www.ryanair.com/api/farfnd/3/oneWayFares/{Uri.EscapeDataString(origin)}/{Uri.EscapeDataString(dst)}/cheapestPerDay" +
                $"?outboundDateFrom={from:yyyy-MM-dd}&outboundDateTo={to:yyyy-MM-dd}&currency={Uri.EscapeDataString(currency)}";

            string cheapestJson;
            try
            {
                cheapestJson = await GetCachedStringAsync(cache, client, cacheKey, cheapestUrl, TimeSpan.FromMinutes(20), ct);
            }
            catch
            {
                return (DealResult?)null; // skip corridor on errors
            }

            using var doc = JsonDocument.Parse(cheapestJson);
            var fare = TryFindBestFare(doc.RootElement);
            if (fare == null) return (DealResult?)null;
            if (fare.Price > maxBudget.Value) return (DealResult?)null;

            var dstMeta = airports[dst];

            if (originMeta.Lat is null || originMeta.Lng is null || dstMeta.Lat is null || dstMeta.Lng is null)
                return (DealResult?)null;

            var km = HaversineKm(originMeta.Lat.Value, originMeta.Lng.Value, dstMeta.Lat.Value, dstMeta.Lng.Value);
            var pricePerKm = km > 1 ? (double)fare.Price / km : (double)fare.Price;

            return new DealResult(
                Origin: origin,
                Destination: dst,
                Date: fare.Date,
                DepartureTime: fare.DepartureTime,
                ArrivalTime: fare.ArrivalTime,
                Price: fare.Price,
                Currency: currency!,
                DistanceKm: Math.Round(km),
                PricePerKm: Math.Round(pricePerKm, 4),
                DestinationName: dstMeta.Name,
                DestinationCountry: dstMeta.Country,
                DestLat: dstMeta.Lat.Value,
                DestLng: dstMeta.Lng.Value,
                OriginName: originMeta.Name,
                OriginCountry: originMeta.Country,
                OriginLat: originMeta.Lat.Value,
                OriginLng: originMeta.Lng.Value
            );

        }
        finally
        {
            // tiny jitter to spread calls
            await Task.Delay(Random.Shared.Next(30, 90), ct);
            semaphore.Release();
        }
    }).ToList();

    var deals = (await Task.WhenAll(tasks))
        .Where(x => x != null)
        .Select(x => x!)
        .OrderBy(x => x.Price)
        .Take(maxDeals.Value)
        .ToList();

    return Results.Ok(new
    {
        origin,
        month = from.ToString("yyyy-MM"),
        currency,
        maxBudget,
        maxDeals,
        routeCount = destList.Count,
        dealCount = deals.Count,
        deals
    });

}).RequireRateLimiting("api");

app.Run();

record AirportMeta(string Iata, string Name, string Country, double? Lat, double? Lng);
record FareSummary(string Date, decimal Price, string? DepartureTime, string? ArrivalTime);
record DailyFare(string Date, decimal Price);
record AgentChatMessage(string Role, string Content);
record AgentTravelContext(
    string? Origin,
    string? Month,
    string? Currency,
    decimal? MaxBudget,
    int? MaxDeals,
    string? DestinationIdea,
    string? DestinationSearch,
    int? MaxDistanceKm,
    int? MinDistanceKm,
    bool PreferWeekend,
    bool OnlyHotDeals,
    string? Notes);

record AgentTravelContextPatch(
    string? Origin,
    string? Month,
    string? Currency,
    decimal? MaxBudget,
    int? MaxDeals,
    string? DestinationIdea,
    string? DestinationSearch,
    int? MaxDistanceKm,
    int? MinDistanceKm,
    bool? PreferWeekend,
    bool? OnlyHotDeals,
    string? Notes);
record AgentChatRequest(List<AgentChatMessage>? Messages, AgentTravelContext? TravelContext);
record AgentChatResponse(
    string Status,
    AgentChatMessage AssistantMessage,
    AgentTravelContext TravelContext,
    string TravelBrief,
    string[] MissingFields,
    string[] SuggestedReplies,
    AgentDealCard[] DealShortlist);
record AgentDealCard(
    string Destination,
    string DestinationName,
    string Date,
    decimal Price,
    string Currency,
    double DistanceKm,
    double PricePerKm,
    string Reason,
    bool IsAlternative);
record OpenAiAgentResult(string? AssistantMessage, string[] SuggestedReplies);
record RyanairSearchResponse(List<DealResult>? Deals);

record DealResult(
    string Origin,
    string Destination,
    string Date,
    string? DepartureTime,
    string? ArrivalTime,
    decimal Price,
    string Currency,
    double DistanceKm,
    double PricePerKm,
    string DestinationName,
    string DestinationCountry,
    double DestLat,
    double DestLng,
    string OriginName,
    string OriginCountry,
    double OriginLat,
    double OriginLng
);

