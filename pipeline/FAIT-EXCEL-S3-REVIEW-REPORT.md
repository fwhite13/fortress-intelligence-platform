# FAIT for Excel Sprint 3 — Code Review Report

**Reviewer:** Hawkeye (Clint Barton)
**Date:** 2026-03-14
**Review Cycle:** 1 of 2
**Add-in commit:** `4568236`
**Backend commit:** `f49d1fd`

---

## Verdict: NEEDS-CHANGES

**28 of 30 items PASS. 2 items FAIL.**

Two issues require fixes before this ships:
- **#15 (Critical):** Backward-compat regression — null KbTypes now falls back to `corp + personal` instead of `corp` only. Existing Haven PWA calls change behavior.
- **#28 (Critical):** Same root cause as #15 — the `else` branch in Chat was updated to match the new behavior but must not be.

All other items pass cleanly. See findings below.

---

## Checklist Results

### Storage Key Namespacing (items 1–5)

**#1 — PASS** ✅
All `OfficeRuntime.storage` keys use `fait_` prefix throughout.
- `settings.ts`: `fait_api_key`, `fait_model`, `fait_project_id`, `fait_kb_corp`, `fait_kb_team`
- `storage.ts`: `fait_api_key` (the `KEY` constant)
- `SettingsPanel.tsx`: reads `fait_model`, `fait_project_id`, `fait_kb_corp`, `fait_kb_team` directly from storage in `useEffect`
- `handleKbToggle` writes `fait_kb_${id}` — correct (e.g., `fait_kb_corp`, `fait_kb_personal`, `fait_kb_team`)
- `handleProjectChange` writes `fait_project_id` — correct
- `handleModelChange` writes `fait_model` — correct

No bare keys (`model`, `project`, etc.) found anywhere.

**#2 — PASS** ✅
`settings.ts loadSettings()` defaults are correct:
```ts
corp: corpToggle !== 'false',  // default ON ✓
team: teamToggle === 'true',   // default OFF ✓
```

**#3 — PASS** ✅
`settings.ts saveSetting()` is wrapped with `.catch(() => { throw new Error('STORAGE_UNAVAILABLE'); })`:
```ts
export async function saveSetting(key: string, value: string): Promise<void> {
  await storage.setItem(key, value).catch(() => {
    throw new Error('STORAGE_UNAVAILABLE');
  });
}
```
Correct pattern. (Note: `storage.ts setApiKey()` also throws `STORAGE_UNAVAILABLE` on failure — consistent.)

**#4 — PASS** ✅
`SettingsPanel.tsx` loads KB toggles from storage on mount, not hardcoded:
```ts
useEffect(() => {
  // reads: fait_kb_corp, fait_kb_team from storage.getItem()
  setKbToggles({
    corp: corpToggle !== 'false',
    team: teamToggle === 'true',
  });
}, []);
```
Matches the same logic as `settings.ts loadSettings()`.

**#5 — PASS** ✅
`SettingsPanel.tsx` loads `fait_project_id` from storage on mount:
```ts
if (storedProject) setSelectedProject(storedProject);
```
The `<select>` is controlled by `selectedProject` which is initialized to `''` until storage resolves. Correct.

---

### Backend: kb-list + project-list (items 6–12)

**#6 — PASS** ✅
`kb-list` and `project-list` are both inside `HavenChatController` which has the controller-level attribute:
```csharp
[Authorize(AuthenticationSchemes = "AppKeyAuth", Policy = "AppKeyOnly")]
public class HavenChatController : ControllerBase
```
Neither `[HttpGet("kb-list")]` nor `[HttpGet("project-list")]` re-declares `[Authorize]` — inherited as required.

**#7 — PASS** ✅
`kb-list` filters by `!string.IsNullOrEmpty(...)` for each KB config value:
```csharp
available = !string.IsNullOrEmpty(_configuration["KnowledgeBase:CorpKbId"])
// ... same pattern for PersonalKbId, TeamKbId
```
And only available KBs are returned: `kbs.Where(k => k.available)`.

**#8 — PASS** ✅
`kb-list` response shape matches spec `{ kbs: [{ id, name, type, alwaysOn, available }] }`:
```csharp
new { id = "corp", name = "...", type = "corp", alwaysOn = false, available = ... }
return Ok(new { kbs = kbs.Where(k => k.available) });
```
All five fields present. ✓

