# FAIT for Excel — Sprint 3 Build Report

**Sprint:** 3 — Settings Panel (KB Toggles, Project Selector, Model Picker)
**Builder:** Tony Stark (software-engineer)
**Date:** 2026-03-14
**Base commit (Sprint 2):** `be91445`

---

## Summary

Sprint 3 delivers a full settings panel to the FAIT Excel add-in, allowing users to toggle Knowledge Bases on/off, select an active project, and pick their preferred model — all persisted via `OfficeRuntime.storage`. The FAIT backend gains two new GET endpoints (`kb-list`, `project-list`) and `KbTypes` override support on both chat and KB-search.

---

## Add-in Files Changed/Added

| File | Change |
|------|--------|
| `src/taskpane/services/settings.ts` | **NEW** — `FaitSettings` type, `loadSettings()`, `saveSetting()` typed wrappers over `OfficeRuntime.storage` |
| `src/taskpane/services/faitApi.ts` | **UPDATED** — `sendChat` / `sendChatStreaming` / `searchKb` accept `kbTypes` + `projectId`; added `fetchKbList()`, `fetchProjectList()`, `KbInfo`, `ProjectInfo` interfaces |
| `src/taskpane/components/SettingsPanel.tsx` | **REPLACED** — full 4-section settings panel (API Key, Knowledge Bases, Active Project, Model); loads/persists all values via storage; FAIT navy/gold branding |
| `src/taskpane/components/ChatPanel.tsx` | **UPDATED** — accepts `model`, `kbToggles`, `projectId` props; removes inline `ModelPicker`; shows model as read-only header text with gear link; builds `kbTypes` array and passes to API; personal KB always included |
| `src/taskpane/hooks/useChat.ts` | **UPDATED** — accepts `kbToggles` + `projectId`, builds `kbTypes` array, passes to both `sendChatStreaming` and `sendChat` fallback |
| `src/taskpane/App.tsx` | **UPDATED** — uses `loadSettings()` on init to hydrate all state; passes `model`, `kbToggles`, `projectId` down to `ChatPanel`; passes `apiKey` + `onKeyChange` to `SettingsPanel`; no-API-key auto-opens settings |

---

## Backend Changes

### File: `fait/src/FortressAI.Web/Controllers/HavenChatController.cs`

#### New endpoints
| Endpoint | Method | Description |
|----------|--------|-------------|
| `/api/haven/kb-list` | GET | Returns configured KB types (`corp`, `personal`, `team`) filtered to those with IDs in config. Each item: `id`, `name`, `type`, `alwaysOn`, `available`. |
| `/api/haven/project-list` | GET | Returns `{ id, name }` list of projects owned by the authenticated user, ordered by name. |

#### Request model changes
| Type | Field Added | Notes |
|------|-------------|-------|
| `HavenChatRequest` | `KbTypes: List<string>?` | Values: `"corp"`, `"personal"`, `"team"`. Null/empty → existing behaviour (corp + project). |
| `KbSearchRequest` | `KbTypes: List<string>?` | Same semantics as above. |

#### KB retrieval logic (Chat + KbSearch)
- If `KbTypes` null/empty → existing default (corp KB + project if `ProjectId` set)
- If `KbTypes` specified → search only listed types:
  - `corp` → `_kbService.RetrieveCorpAsync()`
  - `personal` → `_kbService.RetrievePersonalAsync(query, userId)`
  - `team` → skipped with warning log (teamId not available in Haven context)
- `ProjectId` always adds project KB chunks regardless of `KbTypes`

#### Constructor change
- `IConfiguration _configuration` injected (for `kb-list` endpoint config checks)

---

## Build Results

### npm (Add-in TypeScript)
```
✓ tsc — 0 TypeScript errors
✓ vite build — 38 modules, built in 89ms
dist/assets/taskpane-B61biP5p.js   226.21 kB │ gzip: 69.78 kB
dist/assets/taskpane-DarIh3SN.css    0.75 kB │ gzip:  0.43 kB
```

### dotnet (FAIT Backend)
```
29 Warning(s)  — pre-existing MudBlazor MUD0002 warnings, unrelated to Sprint 3
0 Error(s)
Time Elapsed: 00:00:04.96
```

---

## Commit SHAs

| Repo | Commit | Message |
|------|--------|---------|
| `fait-for-excel` | `4568236` | `feat: Sprint 3 — settings panel (KB toggles, project selector, model picker)` |
| `fip` (monorepo) | `f49d1fd` | `feat(haven): kb-list + project-list endpoints; KbTypes param for chat + kb-search; Sprint 3 addin dist` |

Monorepo pushed to `origin/main`.

---

## Self-Review Checklist

- [x] `KbTypes` added to `HavenChatRequest` and `KbSearchRequest`
- [x] KB retrieval logic respects `KbTypes` in both `Chat` and `KbSearch`
- [x] `Chat` method signature kept as `Task` (not `Task<IActionResult>`)
- [x] `IConfiguration` injected into controller constructor
- [x] `GET /api/haven/kb-list` returns only configured KBs
- [x] `GET /api/haven/project-list` queries `db.Projects.Where(p => p.UserId == userId)`
- [x] `SettingsPanel` has all 4 sections: API Key, Knowledge Bases, Active Project, Model
- [x] `alwaysOn` KBs render disabled toggle always on
- [x] All `OfficeRuntime.storage` keys use `fait_` prefix
- [x] `loadSettings()` hydrates all state on app init
- [x] `ChatPanel` no longer renders inline `ModelPicker`; shows read-only model label
- [x] `ChatPanel` always includes `personal` in `kbTypes`
- [x] `sendChat`, `sendChatStreaming`, `searchKb` all accept + pass `kbTypes`/`projectId`
- [x] FAIT branding (navy/gold, Inter) maintained in `SettingsPanel`
- [x] 0 TypeScript errors, 0 dotnet errors
- [x] Both repos committed and pushed
