# SocialStrats — Ryanair Flight Deal Scanner

A multilingual web application for finding cheap Ryanair flights, comparing deals on a live map, and planning trips with an AI-powered chat assistant.

---

## Overview

SocialStrats scans Ryanair's public flight data for a chosen origin airport and month, ranks results by price and value (price per km), and displays them on an interactive map. An integrated AI agent (powered by OpenAI) helps users define their trip preferences through a conversation and then surfaces the most relevant deals.

---

## Features

| Feature | Description |
|---|---|
| **Live deal scan** | Scans all Ryanair routes from your chosen origin airport for the selected month |
| **Interactive map** | Leaflet-based map with animated plane routes, price labels, and clickable markers |
| **AI trip planner** | Conversational agent (OpenAI GPT-4o-mini) that takes your budget, distance, and vibe and returns ranked deals |
| **Fare calendar** | Full month price grid for any specific route, color-coded by price level |
| **Return flight modal** | Set trip length in days, see the exact return fare, browse a return calendar, get combined round-trip total |
| **Saved trips** | Save favorite deals to browser localStorage, export as CSV |
| **Analytics tab** | Price histogram, €/km scatter chart, median/average stats, best day-of-week, calendar view |
| **Multilingual** | English, Slovak, German (cookie/query string/Accept-Language based switching) |
| **CSV export** | Export filtered deals or saved favorites to CSV |

---

## Tech Stack

| Layer | Technology |
|---|---|
| Framework | ASP.NET Core (.NET 10), Razor Pages |
| Language | C# 13 |
| Frontend | Vanilla JavaScript, HTML5, CSS3 |
| Map | Leaflet.js 1.9.4 |
| Charts | Chart.js 4.4.1 |
| AI | OpenAI API (`gpt-4o-mini` by default) |
| Data source | Ryanair public API (unofficial, no authentication required) |
| Caching | ASP.NET Core IMemoryCache |
| Rate limiting | ASP.NET Core RateLimiter (60 req/min per IP) |
| Database | None — fully stateless |

---

## Project Structure

```
SocialStrats/
├── SocialStrats.sln
└── SocialStrats/
    ├── Program.cs                  # All backend logic, API endpoints, data models
    ├── SocialStrats.csproj
    ├── appsettings.json
    ├── appsettings.Development.json
    ├── .env                        # Secret keys (not committed)
    ├── .env.example                # Template for environment variables
    ├── Pages/
    │   ├── Index.cshtml            # Main page (English) — all UI + JavaScript
    │   ├── Index.cshtml.cs
    │   ├── Index.sk.cshtml         # Slovak version
    │   ├── Index.de.cshtml         # German version
    │   ├── Privacy.cshtml
    │   ├── SetLanguage.cshtml      # Language switcher
    │   └── Shared/
    │       └── _Layout.cshtml      # Master layout
    ├── Properties/
    │   └── launchSettings.json
    └── wwwroot/
        ├── css/
        ├── js/
        └── lib/                    # Bootstrap, jQuery, jQuery Validation
```

---

## Prerequisites

