# Build Report — ADO#3115

## What was built
Wired `FaitV2:SharedSecret` through FIRM so the `/api/assistant/inject` endpoint in fait-v2 authenticates correctly. Created `FaitV2IntegrationService` for programmatic use and fixed the existing `MeetingDetail.razor` inject call to use the correct config key.

---

## Files changed

### FIRM (`/home/fredw/projects/fip/firm/`)

| File | Change |
|------|--------|
| `src/FortressIntelligenceRM.Web/Services/FaitV2IntegrationService.cs` | **New** — `IFaitV2IntegrationService` + `FaitV2IntegrationService`. Posts to `/api/assistant/inject` with `X-Firm-Secret` header sourced from `FaitV2:SharedSecret`. Endpoint URL sourced from `FaitV2:BaseUrl`. Fails gracefully (logs warning, skips) if either config key is missing. |
| `src/FortressIntelligenceRM.Web/Program.cs` | Added `builder.Services.AddHttpClient("FaitV2Client")` + `builder.Services.AddScoped<IFaitV2IntegrationService, FaitV2IntegrationService>()` |
| `src/FortressIntelligenceRM.Web/appsettings.json` | Added `"SharedSecret": ""` placeholder to existing `FaitV2` section |
| `src/FortressIntelligenceRM.Web/Components/Pages/MeetingDetail.razor` | **Bug fix** — changed `Configuration["FirmIntegration:SharedSecret"]` → `Configuration["FaitV2:SharedSecret"]`. The former key doesn't exist in FIRM config; the latter is the correct key for the fait-v2 shared secret. |

### fait-v2 (`/home/fredw/projects/fip/fait-v2/`)
No changes needed — `/api/assistant/inject` already correctly validates `X-Firm-Secret` against `FirmIntegration:SharedSecret`. Verified at Program.cs line ~486.

---

## Commits
- `11fde596` — `feat(firm#3115): add FaitV2IntegrationService + SharedSecret wiring` *(also included out-of-scope changes from CC — see note below)*
- `219b6208` — `fix(firm#3115): use FaitV2:SharedSecret for inject header in MeetingDetail` *(clean, single-file)*

---

## Build verification
- **FIRM:** `dotnet build` — **0 errors**, 20 pre-existing warnings
- **fait-v2:** `dotnet build` — **0 errors**, pre-existing warnings only

---

## Acceptance criteria verification
- [x] `FaitV2:SharedSecret` config key added to FIRM `appsettings.json` — verified
- [x] `FaitV2IntegrationService` created with `IFaitV2IntegrationService` interface — verified
- [x] Registered in FIRM `Program.cs` with named `FaitV2Client` HTTP client — verified
- [x] `MeetingDetail.razor` inject call now uses `FaitV2:SharedSecret` (not `FirmIntegration:SharedSecret`) — verified in commit 219b6208
- [x] fait-v2 inject endpoint validates against `FirmIntegration:SharedSecret` — pre-existing, verified at Program.cs ~486
- [x] Both builds pass 0 errors — verified

---

## ⚠️ Note for Clint: Out-of-scope additions in commit 11fde596
CC bundled the following out-of-scope changes in the first commit (these were staged in the working tree):
- `fait-v2/agent-harness/harness-server.js` — preference detection fire-and-forget (ADO#3093 scope)
- `fait-v2/src/FortressAI.V2.Web/Components/Chat/ChatView.razor` — file upload destination selector UI (ADO#3094 scope)
- `fait-v2/src/FortressAI.V2.Web/Program.cs` — `MemoryWriteRequest` record + `/api/memory/write` endpoint (ADO#3093/3094 scope)

These additions were from in-progress working tree files that CC committed as part of this session. They build clean (0 errors). However, Clint should be aware these are not ADO#3115 scope and should review them accordingly if they haven't been reviewed as part of their respective WIs.

---

## ECS task def action still needed (not part of this code build)
The actual secret value must be set in the firm-web ECS task def:
- `FaitV2__SharedSecret` = `ceb37c318147a2e29f52d018f813546af9bb3262a6cfaf089b91c0a163e61fe6`
- `FaitV2__BaseUrl` = `https://app.fortressam.ai` (if updating from dev URL)

This is an infra/ops action, not part of this code commit.

---

## How to test
1. Deploy FIRM with `FaitV2__SharedSecret` env var set to the value in fait-v2's `FirmIntegration__SharedSecret` ECS task def
2. Open a meeting in FIRM → MeetingDetail → "Send to Assistant"
3. Confirm 200 response and transcript injected into fait-v2 conversation
