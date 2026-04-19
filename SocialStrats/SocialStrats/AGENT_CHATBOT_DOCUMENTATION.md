# SocialStrats Agent Chatbot Documentation

## Overview
This document describes the current implementation of the AI trip agent in SocialStrats, including API flow, fallback behavior, UI behavior, and deployment notes.

The chatbot is implemented on top of:
- Backend endpoint: `POST /api/agent/chat`
- Search endpoint: `GET /api/ryanair/search`
- OpenAI provider: `https://api.openai.com/v1/chat/completions`

## What Changed

### 1) Real LLM provider integration (OpenAI)
- Added OpenAI client and request pipeline in backend.
- Added system prompt and structured JSON response parsing.
- Added localized generation behavior for English, Slovak, and German.

### 2) Agent behavior improvements
- `maxDistanceKm` is no longer mandatory.
- `destination` is now optional for discovery mode (user can ask for random/best options).
- If strict destination has no direct deals, agent now returns **alternatives** (nearby hubs).
- Added explicit `alternatives` status and visual badge in UI.

### 3) Chat UX improvements
- Removed hardcoded starter sample conversation from initial thread.
- Kept only a single assistant intro message on chat init.
- Added shortlist cards rendering in all language pages (`Index`, `Index.sk`, `Index.de`).

## Backend Architecture

### Primary endpoint
`POST /api/agent/chat`

#### Request
- `messages`: chat history (`role`, `content`)
- `travelContext`:
  - `origin`
  - `month`
  - `currency`
  - `maxBudget`
  - `maxDeals`
  - `destinationIdea`
  - `destinationSearch`
  - `maxDistanceKm`
  - `minDistanceKm`
  - `preferWeekend`
  - `onlyHotDeals`
  - `notes`

#### Response
- `status`: one of
  - `needs-more-info`
  - `ready-with-deals`
  - `no-deals`
  - `alternatives`
- `assistantMessage`
- `travelContext`
- `travelBrief`
- `missingFields`
- `suggestedReplies`
- `dealShortlist`:
  - destination fields
  - pricing fields
  - reason
  - `isAlternative` flag

## Decision Flow

1. Validate required fields.
- Required: `origin`, `month`, `maxBudget`
- Optional: destination (discovery mode supported)

2. Fetch candidate deals.
- Uses widened internal budget envelope to avoid empty output in strict cases.
- Applies distance and weekend filters.

3. Destination logic.
- If destination specified: filter to matching destination.
- If no matching direct deals: compute nearby alternatives and return `status=alternatives`.

4. LLM response generation.
- Build structured prompt with context + selected deals.
- Parse JSON response (`assistantMessage`, `suggestedReplies`).
- Fallback to deterministic text if LLM response fails.

## Alternatives Mode
When selected destination has no direct deals:
- Resolve destination anchor airport/city from Ryanair airport catalog.
- Rank available deals by proximity to anchor + price/value.
- Return top alternatives with `isAlternative=true`.
- UI shows explicit `Alternative` badge.

## Frontend Integration

### Affected pages
- `Pages/Index.cshtml`
- `Pages/Index.sk.cshtml`
- `Pages/Index.de.cshtml`

### Behavior
- Initial thread starts with intro assistant message only.
- On each agent response:
  - update chat thread
  - update context summary
  - update suggested replies chips
  - render shortlist cards
  - show alternative badge when `isAlternative=true`

## Configuration

### Environment variables
Loaded from `SocialStrats/SocialStrats/.env`:
- `OPENAI_API_KEY`
- `OPENAI_MODEL` (default: `gpt-4o-mini`)

### Git safety
- `.env` is ignored by git through root `.gitignore`.

## Run Instructions
From repository root:

```bash
cd "/Users/serhiivielkin/Desktop/cernansky project/RYSTATS2"
dotnet run --project SocialStrats/SocialStrats/SocialStrats.csproj --urls http://localhost:5303
```

Open:
- `http://localhost:5303`

## Validation Examples

### Discovery mode (no destination)
Should return `ready-with-deals` and non-empty shortlist.

### Strict destination without direct deals (e.g. Paris case)
Should return `alternatives` with non-empty shortlist and `isAlternative=true` cards.

## Known Limitations
- OpenAI calls add latency per message.
- Ryanair upstream availability varies by market/month and can change quickly.
- `TryGetFlightTimesAsync` helper is currently unused (compiler warning only).

## Files Touched By This Feature
- `SocialStrats/SocialStrats/Program.cs`
- `SocialStrats/SocialStrats/Pages/Index.cshtml`
- `SocialStrats/SocialStrats/Pages/Index.sk.cshtml`
- `SocialStrats/SocialStrats/Pages/Index.de.cshtml`
- `SocialStrats/SocialStrats/AGENT_CHATBOT_DOCUMENTATION.md`
- `.gitignore`