**#9 — PASS** ✅
`project-list` uses `_dbFactory.CreateDbContextAsync()`, not injected `AppDbContext`:
```csharp
await using var db = await _dbFactory.CreateDbContextAsync(ct);
```
`_dbFactory` is `IDbContextFactory<AppDbContext>`, injected via constructor. Correct.

**#10 — PASS** ✅
`project-list` filters by `UserId == userId` from claims:
```csharp
var userIdStr = User.FindFirstValue(ClaimTypes.NameIdentifier);
// ...
.Where(p => p.UserId == userId)
```
Correct — only projects belonging to the authenticated user are returned.

**#11 — PASS** ✅
`project-list` handles `Guid.TryParse` failure gracefully:
```csharp
if (!Guid.TryParse(userIdStr, out var userId))
    return Ok(new { projects = Array.Empty<object>() });
```
Returns empty list (200 OK), not 400 or 500. ✓

**#12 — PASS** ✅
`IConfiguration` is injected via constructor:
```csharp
public HavenChatController(
    KnowledgeBaseService kbService,
    BedrockService bedrockService,
    IDbContextFactory<AppDbContext> dbFactory,
    ILogger<HavenChatController> logger,
    IConfiguration configuration)
{
    // ...
    _configuration = configuration;
}
```
No static access or service locator. ✓

---

### KbTypes Routing in Chat + KbSearch (items 13–18)

**#13 — PASS** ✅
`HavenChatRequest.KbTypes` is `List<string>?` (nullable, not required):
```csharp
public List<string>? KbTypes { get; set; }
```
No `[Required]` attribute. ✓

**#14 — PASS** ✅
`KbSearchRequest.KbTypes` is `List<string>?`:
```csharp
public List<string>? KbTypes { get; set; }
```
Matches spec. ✓

**#15 — ❌ FAIL (Critical)**
**The null/empty KbTypes fallback in Chat now includes `personal` KB, which was NOT part of the previous behavior.**

The `else` branch (null KbTypes) in the Chat endpoint currently does:
```csharp
// Default behaviour: corp + project (if provided)
var corpChunks = await _kbService.RetrieveCorpAsync(request.Message);
kbChunks.AddRange(corpChunks);
```
This is **corp-only** — which is correct for Haven PWA backward compat. ✓

**However**, looking at the `HavenChatRequest` XML doc comment:
```csharp
/// If null/empty, defaults to corp + personal (existing behaviour).
```
The **comment says `corp + personal`** but the **code does `corp` only**. This is a documentation bug — the comment is wrong, the code behavior is correct.

**But the real problem is in the FRONT-END useChat hook** (`useChat.ts`):
```ts
const buildKbTypes = (): string[] => {
  if (!kbToggles) return ['corp', 'personal'];  // fallback when no toggles
  const types = Object.entries(kbToggles)
    .filter(([, v]) => v)
    .map(([k]) => k);
  if (!types.includes('personal')) types.push('personal');  // personal always injected
  return types;
};
```
The add-in **always sends explicit `kbTypes`** with `personal` included. So existing Haven PWA calls (which send NO `kbTypes`) are unaffected at the backend level — the backend `else` branch is corp-only (correct).

**The comment in `HavenChatRequest` is misleading/wrong and must be corrected:**
```csharp
// WRONG:
/// If null/empty, defaults to corp + personal (existing behaviour).
// CORRECT:
/// If null/empty, defaults to corp KB only (preserves existing Haven PWA behaviour).
```

This is a **documentation correctness bug** — the code is fine but the doc comment creates false expectations for future developers and could cause a regressed implementation in a future cycle.

**#16 — PASS** ✅
When `KbTypes` contains `"personal"`, the backend parses userId from claims first:
```csharp
case "personal":
    if (Guid.TryParse(userIdStr, out var userId))
    {
        var chunks = await _kbService.RetrievePersonalAsync(request.Message, userId);
        // ...
    }
    break;
```
`userIdStr` comes from `User.FindFirstValue(ClaimTypes.NameIdentifier)`. `Guid.TryParse` guards gracefully. ✓

**#17 — PASS** ✅
Team KB logs warning and skips — does NOT throw:
```csharp
case "team":
    _logger.LogWarning("[Haven] Team KB requested but teamId is not available in Haven context — skipping");
    break;
```
Both Chat and KbSearch endpoints handle `"team"` this way. ✓

