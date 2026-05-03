# SocialStrats - skener výhodných leteniek Ryanair

SocialStrats je webová aplikácia na hľadanie lacných letov Ryanair. Používateľ si vyberie odletové letisko, mesiac a rozpočet, aplikácia prejde dostupné trasy a výsledky zobrazí na interaktívnej mape. Okrem klasického zoznamu letov ukazuje aj hodnotu letu cez cenu za kilometer a ponúka AI asistenta, ktorý pomáha nájsť vhodnú destináciu podľa preferencií.

---

## Prehľad projektu

Cieľom projektu je zjednodušiť hľadanie lacných európskych letov. Namiesto toho, aby používateľ ručne prechádzal jednotlivé destinácie, SocialStrats načíta verejne dostupné dáta Ryanairu, zoradí ich podľa ceny a hodnoty a ukáže ich vizuálne na mape.

Aplikácia je postavená tak, aby bola použiteľná aj bez AI časti. Základný skener letov funguje samostatne. AI trip planner je doplnková funkcia, ktorá používateľovi pomáha cez krátku konverzáciu spresniť rozpočet, vzdialenosť, typ destinácie a štýl výletu.

---

## Hlavné funkcie

| Funkcia | Popis |
|---|---|
| **Vyhľadávanie letov** | Prejde Ryanair trasy z vybraného odletového letiska pre konkrétny mesiac. |
| **Interaktívna mapa** | Mapa postavená na Leaflet.js zobrazuje destinácie, ceny, markery a animované trasy. |
| **AI plánovač výletu** | Konverzačný asistent cez OpenAI API pomáha vybrať lety podľa rozpočtu, vzdialenosti a preferencií. |
| **Kalendár cien** | Zobrazuje ceny pre konkrétnu trasu počas celého mesiaca a farebne odlišuje lacnejšie a drahšie dni. |
| **Spiatočný let** | Používateľ vie nastaviť dĺžku pobytu, pozrieť cenu spiatočného letu a porovnať celkovú cenu tam aj späť. |
| **Uložené lety** | Obľúbené lety sa ukladajú do `localStorage` v prehliadači a dajú sa exportovať do CSV. |
| **Analytická časť** | Obsahuje histogram cien, graf ceny za kilometer, priemerné a mediánové hodnoty a ďalšie porovnania. |
| **Viacjazyčnosť** | Rozhranie podporuje angličtinu, slovenčinu a nemčinu. Jazyk sa prepína cez cookie, query string alebo hlavičku prehliadača. |
| **CSV export** | Výsledky vyhľadávania alebo uložené lety je možné exportovať ako CSV súbor. |

---

## Použité technológie

| Vrstva | Technológia |
|---|---|
| Framework | ASP.NET Core (.NET 10), Razor Pages |
| Jazyk | C# 13 |
| Frontend | Vanilla JavaScript, HTML5, CSS3 |
| Mapa | Leaflet.js 1.9.4 |
| Grafy | Chart.js 4.4.1 |
| AI | OpenAI API, predvolene `gpt-4o-mini` |
| Zdroj dát | Verejné Ryanair API, neoficiálne a bez potreby autentifikácie |
| Cacheovanie | ASP.NET Core IMemoryCache |
| Rate limiting | ASP.NET Core RateLimiter, 60 požiadaviek za minútu na IP adresu |
| Databáza | Nepoužíva sa. Aplikácia je bezstavová a dáta drží len dočasne v cache alebo v prehliadači. |

---

## Štruktúra projektu

```
SocialStrats/
├── SocialStrats.sln
└── SocialStrats/
    ├── Program.cs                  # Backend logika, API endpointy a dátové modely
    ├── SocialStrats.csproj
    ├── appsettings.json
    ├── appsettings.Development.json
    ├── .env                        # Lokálne tajné kľúče, necommitujú sa
    ├── .env.example                # Vzor pre premenné prostredia
    ├── Pages/
    │   ├── Index.cshtml            # Hlavná anglická stránka, UI a JavaScript
    │   ├── Index.cshtml.cs
    │   ├── Index.sk.cshtml         # Slovenská verzia
    │   ├── Index.de.cshtml         # Nemecká verzia
    │   ├── Privacy.cshtml
    │   ├── SetLanguage.cshtml      # Prepínanie jazyka
    │   └── Shared/
    │       └── _Layout.cshtml      # Spoločný layout
    ├── Properties/
    │   └── launchSettings.json
    └── wwwroot/
        ├── css/
        ├── js/
        └── lib/                    # Bootstrap, jQuery, jQuery Validation
```