- [.NET 10 SDK](https://dotnet.microsoft.com/download/dotnet/10.0)
- An [OpenAI API key](https://platform.openai.com/api-keys) (required for the AI planner; the deal scanner works without it)

---

## Setup

### 1. Clone the repository

```bash
git clone https://github.com/PhoeniX987/RYSTATS2V.git
cd RYSTATS2V/SocialStrats
```

### 2. Create your environment file

Copy the example and fill in your real values:

```bash
cp SocialStrats/.env.example SocialStrats/.env
```

Edit `SocialStrats/.env`:

```
OPENAI_API_KEY=sk-proj-your-real-key-here
OPENAI_MODEL=gpt-4o-mini
```

> The `.env` file is in `.gitignore` and will never be committed.

### 3. Run the application

```bash
dotnet run --project SocialStrats
```

The app will be available at:
- HTTP: `http://localhost:5295`
- HTTPS: `https://localhost:7043`

---

## Environment Variables

| Variable | Required | Default | Description |
|---|---|---|---|
| `OPENAI_API_KEY` | No* | — | OpenAI API key for the AI trip planner |
| `OPENAI_MODEL` | No | `gpt-4o-mini` | OpenAI model to use |

> *Without `OPENAI_API_KEY` the deal scanner and all other features still work. Only the AI chat assistant is disabled.

---

## API Endpoints

All endpoints are rate-limited to **60 requests per minute per IP**.

### `GET /api/ryanair/search`

Main search endpoint. Scans all routes from an origin airport and returns deals sorted by price.

| Parameter | Type | Required | Description |
|---|---|---|---|
| `origin` | string | Yes | IATA airport code (e.g. `BTS`, `STN`, `DUB`) |
| `month` | string | Yes | Travel month in `YYYY-MM` format |
| `currency` | string | No | Currency code. Default: `EUR` |
| `maxBudget` | int | No | Maximum price filter. Default: `150` |
| `maxDeals` | int | No | Maximum results to return. Default: `30` |
| `lang` | string | No | Language for airport names. Default: `en` |

**Example:**
```
GET /api/ryanair/search?origin=BTS&month=2025-06&currency=EUR&maxBudget=120&maxDeals=40
```

---

### `GET /api/ryanair/calendar`

Returns all daily prices for a specific route and month. Used by the fare calendar and return flight modal.

| Parameter | Type | Required | Description |
|---|---|---|---|
| `origin` | string | Yes | Departure IATA code |
| `destination` | string | Yes | Arrival IATA code |
| `month` | string | Yes | `YYYY-MM` |
| `currency` | string | No | Default: `EUR` |

**Example:**
```
GET /api/ryanair/calendar?origin=BTS&destination=BCN&month=2025-06&currency=EUR
```

---

### `GET /api/ryanair/cheapest`

Returns only the single cheapest fare for a route in a given month.

| Parameter | Type | Required | Description |
|---|---|---|---|
| `origin` | string | Yes | Departure IATA code |
| `destination` | string | Yes | Arrival IATA code |
| `month` | string | Yes | `YYYY-MM` |
| `currency` | string | No | Default: `EUR` |

---

### `GET /api/ryanair/airports`

Returns the full list of active Ryanair airports with coordinates and country info.

| Parameter | Type | Required | Description |
|---|---|---|---|
| `lang` | string | No | Language. Default: `en` |

---

### `GET /api/ryanair/routes/{origin}`

Returns all routes (destination airports) served by Ryanair from a given origin.

| Parameter | Type | Required | Description |
|---|---|---|---|
| `origin` | string | Yes | Origin IATA code (path parameter) |
| `lang` | string | No | Default: `en` |

---

### `POST /api/agent/chat`

AI trip planner endpoint. Accepts a conversation thread and travel context, returns an assistant message and ranked deal shortlist.

**Request body:**
```json
{
  "messages": [
    { "role": "user", "content": "I want a beach trip under €120 from BTS in June" }
  ],
  "travelContext": {
    "origin": "BTS",
    "month": "2025-06",
    "currency": "EUR",
    "maxBudget": 120,
    "maxDeals": 10,
    "destinationIdea": "beach",
    "maxDistanceKm": 2000,
    "preferWeekend": false,
    "onlyHotDeals": false
  }
}
```

**Response:**
```json
{
  "status": "ready-with-deals",
  "assistantMessage": { "role": "assistant", "content": "..." },
  "travelContext": { ... },
  "travelBrief": "origin BTS, month 2025-06, budget up to 120 EUR",
  "missingFields": [],
  "suggestedReplies": ["Show weekend only", "Find best €/km"],
  "dealShortlist": [
    {
      "destination": "AGP",
      "destinationName": "Malaga",
      "date": "2025-06-14",
      "price": 89.99,
      "currency": "EUR",
      "distanceKm": 2150,
      "pricePerKm": 0.0418,
      "reason": "smart value: price fits, distance within range",
      "isAlternative": false
    }
  ]
}
```

---

## Caching

| Data | TTL |
|---|---|
| Airport catalog | 12 hours |
| Routes per origin | 6 hours |
| Cheapest fares per corridor | 20 minutes |
| Booking availability | 30 minutes |
| Fare calendar | 20 minutes |

Caching is in-memory and resets on server restart.

---

## Language Support

The UI is available in three languages. The active language is determined in this priority order:

1. Cookie (`ss_lang`)
2. Query string (`?culture=sk`)
3. Browser `Accept-Language` header

**Supported codes:** `en` (English), `sk` (Slovak), `de` (German)

To switch language, navigate to `/SetLanguage?culture=sk&returnUrl=/`.

---

## How the Deal Scanner Works

1. Fetches the full Ryanair airport catalog (cached 12h)
2. Fetches all routes from the chosen origin (cached 6h)
3. For each destination route, calls the Ryanair `cheapestPerDay` API to get the cheapest fare in the selected month (cached 20min, up to 4 concurrent requests)
4. Filters results by `maxBudget`
5. Calculates distance using the Haversine formula
6. Returns deals sorted by price, including price-per-km for value comparison

> **Note:** This app uses Ryanair's unofficial public API. It has no affiliation with Ryanair. The API may change or become unavailable at any time.

---

## Known Limitations

- **One origin at a time** — the scanner searches from a single departure airport per scan
- **One-way focus** — the main scan is one-way; return fares are fetched separately via the Return modal
- **No user accounts** — saved trips use browser `localStorage` and are not synced across devices
- **Ryanair only** — does not cover other airlines
- **Unofficial API** — Ryanair may rate-limit or change their API endpoints without notice; the app includes retry logic with exponential backoff

---

## License

This project is for personal and educational use. Not affiliated with or endorsed by Ryanair DAC.