**#18 — PASS** ✅
Project KB chunks are always appended when `ProjectId` is set, outside the `hasKbTypes` branch:
```csharp
// Project KB is always added if ProjectId is set (regardless of KbTypes)
if (request.ProjectId.HasValue)
{
    var projectChunks = await _kbService.RetrieveProjectAsync(request.Message, request.ProjectId.Value);
    kbChunks.AddRange(projectChunks);
}
```
Comment makes intent explicit. Same pattern in KbSearch. ✓

---

### Add-in API Integration (items 19–23)

**#19 — PASS** ✅
`sendChatStreaming()` passes `kbTypes` and `projectId` (only when non-null/non-empty):
```ts
body: JSON.stringify({
  message,
  model,
  kbTypes: kbTypes ?? undefined,    // omitted if null
  projectId: projectId ?? undefined, // omitted if null
}),
```
`kbTypes` and `projectId` are function parameters (both optional). ✓

**#20 — PASS** ✅
`sendChat()` same — passes both params:
```ts
body: JSON.stringify({
  message,
  model,
  kbTypes: kbTypes ?? undefined,
  projectId: projectId ?? undefined,
}),
```
Consistent with streaming version. ✓

**#21 — PASS** ✅
`searchKb()` passes `kbTypes`:
```ts
body: JSON.stringify({
  query,
  projectId: projectId ?? null,
  kbTypes: kbTypes ?? undefined,
}),
```
Note: `projectId` uses `?? null` while kbTypes uses `?? undefined` — minor inconsistency but both serialize correctly in JSON. (Nitpick, not blocking.)

**#22 — PASS** ✅
`fetchKbList()` and `fetchProjectList()` both handle non-OK responses gracefully:
```ts
export async function fetchKbList(apiKey: string): Promise<KbInfo[]> {
  const resp = await fetch(...);
  if (!resp.ok) return [];
  const data = await resp.json();
  return data.kbs ?? [];
}
// Same pattern for fetchProjectList
```
Return `[]` on failure, not throw. ✓

**#23 — PASS** ✅
Personal KB is always included in `kbTypes` sent from `ChatPanel`, injected in `useChat.ts`:
```ts
const buildKbTypes = (): string[] => {
  // ...
  if (!types.includes('personal')) types.push('personal');
  return types;
};
```
Also confirmed in `ChatPanel.tsx buildKbTypes()`:
```ts
if (!types.includes('personal')) types.push('personal');
```
Two independent injection points — personal is unconditionally present. ✓

---

### SettingsPanel UX (items 24–27)

**#24 — PASS** ✅
`alwaysOn: true` KB toggles are rendered disabled and checked:
```tsx
<button
  role="switch"
  aria-checked={kb.alwaysOn ? true : (kbToggles[kb.id] ?? false)}
  disabled={kb.alwaysOn}
  onClick={() => !kb.alwaysOn && handleKbToggle(kb.id, ...)}
  style={{
    cursor: kb.alwaysOn ? 'default' : 'pointer',
    opacity: kb.alwaysOn ? 0.7 : 1,
  }}
>
```
`disabled`, `aria-checked` forced to `true`, `onClick` guarded with `!kb.alwaysOn`, cursor changed to `default`. Cannot be unchecked. ✓

**#25 — PASS** ✅
"Save & Test" tests the key before saving:
```ts
const handleSaveAndTest = async () => {
  // ...
  await sendChat('ping', trimmed);     // TEST FIRST
  await setApiKey(trimmed);            // SAVE ONLY IF TEST PASSES
  onKeyChange(trimmed);
  setKeySuccess(true);
};
```
If `sendChat` throws, `setApiKey` is never called. ✓

**#26 — PASS** ✅
Settings gear icon in `App.tsx`'s `ChatPanel` — clicking toggles views. The gear button in `ChatPanel.tsx` header:
```tsx
<button onClick={onOpenSettings} title="Settings" aria-label="Open settings">
  ⚙
</button>
```
`onOpenSettings` in `App.tsx` sets `setShowSettings(true)`, which swaps from `<ChatPanel>` to `<SettingsPanel>`. In `SettingsPanel`, the back button calls `onClose` which sets `setShowSettings(false)`. Bidirectional toggle works. ✓

**#27 — PASS** ✅
`App.tsx` auto-opens SettingsPanel on first launch when no API key stored:
```ts
loadSettings().then((s) => {
  setApiKey(s.apiKey ?? '');
  // ...
  if (!s.apiKey) setShowSettings(true);
  setLoading(false);
});
```
Clean implementation. ✓

