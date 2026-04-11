# AI agent handoff

Tento projekt už má pripravené UI aj backend stub pre chat s agentom.

## Kde to je

- Frontend UI:
  - `Pages/Index.cshtml`
  - `Pages/Index.sk.cshtml`
- Backend stub:
  - `Program.cs`
  - endpoint: `POST /api/agent/chat`

## Čo už je hotové

- Hero sekcia má chat panel nad headline.
- UI zbiera:
  - kam chce používateľ ísť / aký vibe tripu chce
  - budget
  - max vzdialenosť
  - poznámky typu `víkend`, `more`, `direct`
  - aktuálny `origin`, `month`, `currency`, `maxBudget`, filtre zo stránky
- Frontend posiela tieto dáta na backend ako JSON.
- Backend zatiaľ nevolá reálny model. Len vracia:
  - assistant správu
  - travel brief
  - missing fields
  - suggested replies

## Request shape

Frontend posiela na `/api/agent/chat` približne toto:

```json
{
  "messages": [
    { "role": "assistant", "content": "..." },
    { "role": "user", "content": "I want a warm beach trip..." }
  ],
  "travelContext": {
    "origin": "BTS",
    "month": "2026-05",
    "currency": "EUR",
    "maxBudget": 120,
    "maxDeals": 40,
    "destinationIdea": "warm beach",
    "destinationSearch": null,
    "maxDistanceKm": 1500,
    "minDistanceKm": null,
    "preferWeekend": true,
    "onlyHotDeals": false,
    "notes": "direct flights, sun"
  }
}
```

## Response shape

Backend vracia:

```json
{
  "status": "needs-more-info",
  "assistantMessage": {
    "role": "assistant",
    "content": "..."
  },
  "travelContext": {
    "origin": "BTS",
    "month": "2026-05",
    "currency": "EUR",
    "maxBudget": 120,
    "maxDeals": 40,
    "destinationIdea": "warm beach",
    "destinationSearch": null,
    "maxDistanceKm": 1500,
    "minDistanceKm": null,
    "preferWeekend": true,
    "onlyHotDeals": false,
    "notes": "direct flights, sun"
  },
  "travelBrief": "origin BTS, month 2026-05, budget up to 120 EUR, ...",
  "missingFields": ["distance"],
  "suggestedReplies": [
    "Keep it within 1500 km."
  ]
}
```

## Čo treba dorobiť

Nahradiť stub v `Program.cs` reálnym volaním na AI providera.

Teraz endpoint:

1. zoberie `messages` + `travelContext`
2. skontroluje, čo chýba
3. vráti placeholder odpoveď

Ty tam máš spraviť:

1. Zostaviť system prompt.
2. Poslať do modelu celé `messages`.
3. Do promptu priložiť aj `travelContext`.
4. Nechať model:
   - dopýtať sa chýbajúce info
   - alebo keď má dosť info, navrhnúť shortlist trás
5. Po získaní dosť info zavolať existujúci endpoint:
   - `GET /api/ryanair/search`
6. Výsledné deals nechať agentom zoradiť a vysvetliť.

## Čo sa má agent pýtať

Minimum:

- Kam chce používateľ ísť alebo aký typ tripu chce.
- Odkiaľ letí.
- Aký má budget.
- Ako ďaleko chce letieť.
- Kedy chce cestovať.

Odporúčané doplňujúce otázky:

- Víkend vs. dlhší trip.
- Teplo / more / city break / príroda.
- Priame lety áno / nie.
- Sólo / pár / partia.
- Či chce najlacnejšie lety alebo najlepší pomer cena / vzdialenosť.

## Odporúčaný flow

1. Agent zozbiera brief.
2. Keď má dosť info, zavolá `/api/ryanair/search`.
3. Zo search response si vezme `deals`.
4. Vyberie top 3 až 5 možností.
5. Ku každej možnosti vysvetlí:
   - prečo sedí na budget
   - prečo sedí na vzdialenosť
   - či je to skôr cheap pick alebo smart value pick

## Jednoduchý system prompt

```text
You are a flight planning assistant for SocialStrats.
Your job is to help the user define a clear travel brief first.
Ask short follow-up questions until you know:
- destination or trip vibe
- origin airport
- budget
- max distance or tolerance for longer flights
- travel month or date window

When enough information is available, summarize the brief clearly and prepare the data for route search.
When Ryanair deals are available, rank them by fitness to the user's brief, not just by raw price.
Be concise, practical, and explain tradeoffs.
```

## Poznámka k UI

Frontend už vie:

- zobraziť thread správ
- posielať request na `/api/agent/chat`
- zobraziť suggested replies
- ukázať summary pill-y s budgetom, mesiacom a vzdialenosťou

Čiže ďalší krok už nie je robiť nový frontend, ale len napojiť reálnu AI logiku na existujúci endpoint.
