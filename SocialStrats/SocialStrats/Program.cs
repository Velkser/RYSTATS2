using System.Globalization;
using System.Net;
using System.Net.Http.Headers;
using System.Text.Json;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.Extensions.Caching.Memory;
using System.Globalization;
using Microsoft.AspNetCore.Localization;
using Microsoft.AspNetCore.Mvc.Razor;


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

    decimal? bestPrice = null;
    string? bestDate = null;

    static void Consider(JsonElement e, ref decimal? bestPrice, ref string? bestDate)
    {
        string? date =
            e.TryGetProperty("dateOut", out var dateOut) ? dateOut.GetString() :
            e.TryGetProperty("day", out var day) ? day.GetString() :
            null;

        decimal? price =
            e.TryGetProperty("price", out var p) && p.ValueKind == JsonValueKind.Number ? p.GetDecimal() :
            (e.TryGetProperty("price", out var p2) && p2.ValueKind == JsonValueKind.Object && p2.TryGetProperty("value", out var pv) && pv.ValueKind == JsonValueKind.Number) ? pv.GetDecimal() :
            (e.TryGetProperty("fare", out var f) && f.ValueKind == JsonValueKind.Object && f.TryGetProperty("amount", out var fa) && fa.ValueKind == JsonValueKind.Number) ? fa.GetDecimal() :
            null;

        if (date != null && price != null)
        {
            if (bestPrice == null || price.Value < bestPrice.Value)
            {
                bestPrice = price.Value;
                bestDate = date;
            }
        }
    }

    static void Walk(JsonElement el, ref decimal? bestPrice, ref string? bestDate)
    {
        if (el.ValueKind == JsonValueKind.Array)
        {
            foreach (var x in el.EnumerateArray()) Walk(x, ref bestPrice, ref bestDate);
            return;
        }
        if (el.ValueKind == JsonValueKind.Object)
        {
            Consider(el, ref bestPrice, ref bestDate);
            foreach (var prop in el.EnumerateObject())
                Walk(prop.Value, ref bestPrice, ref bestDate);
        }
    }

    Walk(doc.RootElement, ref bestPrice, ref bestDate);

    if (bestPrice == null || bestDate == null)
        return Results.NotFound(new { message = "No fares found for this corridor/month." });

    return Results.Ok(new { origin, destination, date = bestDate, price = bestPrice, currency });
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

            decimal? bestPrice = null;
            string? bestDate = null;

            static void ConsiderFare(JsonElement e, ref decimal? bestPrice, ref string? bestDate)
            {
                string? date =
                    e.TryGetProperty("dateOut", out var dateOut) ? dateOut.GetString() :
                    e.TryGetProperty("day", out var day) ? day.GetString() :
                    null;

                decimal? price =
                    e.TryGetProperty("price", out var p) && p.ValueKind == JsonValueKind.Number ? p.GetDecimal() :
                    (e.TryGetProperty("price", out var p2) && p2.ValueKind == JsonValueKind.Object && p2.TryGetProperty("value", out var pv) && pv.ValueKind == JsonValueKind.Number) ? pv.GetDecimal() :
                    (e.TryGetProperty("fare", out var f) && f.ValueKind == JsonValueKind.Object && f.TryGetProperty("amount", out var fa) && fa.ValueKind == JsonValueKind.Number) ? fa.GetDecimal() :
                    null;

                if (date != null && price != null)
                {
                    if (bestPrice == null || price.Value < bestPrice.Value)
                    {
                        bestPrice = price.Value;
                        bestDate = date;
                    }
                }
            }

            static void WalkFares(JsonElement el, ref decimal? bestPrice, ref string? bestDate)
            {
                if (el.ValueKind == JsonValueKind.Array)
                {
                    foreach (var x in el.EnumerateArray()) WalkFares(x, ref bestPrice, ref bestDate);
                    return;
                }
                if (el.ValueKind == JsonValueKind.Object)
                {
                    ConsiderFare(el, ref bestPrice, ref bestDate);
                    foreach (var prop in el.EnumerateObject())
                        WalkFares(prop.Value, ref bestPrice, ref bestDate);
                }
            }

            WalkFares(doc.RootElement, ref bestPrice, ref bestDate);

            if (bestPrice == null || bestDate == null) return (DealResult?)null;
            if (bestPrice.Value > maxBudget.Value) return (DealResult?)null;

            var dstMeta = airports[dst];

            if (originMeta.Lat is null || originMeta.Lng is null || dstMeta.Lat is null || dstMeta.Lng is null)
                return (DealResult?)null;

            var km = HaversineKm(originMeta.Lat.Value, originMeta.Lng.Value, dstMeta.Lat.Value, dstMeta.Lng.Value);
            var pricePerKm = km > 1 ? (double)bestPrice.Value / km : (double)bestPrice.Value;

            return new DealResult(
                Origin: origin,
                Destination: dst,
                Date: bestDate!,
                DepartureTime: null,
                ArrivalTime: null,
                Price: bestPrice.Value,
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

// 4) Enrich final deals with dep/arr times (only for what we actually return)
    const string bookingMarket = "en-gb"; // stable default; you can later map from `lang` if you want
    var sem2 = new SemaphoreSlim(4);

    var enriched = await Task.WhenAll(deals.Select(async d =>
    {
        await sem2.WaitAsync(ct);
        try
        {
            // BestDate can be "yyyy-MM-dd" already; if it contains time, slice first 10 chars
            var ymd = d.Date.Length >= 10 ? d.Date.Substring(0, 10) : d.Date;

            var (dep, arr) = await TryGetFlightTimesAsync(cache, client, d.Origin, d.Destination, ymd, bookingMarket, ct);
            return d with { DepartureTime = dep, ArrivalTime = arr };
        }
        finally
        {
            await Task.Delay(Random.Shared.Next(30, 90), ct);
            sem2.Release();
        }
    }));

    deals = enriched.ToList();

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