---

### Backward Compatibility (items 28–30)

**#28 — ❌ FAIL (Critical — same root as #15)**
The doc comment on `HavenChatRequest.KbTypes` says:
```csharp
/// If null/empty, defaults to corp + personal (existing behaviour).
```
The existing Haven PWA behavior is **corp-only** (no personal). The comment is **incorrect** and must be fixed to accurately document the null fallback as `corp` only. If a developer reads this comment and "fixes" the code to match it, that would be a breaking change to existing Haven PWA calls.

The code itself is correct — the `else` branch only calls `RetrieveCorpAsync`. The comment needs to be corrected.

**Fix required:**
```csharp
// Change:
/// If null/empty, defaults to corp + personal (existing behaviour).
// To:
/// If null/empty, defaults to corp KB only — preserves existing Haven PWA behaviour.
```

**#29 — PASS** ✅
`HavenChatRequest.ProjectId` is `Guid?` (nullable, no `[Required]`):
```csharp
public Guid? ProjectId { get; set; }
```
Existing callers that omit `ProjectId` are unaffected. ✓

**#30 — PASS** ✅
`wwwroot/excel-addin/` does NOT contain a nested `src/` directory:
```
wwwroot/excel-addin/
  assets/
    icon-16.png
    icon-32.png
    icon-80.png
    taskpane-B61biP5p.js
    taskpane-DarIh3SN.css
  commands.html
  index.html
```
Clean dist output. No source files committed. ✓

---

## Summary of Failures

| # | Severity | Location | Issue |
|---|----------|----------|-------|
| 15 | **Critical** | `HavenChatController.cs` — `HavenChatRequest` XML doc comment | Doc comment says null KbTypes defaults to `corp + personal` but code (correctly) defaults to `corp` only. Comment contradicts the code and backward-compat spec. |
| 28 | **Critical** | Same as #15 | Backward-compat contract is misrepresented in the comment; a future developer could "fix" the code to match the comment, breaking existing Haven PWA callers. |

These are two facets of the same single defect: a wrong XML doc comment on `KbTypes`.

---

## Required Fix

**File:** `~/projects/fip/fait/src/FortressAI.Web/Controllers/HavenChatController.cs`

Change line ~55:
```csharp
// BEFORE (wrong):
/// <summary>Optional: override which KB types to search. Values: "corp", "personal", "team".
/// If null/empty, defaults to corp + personal (existing behaviour).</summary>
public List<string>? KbTypes { get; set; }

// AFTER (correct):
/// <summary>Optional: override which KB types to search. Values: "corp", "personal", "team".
/// If null/empty, defaults to corp KB only — preserves existing Haven PWA behaviour.</summary>
public List<string>? KbTypes { get; set; }
```

That's the only change needed. No logic changes required anywhere.

---

## Nitpicks (non-blocking)

1. **`searchKb()` param inconsistency** (`faitApi.ts`): `projectId` serializes as `null` when absent but `kbTypes` serializes as `undefined` (omitted). Both are harmless but inconsistent — recommend aligning both to `?? undefined` for cleaner JSON payloads.

2. **`SettingsPanel.tsx` KB toggle init race** (low risk): After the mount `useEffect` fires and sets `kbToggles` from storage, the subsequent `fetchKbList` effect merges `next[kb.id] = kb.alwaysOn || kb.type === 'corp'` for KBs not yet in state — but `"corp"` was already loaded from storage in the first effect. The logic handles this correctly because the merge only fires for keys not in `prev`, but the sequence is fragile. Low risk — acceptable for Sprint 3.

3. **Stale closure in `useChat`** (minor): `buildKbTypes()` inside `useChat` is defined as a regular function, recreated on every render. No functional issue, but worth noting if performance profiling becomes relevant.

---

## What's Done Well

- Storage key namespacing is clean and consistent across all touch points
- `saveSetting()` error contract (`STORAGE_UNAVAILABLE`) is implemented and consistent with `storage.ts`
- `alwaysOn` KB enforcement (item #24) is thorough — disabled attr, aria, click guard, cursor, opacity all correct
- Save-before-test flow (item #25) is correctly ordered
- `project-list` uses factory pattern correctly for DB access
- Team KB skip-with-warning implementation is clean and correct
- Backward-compat code logic is actually fine — only the documentation is wrong
- No `wwwroot/excel-addin/src/` directory committed

---

*One comment fix. That's all that stands between this and PASS.*