---

## Požiadavky na spustenie

- [.NET 10 SDK](https://dotnet.microsoft.com/download/dotnet/10.0)
- [OpenAI API key](https://platform.openai.com/api-keys), ak chcete používať AI plánovač. Samotné vyhľadávanie letov funguje aj bez neho.

---

## Spustenie projektu

### 1. Naklonovanie repozitára

```bash
git clone https://github.com/PhoeniX987/RYSTATS2V.git
cd RYSTATS2V/SocialStrats
```

### 2. Vytvorenie `.env` súboru

Skopírujte vzorový súbor a doplňte vlastné hodnoty:

```bash
cp SocialStrats/.env.example SocialStrats/.env
```

V súbore `SocialStrats/.env` nastavte:

```
OPENAI_API_KEY=sk-proj-your-real-key-here
OPENAI_MODEL=gpt-4o-mini
```

> Súbor `.env` je v `.gitignore`, takže sa neodovzdáva do repozitára.

### 3. Spustenie aplikácie

```bash
dotnet run --project SocialStrats
```

Aplikácia bude dostupná na:
- HTTP: `http://localhost:5295`
- HTTPS: `https://localhost:7043`

---

## Premenné prostredia

| Premenná | Povinná | Predvolená hodnota | Popis |
|---|---|---|---|
| `OPENAI_API_KEY` | Nie* | - | OpenAI API kľúč pre AI plánovač výletu. |
| `OPENAI_MODEL` | Nie | `gpt-4o-mini` | Model použitý pre AI asistenta. |

> *Bez `OPENAI_API_KEY` funguje vyhľadávanie letov, mapa, kalendár, uložené lety aj export. Vypnutý bude iba AI chat asistent.

---

## API endpointy

Všetky endpointy sú chránené limitom **60 požiadaviek za minútu na jednu IP adresu**.

### `GET /api/ryanair/search`

Hlavný vyhľadávací endpoint. Prejde trasy z vybraného odletového letiska a vráti najlepšie nájdené lety zoradené podľa ceny.

| Parameter | Typ | Povinný | Popis |
|---|---|---|---|
| `origin` | string | Áno | IATA kód odletového letiska, napríklad `BTS`, `STN`, `DUB`. |
| `month` | string | Áno | Mesiac cesty vo formáte `YYYY-MM`. |
| `currency` | string | Nie | Mena. Predvolená hodnota je `EUR`. |
| `maxBudget` | int | Nie | Maximálna cena. Predvolená hodnota je `150`. |
| `maxDeals` | int | Nie | Maximálny počet výsledkov. Predvolená hodnota je `30`. |
| `lang` | string | Nie | Jazyk názvov letísk. Predvolená hodnota je `en`. |

**Príklad:**
```
GET /api/ryanair/search?origin=BTS&month=2025-06&currency=EUR&maxBudget=120&maxDeals=40
```

---

### `GET /api/ryanair/calendar`

Vráti denné ceny pre konkrétnu trasu a mesiac. Používa sa v kalendári cien a v modálnom okne pre spiatočný let.

| Parameter | Typ | Povinný | Popis |
|---|---|---|---|
| `origin` | string | Áno | IATA kód odletového letiska. |
| `destination` | string | Áno | IATA kód príletového letiska. |
| `month` | string | Áno | Mesiac vo formáte `YYYY-MM`. |
| `currency` | string | Nie | Mena. Predvolená hodnota je `EUR`. |

**Príklad:**
```
GET /api/ryanair/calendar?origin=BTS&destination=BCN&month=2025-06&currency=EUR
```

---

### `GET /api/ryanair/cheapest`

Vráti najlacnejšiu cenu pre jednu konkrétnu trasu v danom mesiaci.

| Parameter | Typ | Povinný | Popis |
|---|---|---|---|
| `origin` | string | Áno | IATA kód odletového letiska. |
| `destination` | string | Áno | IATA kód príletového letiska. |
| `month` | string | Áno | Mesiac vo formáte `YYYY-MM`. |
| `currency` | string | Nie | Mena. Predvolená hodnota je `EUR`. |

---

### `GET /api/ryanair/airports`

Vráti zoznam aktívnych Ryanair letísk vrátane súradníc a krajiny.

| Parameter | Typ | Povinný | Popis |
|---|---|---|---|
| `lang` | string | Nie | Jazyk odpovede. Predvolená hodnota je `en`. |

---

### `GET /api/ryanair/routes/{origin}`

Vráti všetky destinácie, do ktorých Ryanair lieta z vybraného odletového letiska.

| Parameter | Typ | Povinný | Popis |
|---|---|---|---|
| `origin` | string | Áno | IATA kód odletového letiska v URL ceste. |
| `lang` | string | Nie | Jazyk odpovede. Predvolená hodnota je `en`. |

---

### `POST /api/agent/chat`

Endpoint pre AI plánovač výletu. Prijíma históriu konverzácie a cestovný kontext. Vráti odpoveď asistenta a krátky zoznam odporúčaných letov.

**Telo požiadavky:**
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

**Odpoveď:**
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

## Cacheovanie

| Dáta | Platnosť cache |
|---|---|
| Katalóg letísk | 12 hodín |
| Trasy pre odletové letisko | 6 hodín |
| Najlacnejšie ceny pre konkrétnu trasu | 20 minút |
| Dostupnosť rezervácie | 30 minút |
| Kalendár cien | 20 minút |

Cache je uložená iba v pamäti aplikácie. Po reštarte servera sa vymaže.

---

## Jazyková podpora

Rozhranie je dostupné v troch jazykoch: angličtina, slovenčina a nemčina. Aktívny jazyk sa vyberá v tomto poradí:

1. Cookie (`ss_lang`)
2. Query string (`?culture=sk`)
3. Hlavička prehliadača `Accept-Language`

**Podporované kódy:** `en` (angličtina), `sk` (slovenčina), `de` (nemčina)

Prepnutie jazyka je možné cez:

```
/SetLanguage?culture=sk&returnUrl=/
```

---

## Ako funguje vyhľadávač letov

1. Aplikácia načíta katalóg Ryanair letísk a uloží ho do cache na 12 hodín.
2. Pre zvolené odletové letisko načíta dostupné trasy a uloží ich do cache na 6 hodín.
3. Pre každú destináciu zavolá Ryanair `cheapestPerDay` API a zistí najlacnejší let vo vybranom mesiaci.
4. Výsledky filtruje podľa `maxBudget`.
5. Vzdialenosť medzi letiskami počíta pomocou Haversine vzorca.
6. Výsledky vracia zoradené podľa ceny a dopĺňa aj hodnotu ceny za kilometer.

> **Poznámka:** Aplikácia používa neoficiálne verejné API Ryanair. Projekt nie je prepojený so spoločnosťou Ryanair a Ryanair ho nepodporuje. Endpointy sa môžu zmeniť alebo prestať fungovať.

---

## Známe obmedzenia

- **Jedno odletové letisko naraz** - vyhľadávanie prebieha vždy z jedného zvoleného letiska.
- **Hlavné vyhľadávanie je jednosmerné** - spiatočné lety sa riešia samostatne cez Return modal.
- **Bez používateľských účtov** - uložené lety sú iba v `localStorage` prehliadača a nesynchronizujú sa medzi zariadeniami.
- **Iba Ryanair** - aplikácia momentálne nepokrýva iné letecké spoločnosti.
- **Neoficiálne API** - Ryanair môže endpointy zmeniť, obmedziť alebo dočasne blokovať. Aplikácia preto používa cache, retry logiku a obmedzenie počtu požiadaviek.

---

## Licencia

Projekt slúži na osobné a študijné účely. Nie je oficiálne spojený so spoločnosťou Ryanair DAC a Ryanair ho nijako nepodporuje.

---

## Popis projektu

SocialStrats je viacjazyčná webová aplikácia, ktorá pomáha používateľom nájsť lacné Ryanair lety jednoduchým a vizuálnym spôsobom. Projekt sa zameriava na vyhľadávanie dostupných trás z vybraného odletového letiska, porovnanie cien pre zvolený mesiac a zobrazenie najzaujímavejších možností na živej mape.

Používateľ môže vyhľadávať lety podľa rozpočtu, destinácie, mesiaca a ďalších preferencií. Výsledky nie sú zoradené iba podľa absolútnej ceny, ale aj podľa hodnoty letu, napríklad cez cenu za kilometer. Vďaka tomu sa dá jednoduchšie rozlíšiť, či je let len lacný, alebo je aj skutočne výhodný vzhľadom na vzdialenosť.

Dôležitou súčasťou projektu je AI plánovač výletu. Používateľ nemusí hneď presne vedieť, kam chce letieť. Môže opísať rozpočet, približný typ destinácie, vzdialenosť alebo štýl výletu a asistent mu pomôže nájsť vhodné lety z dostupných výsledkov.

Okrem toho aplikácia obsahuje kalendár cien, vyhľadanie spiatočného letu, uložené lety, CSV export a analytické grafy. Rozhranie podporuje angličtinu, slovenčinu a nemčinu, takže aplikácia je použiteľnejšia aj mimo jedného jazykového prostredia.

Technicky je projekt postavený na ASP.NET Core Razor Pages, C#, JavaScripte, Leaflet.js, Chart.js a OpenAI API. Dáta čerpá z verejných Ryanair endpointov a nepoužíva vlastnú databázu, takže je ľahký, bezstavový a jednoduchý na lokálne spustenie.

---

## Pavol Lukačka

Môj prínos v projekte bol hlavne v prvotnom návrhu aplikácie, vytvorení základného funkčného prototypu a v intenzívnom použití LLM nástrojov počas vývoja. Projekt som začal myšlienkou vytvoriť webovú aplikáciu, ktorá nebude len obyčajným zoznamom lacných leteniek, ale vizuálnym nástrojom na objavovanie lacných letov po Európe. Od začiatku som smeroval riešenie k tomu, aby používateľ videl lety na mape, vedel rýchlo porovnať cenu, vzdialenosť a hodnotu letu a aby sa z projektu dal neskôr rozvíjať širší koncept SocialStrats.

Moja prompt história je uložená v priečinku `Lukacka LLM promt history`, konkrétne v súboroch `rystats-chat-history1.md` až `rystats-chat-history18.md`. Tieto záznamy dokumentujú postupný vývoj od prvého nápadu cez prototyp, napojenie na Ryanair endpointy, riešenie problémov s dátami, návrh používateľského rozhrania, animácie mapy, analytické metriky a lokalizáciu.

### Prvotný návrh a smerovanie produktu

Na začiatku som prišiel s ideou aplikácie v .NET, ktorá by mapovala lacné lety v Európe. Pôvodná myšlienka bola širšia, ale cez LLM diskusiu som ju zúžil na realistické MVP: mapa, výber odletového letiska, dátum alebo mesiac, základné filtrovanie a zobrazenie lacných letov. Dôležité bolo, aby aplikácia mala jasný účel a aby používateľ nemusel prechádzať klasické tabuľkové vyhľadávače letov. Hlavným princípom bolo "map-first" rozhranie, teda najskôr vizuálne pochopiť, kam sa dá lacno letieť.

V ďalších promptoch som rozvíjal aj identitu projektu SocialStrats. Aplikácia mala byť nielen technickým skenerom leteniek, ale aj produktom, ktorý vie pracovať s myšlienkou cestovania, sociálneho objavovania miest a strategického porovnávania destinácií. Preto som riešil texty, hero sekciu, marketingové pomenovanie funkcií a to, aby stránka pôsobila modernejšie a použiteľnejšie pre reálneho používateľa.

### Základný funkčný prototyp

V prvej fáze som cez LLM nástroje vytvoril základnú verziu aplikácie. Najskôr išlo o jednoduchý single-file .NET prototyp s API endpointom, vloženým HTML a mock dátami. Následne som riešenie prepracoval do ASP.NET Core Razor Pages štruktúry s oddelením `Program.cs`, `Index.cshtml` a page modelu. Táto fáza vytvorila základ, z ktorého sa projekt ďalej rozvíjal.

V prototypovej verzii som riešil:

1. návrh základnej architektúry v ASP.NET Core,
2. vytvorenie Razor Pages rozhrania,
3. použitie Leaflet.js mapy,
4. zobrazenie leteckých trás a markerov na mape,
5. základný vstup pre odletové letisko a obdobie,
6. vykresľovanie kariet s výsledkami,
7. prvé rozdelenie backendovej a frontendovej logiky.

Táto časť projektu ešte neobsahovala AI agenta ani plnohodnotné spiatočné lety. Išlo o základnú funkčnú aplikáciu, ktorá dokázala vyhľadávať a vizualizovať lacné lety jedným smerom. Neskoršie tímové rozšírenia, ako AI chatbot a práca so spiatočnými letmi, sú dokumentované v samostatných sekciách ďalších členov tímu.

### Napojenie na Ryanair dáta

Veľká časť môjho prínosu bola v hľadaní spôsobu, ako pracovať s reálnymi dátami. Pôvodne sa uvažovalo aj nad inými zdrojmi a nad Wizz Air API, ale počas testovania sa ukázalo, že niektoré endpointy vracajú chyby, sú limitované alebo nie sú vhodné na stabilné použitie. Preto som projekt nasmeroval na Ryanair verejné endpointy, ktoré bolo možné použiť bez vlastnej databázy a bez plateného API kľúča.

V tejto časti som cez LLM postupne riešil:

- získanie zoznamu Ryanair letísk,
- získanie trás z konkrétneho odletového letiska,
- vyhľadanie najlacnejších cien pre vybraný mesiac,
- prácu s endpointmi typu `routes`, `cheapest` a dostupnosťami,
- doplnenie HTTP hlavičiek pre stabilnejšie volania,
- obmedzenie paralelných requestov kvôli riziku rate limitingu,
- caching odpovedí, aby sa zbytočne nezaťažoval externý endpoint,
- spracovanie prípadov, keď Ryanair JSON nevracia vždy rovnakú štruktúru.

Dôležitým technickým problémom bolo, že Ryanair JSON niekedy vracal hodnoty ako string a inokedy ako objekt. Pôvodný parser preto mohol spadnúť pri `GetString()`. Tento problém som riešil cez LLM analýzu chybového logu a následnú úpravu parsera tak, aby bol tolerantnejší voči rôznym tvarom odpovede.

### Mapové rozhranie a používateľský zážitok

Od začiatku som chcel, aby aplikácia bola postavená okolo mapy. Preto som riešil nielen samotné body letísk, ale aj animované trasy, pohyb lietadla a celkový vizuálny dojem. V prompt histórii je vidieť viacero iterácií, kde som riešil, že lietadlo nebolo správne natočené podľa trasy alebo že pohyb mapy pri sledovaní lietadla pôsobil trhane.

Výsledkom týchto iterácií bol návrh optimalizovanejšieho prístupu k animácii:

- oddelenie pohybu lietadla od náročného neustáleho presúvania Leaflet mapy,
- použitie `requestAnimationFrame`,
- úprava rotácie lietadla podľa smeru letu,
- zníženie trhania pri follow režime,
- návrh GPU-friendly riešenia cez CSS transformácie,
- zachovanie Leaflet mapy bez potreby prechodu na inú mapovú knižnicu.

Okrem mapy som riešil aj lepšie karty letov. Postupne som dopĺňal zobrazenie názvu letiska namiesto samotného IATA kódu, cenu za kilometer, vzdialenosť, dátum letu a neskôr aj návrh na zobrazenie času odletu a príletu, ak je táto informácia dostupná v dátach. Tým sa karta letu posunula z jednoduchého technického výpisu na praktickejší rozhodovací prvok.

### Analytické a porovnávacie prvky

Mojou snahou bolo, aby aplikácia neukazovala iba najlacnejšiu cenu, ale aby pomáhala porovnávať hodnotu letu. Preto som do návrhu zaviedol metriky ako vzdialenosť letu a cena za kilometer. Táto metrika pomáha používateľovi pochopiť, či je let výhodný nielen podľa absolútnej ceny, ale aj podľa toho, akú vzdialenosť za danú cenu získa.

V prompt histórii som riešil aj analytické rozšírenia:

- price histogram,
- scatter graf hodnoty voči vzdialenosti,
- priemerné a mediánové ceny,
- zvýraznenie najvýhodnejších letov,
- textové vysvetlenie, prečo je konkrétny let dobrý deal,
- prepojenie štatistík s mapou a kartami výsledkov.

Tieto prvky posúvajú projekt bližšie k zadaniu funkčného softvérového riešenia, pretože aplikácia nie je len UI nad API. Pridáva vlastnú interpretačnú vrstvu nad dátami.

### Lokalizácia a produktová komunikácia

Ďalšia časť môjho prínosu bola v návrhu viacjazyčnosti a lepšej používateľskej komunikácie. V prompt histórii som riešil rozdelenie stránky podľa kultúry, použitie `SetLanguage` endpointu, cookie pre uloženie výberu jazyka, query string prepínanie a suffix Razor views pre jazykové verzie.

Zároveň som cez LLM iteroval texty v aplikácii tak, aby boli zrozumiteľnejšie a viac orientované na používateľa. Riešil som napríklad, ako lepšie vysvetliť map-first prístup, ako pomenovať výhody aplikácie, ako zdôrazniť zdieľanie a ako priznať limitáciu, že základná verzia pracovala primárne s Ryanair dátami.

### Použitie LLM nástrojov v mojej časti

LLM nástroje som nepoužíval iba na jednorazové vygenerovanie kódu. Používal som ich ako vývojového partnera počas celého návrhu a implementácie. V prompt histórii je vidieť, že som postupoval iteratívne: najskôr som zadal produktový cieľ, potom som zužoval rozsah, riešil chyby, pýtal si celé kopírovateľné bloky kódu, analyzoval runtime chyby, upravoval dizajn a neskôr dopĺňal dokumentáciu.

LLM som využil hlavne na:

- návrh architektúry aplikácie,
- generovanie prvého prototypu,
- refaktoring na Razor Pages,
- návrh backend endpointov,
- hľadanie vhodného zdroja dát,
- analýzu problémov s Ryanair endpointmi,
- návrh robustnejšieho JSON parsovania,
- úpravu frontendového rozhrania,
- zlepšenie animácií v Leaflet mape,
- návrh analytických komponentov,
- návrh lokalizačného mechanizmu,
- tvorbu vývojových poznámok a README častí.

Najväčší prínos LLM bol v rýchlosti iterovania. Vedel som rýchlo porovnať viac možností, nechať si vysvetliť chybu, dostať návrh opravy a následne ho prispôsobiť projektu. Zároveň sa ukázali aj limity: niektoré navrhnuté API endpointy neboli stabilné, časť kódu bolo potrebné overovať ručne a pri externých službách bolo nutné počítať s rate limitingom, zmenou formátu dát a neoficiálnym charakterom Ryanair API.

### Zhrnutie môjho prínosu

Môj prínos možno zhrnúť tak, že som vytvoril základnú funkčnú kostru aplikácie a určil jej produktový smer. Priniesol som myšlienku mapového vyhľadávania lacných letov, navrhol prvé MVP, vytvoril základný .NET/Razor prototyp, napojil aplikáciu na Ryanair dáta, riešil nefunkčné API volania, rozvíjal mapové UI, doplnil hodnotové metriky a pracoval na lepšej používateľskej prezentácii aplikácie.

Základná verzia, ktorú som budoval, bola funkčná aplikácia na vyhľadávanie a vizualizáciu jednosmerných Ryanair letov. Neobsahovala ešte AI agenta ani finálne riešenie spiatočných letov. Tieto rozšírenia vznikli v ďalších tímových častiach projektu a sú popísané v samostatných sekciách README. Moja časť je dôležitá najmä preto, že vytvorila jadro aplikácie, na ktoré mohli nadviazať ďalší členovia tímu.

---

## Stanislav Olbert

Ja som do toho projectu pridal možnosť ukaldať si obľúbené lety, prehľad v kalendáry pod každým letom a možnosť hľadania spätného letu.

Pracoval som s claude AI priamo v PowerShell windows a v chat_history_2026-05-01 súbore je moja história chatu práce s ním.

Pridal som tieto veci

  1. Obľúbené lety (Saved / Favorite Trips)                                                                             
  - Tlačidlo ❤ na každej karte letu — kliknutím uložíš/odložíš let                                                        - Záložka "❤️ Saved" v pravom paneli so zoznamom uložených letov
  - Uloženie do localStorage (zostane aj po obnovení stránky)
  - Tlačidlá "Clear all" a "Export CSV" v záložke obľúbených

  ---
  2. Modálne okno — Spiatočný let (Return Flight Modal)

  - Tlačidlo "↩️ Return" na každej karte letu (namiesto starého odznaku s cenou)
  - Po kliknutí sa otvorí popup s:
    - Informáciami o odchodovom lete
    - Nastavením počtu dní (tlačidlá − / + alebo priamy vstup) — kedy chceš letieť späť
    - Presnou cenou spiatočného letu na vypočítaný dátum
    - Celkovou cenou tam + späť
    - Farebným kalendárom celého mesiaca (zelená = lacno, červená = draho) — kliknutím na deň sa automaticky aktualizuje
   výber

  ---
  3. Kaledár cien (Fare Calendar)

  - Tlačidlo "📅 Calendar" na každej karte letu
  - Otvorí popup s mriežkou celého mesiaca pre daný spoj
  - Bunky sú farebne označené podľa ceny (zelená → žltá → oranžová → červená)
  - Navigácia šípkami medzi mesiacmi

  ---
  4. Backend endpoint /api/ryanair/calendar

  - Nový API endpoint v Program.cs
  - Vracia všetky denné ceny pre konkrétny spoj a mesiac
  - Cachovaný 20 minút
  - Využíva ho aj kaledár aj modálne okno spiatočného letu

  ---
  5. README dokumentácia

  - Nový súbor SocialStrats/README.md s kompletnou dokumentáciou projektu (popis, API endpointy, inštalácia,
  konfigurácia, atď.)

---

## Serhi Vielkin

---

V adresári RYSTATS2V\SocialStrats\SocialStrats sú dve súbory ktoré dokumentuju môj prínos AI chatbot (AGENT_CHATBOT_DOCUMENTATION, AI_AGENT_HANDOFF_SK).

---

## Ako projekt spĺňa zadanie

Táto časť zhŕňa, ako projekt SocialStrats napĺňa požiadavky zadania k funkčnému softvérovému riešeniu s intenzívnym využitím LLM nástrojov.

| Požiadavka zadania | Splnenie v projekte |
|---|---|
| Funkčné softvérové riešenie | Projekt obsahuje spustiteľnú ASP.NET Core aplikáciu na vyhľadávanie, filtrovanie a vizualizáciu lacných Ryanair letov. Používateľ si vyberá odletové letisko, mesiac, rozpočet a výsledky vidí na mape aj v kartách. |
| Backendová časť | Backend je realizovaný v `Program.cs` pomocou Minimal API endpointov. Rieši získavanie letísk, trás, cien, kalendárových dát, cacheovanie, rate limiting a komunikáciu s externým Ryanair API. |
| Frontendová časť | Frontend je postavený na ASP.NET Core Razor Pages. Používa Leaflet.js na mapu, Chart.js na analytické grafy a vlastný JavaScript na filtrovanie, animácie, uložené lety, exporty a prácu s výsledkami. |
| Jasný účel aplikácie | Aplikácia rieši konkrétny prípad použitia: rýchle nájdenie výhodných Ryanair letov z vybraného odletového letiska a ich porovnanie podľa ceny, vzdialenosti a hodnoty za kilometer. |
| Git repozitár a štruktúra | Projekt je odovzdaný ako Git repozitár so zdrojovým kódom, README dokumentáciou a samostatnými priečinkami s prompt históriou. Zdrojový kód aplikácie je uložený v adresári `SocialStrats`. |
| Tímová spolupráca | README obsahuje samostatné sekcie pre členov tímu a ich prínos. Pavol Lukačka dokumentuje návrh a základ aplikácie, Stanislav Olbert dokumentuje saved trips, fare calendar a return flight funkcionalitu, Serhii Vielkin dokumentuje AI chatbota. |
| Použitie LLM pri vývoji | LLM nástroje boli použité pri návrhu architektúry, generovaní prvého prototypu, refaktoringu, debugovaní, návrhu UI, dokumentovaní, tvorbe prompt history a pri implementácii AI chatbota. |
| LLM ako súčasť riešenia | Súčasťou výsledného projektu je AI trip planner napojený na OpenAI API. Chatbot pomáha používateľovi vybrať vhodné lety podľa rozpočtu, vzdialenosti a preferencií. |
| Dokumentácia | README obsahuje popis projektu, technológie, API endpointy, návod na spustenie, obmedzenia, caching, lokalizáciu a individuálne prínosy členov tímu. Ďalšie dokumenty k AI agentovi sú v `AGENT_CHATBOT_DOCUMENTATION` a `AI_AGENT_HANDOFF_SK`. |
| Reflexia LLM nástrojov | Projekt obsahuje prompt history priečinky a textové zhodnotenie toho, kde LLM pomohli, kde mali limity a čo bolo potrebné overovať ručne. |

Z pohľadu zadania projekt spĺňa jadro požiadavky: ide o funkčné softvérové riešenie s backendom, frontendovou aplikáciou, mapovou vizualizáciou, externým dátovým zdrojom, analytickými prvkami a vedomým využitím LLM nástrojov počas vývoja aj priamo vo výslednej funkcionalite.

---

## Spoločná reflexia využitia LLM nástrojov a limitov

LLM nástroje boli v projekte použité intenzívne a nie iba formálne. Pomohli pri prvotnom návrhu aplikácie, pri rýchlom vytvorení prototypu, pri úprave architektúry, pri písaní backendových endpointov, pri návrhu frontendového rozhrania, pri debugovaní chýb, pri tvorbe dokumentácie a pri implementácii AI chatbota. Vďaka nim bolo možné rýchlo porovnávať viac riešení, iterovať dizajn a vysvetľovať chyby v kóde.

Zároveň sa počas práce ukázalo, že LLM nástroje nevedia nahradiť overovanie funkčnosti. Viackrát navrhli endpointy alebo technické riešenia, ktoré vyzerali správne, ale v praxi neboli stabilné alebo nefungovali podľa očakávania. Pri externých API bolo potrebné kontrolovať reálne odpovede, spracovať chybové stavy a upravovať parsery podľa skutočného JSON formátu.

Najdôležitejšie limity, ktoré sme počas projektu pozorovali:

- Niektoré LLM návrhy pracovali s API endpointmi, ktoré neboli stabilné alebo neboli vhodné na dlhodobé použitie.
- Ryanair API je neoficiálne a projekt s ním nie je nijako formálne prepojený. Endpointy sa môžu zmeniť, môžu byť limitované alebo dočasne nedostupné.
- Niektoré časti kódu bolo potrebné ručne otestovať, pretože LLM síce vedelo navrhnúť riešenie, ale nevedelo garantovať správanie reálnej služby.
- Pri používateľskom rozhraní bolo potrebných viac iterácií. Prvé návrhy neboli vždy dostatočne responzívne, prehľadné alebo použiteľné na mobile.
- Pri mapových animáciách sa ukázalo, že všeobecný návrh nestačí. Bolo potrebné riešiť konkrétne správanie Leaflet mapy, výkon prehliadača a plynulosť animácie.
- AI zrýchlila prototypovanie a dokumentáciu, ale nenahradila testovanie, kontrolu dát, čítanie chybových hlášok a finálne rozhodovanie vývojára.

Najväčším prínosom LLM bolo zrýchlenie vývojového cyklu. Namiesto toho, aby sa každá časť riešila od nuly, sme mohli cez promptovanie rýchlo vytvoriť prvú verziu, následne ju kriticky skontrolovať, upraviť a prispôsobiť reálnemu projektu. Tento spôsob práce sa ukázal ako veľmi užitočný hlavne pri prototype, refaktoringu, vysvetľovaní chýb a písaní dokumentácie.

Z pohľadu skúsenosti tímu bolo dôležité pochopiť, že dobré používanie LLM nie je iba zadanie krátkeho príkazu. Kvalitný výsledok vznikal najmä vtedy, keď sme vedeli presne opísať problém, priložiť chybové hlášky, ukázať existujúci kód, pomenovať obmedzenia a následne výstup LLM kriticky overiť. Práve táto iteratívna práca bola jedným z hlavných prínosov projektu.

---
