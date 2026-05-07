# Build Report — ADO#2845
## FAIT v2: 4-step onboarding wizard + post-onboarding landing

---

## Cycle 1 (commit `f380926`)

Initial build — 4-step MudStepper wizard, SOUL.md provisioning, post-onboarding dashboard.
Sent to Clint for review.

**Verdict:** NEEDS-CHANGES — 2 critical issues (C1, C2).

---

## Cycle 2 — BUILD REPORT

**Agent:** Tony Stark (software-engineer)
**Commit:** `9f5a6d2`
**Build result:** SUCCEEDED — 0 errors, 0 warnings

### What was built

Two targeted fixes addressing both critical issues from Clint's cycle 1 review:

**C1 — Wizard data now flows into SOUL.md (not discarded)**

- Added `WizardData` record to `IUserProvisioningService.cs` with 9 fields: Role, Responsibilities, CommunicationStyle, ResponseFormat, ShowCitations, UseCases, PreferredName, AssistantName, AccentColor
- Updated `IUserProvisioningService.ProvisionAsync` interface signature: added `WizardData? wizardData = null` before `CancellationToken`
- Updated `UserProvisioningService.ProvisionAsync` to match new signature
- Added `BuildSoulMdContent(string displayName, WizardData? wizardData)` private static method — builds enriched SOUL.md with User Context section (role, responsibilities, use cases, assistant name) and Communication Style section (style, format, citations)
- S3 SOUL.md write now calls `BuildSoulMdContent(displayName, wizardData)` instead of inline template substitution
- `Onboarding.razor`: replaced `BuildEnrichedDisplayName()` with `BuildWizardData()` — collects all 9 wizard fields into `WizardData` record
- `FinishWizard()` now passes `wizardData:` and `displayName: _displayName` to `ProvisionAsync`

**C2 — Guard against empty EntraOid**

- `UserProvisioningService.ProvisionAsync`: throws `ArgumentException("entraOid cannot be empty")` if `entraOid` is null/whitespace — after the GUID guard
- `Onboarding.razor` `OnInitializedAsync`: if `_entraOid` is empty after auth state load, sets `_provisionError = true` + `_errorMessage` and returns early — wizard shows error instead of proceeding

### Files changed

- `src/FortressAI.V2.Web/Services/IUserProvisioningService.cs` — Added `WizardData` record; updated `ProvisionAsync` signature
- `src/FortressAI.V2.Web/Services/UserProvisioningService.cs` — Updated signature; replaced SOUL.md template call with `BuildSoulMdContent`; added `BuildSoulMdContent` method; added entraOid guard
- `src/FortressAI.V2.Web/Components/Pages/Onboarding.razor` — `BuildWizardData()` replacing `BuildEnrichedDisplayName()`; updated `ProvisionAsync` call; entraOid empty guard in `OnInitializedAsync`

### Parallelization used

No — 3 files, sequential edits with inter-file dependencies (interface → service → razor).

### CC sessions run

1 CC run (Sonnet). Brief piped via `cat pipeline/brief-c2.md | claude --model sonnet --print --dangerously-skip-permissions`.

### Acceptance criteria verification

- [x] `WizardData` record in `IUserProvisioningService.cs` — confirmed in diff
- [x] `ProvisionAsync` interface signature includes `WizardData? wizardData = null` — confirmed
- [x] `UserProvisioningService.ProvisionAsync` signature matches — confirmed
- [x] `BuildSoulMdContent` uses wizardData fields to enrich SOUL.md — confirmed (Role, Responsibilities, UseCases, AssistantName, CommunicationStyle, ResponseFormat, ShowCitations)
- [x] S3 SOUL.md write calls `BuildSoulMdContent(displayName, wizardData)` — confirmed
- [x] `Onboarding.razor` calls `ProvisionAsync` with `wizardData:` parameter — confirmed
- [x] `ProvisionAsync` throws if `entraOid` is null/whitespace — confirmed
- [x] `OnInitializedAsync` guards empty `_entraOid` — confirmed
- [x] `dotnet build` — 0 errors, 0 warnings — confirmed

### Known edge cases / things Clint should scrutinize

- `BuildSoulMdContent` has a `wizardData != null` split for the Personality section — both branches emit the same content. This is harmless (always outputs the section) but could be simplified to a single `sb.AppendLine`. Not blocking.
- `WizardData` is not persisted to DB — only used at provisioning time to build the SOUL.md string. This is per spec (DB persistence is a future WI). If user re-provisions, wizard data from the original run won't be re-applied.
- `_selectedUseCases` is a `HashSet<string>` — `.ToList()` produces deterministic content (order may vary). For SOUL.md readability this is fine.

### How to test locally

1. Run app, log in as a test user who has NOT completed onboarding
2. Complete all 4 wizard steps with non-default values (specific role, select 2+ use cases, change style to Concise)
3. On finish, check S3 for `workspaces/{userId}/assistants/SOUL.md` — should contain Role, Use cases, Communication Style sections with the values entered
4. Test empty EntraOid: mock the auth claim to return `""` — wizard should show error message without proceeding

### ADO comment posted

Comment ID 781687, posted 2026-05-07T01:36:46Z

---

_Cycle 2 complete. Sending to Clint for re-review._

---

## Cycle 3 — BUILD REPORT

**Agent:** Tony Stark (software-engineer)
**Commit:** `ca856e5`
**Build result:** SUCCEEDED — 0 errors, 0 warnings

### What was built

Five targeted fixes from Clint's cycle 2 review. I1 blocked PASS; I2/I3/N1/N2 were tracked issues.

**I1 — AdditionalContext wired end-to-end (blocks PASS)**
- Added `string? AdditionalContext` to `WizardData` record in `IUserProvisioningService.cs` (before `AccentColor`)
- Added `AdditionalContext: _additionalContext,` to `BuildWizardData()` in `Onboarding.razor`
- Added emission of `AdditionalContext` in `BuildSoulMdContent()` under User Context section

**I2 — Dead if/else in Personality section simplified**
- Both branches of `if (wizardData != null)` / `else` in `BuildSoulMdContent()` emitted identical content — collapsed to single unconditional block.

**I3 — Null guard on UseCases**
- `wizardData.UseCases.Count > 0` → `wizardData.UseCases?.Count > 0` in `BuildSoulMdContent()`

**N1 — IsNullOrWhiteSpace consistency on entraOid**
- `string.IsNullOrEmpty(_entraOid)` → `string.IsNullOrWhiteSpace(_entraOid)` in `Onboarding.razor` `OnInitializedAsync`

**N2 — AccentColor comment**
- Added `// UI-only — not persisted at provisioning time` inline comment on `AccentColor` in `WizardData` record (handled as part of I1 edit)

### Files changed

- `src/FortressAI.V2.Web/Services/IUserProvisioningService.cs` — Added `AdditionalContext` to `WizardData`; added comment on `AccentColor`
- `src/FortressAI.V2.Web/Services/UserProvisioningService.cs` — Emit `AdditionalContext` in `BuildSoulMdContent`; collapsed dead Personality if/else; added null guard on `UseCases`
- `src/FortressAI.V2.Web/Components/Pages/Onboarding.razor` — Added `AdditionalContext: _additionalContext` to `BuildWizardData()`; `IsNullOrEmpty` → `IsNullOrWhiteSpace` on entraOid guard

### Parallelization used

No — sequential edits with inter-file dependencies (interface → service → razor).

### CC sessions run

1 CC run (Sonnet). Brief piped via `cat pipeline/brief-c3.md | claude --model sonnet --print --dangerously-skip-permissions`.

### Acceptance criteria verification

- [x] `WizardData.AdditionalContext` field exists in interface file — confirmed
- [x] `BuildWizardData()` sets `AdditionalContext: _additionalContext` — confirmed
- [x] `BuildSoulMdContent()` emits AdditionalContext when non-whitespace — confirmed
- [x] Dead Personality if/else collapsed — confirmed
- [x] `UseCases?.Count` null guard — confirmed
- [x] `IsNullOrWhiteSpace` used consistently for entraOid — confirmed
- [x] AccentColor comment present — confirmed
- [x] `dotnet build` — 0 errors, 0 warnings — confirmed

### ADO comment posted

Comment ID 781691, posted 2026-05-07T01:45:07Z

---

_Cycle 3 complete. Sending to Clint for re-review._
