# Review Report — ADO#2845
## FAIT v2: 4-step onboarding wizard UI + post-onboarding landing

**Agent:** Hawkeye (Clint Barton) — cycle 1 (recovered from session history)
**Commit:** `f380926`
**Verdict:** NEEDS-CHANGES — 2 Critical issues

---

## Critical Issues (block PASS)

**C1 — Data Loss: wizard inputs discarded at provisioning**
The wizard collects 8+ fields (role, responsibilities, communication style, response format, citations toggle, use cases, preferred name, assistant name, accent color) but `FinishWizard()` passes only `displayName` to `ProvisionAsync`. All collected data is thrown away. The spec (§5.2) states wizard data feeds into the SOUL.md template — this is not happening.

Fix: Extend `ProvisionAsync` signature (or add a `WizardData` parameter) and pass the collected preferences. At minimum, incorporate them into the SOUL.md template written to S3. Full DB persistence of preferences is a future WI — but the data must flow through, not get silently discarded.

**C2 — EntraOid empty-string risk**
If Entra claims are missing, `_entraOid` remains `""`. `GetOrCreateUserId` then calls `FirstOrDefault(u => u.EntraOid == "")` — this could match a corrupted/test DB record, causing identity spoofing.

Fix: Guard at top of `ProvisionAsync` (in addition to the existing GUID guard on userId):
```csharp
if (string.IsNullOrWhiteSpace(entraOid))
    throw new ArgumentException("entraOid cannot be empty", nameof(entraOid));
```
And in `Onboarding.razor`, if `_entraOid` is empty after auth state load, show an error instead of proceeding.

---

## Important Issues

**I3 — Redirect logic in `OnAfterRenderAsync` causes UI flash**
The redirect guard fires in `OnAfterRenderAsync` — correct for Blazor (can't navigate in `OnInitializedAsync`), but results in a brief flash of the requested page before redirect. Consider a loading overlay pattern.

**I4 — Double DB query**
`OnInitializedAsync` does one lookup; `GetOrCreateUserId` does another. Can be consolidated by caching the result.

**I5 — `ProvisionAsync` return value discarded**
`WasProvisioned` bool not captured — can't distinguish first-time vs. idempotent re-provision in logs.

---

## Passing Items (all other checks)

Routes.razor redirect guard ✅, 4 steps present ✅, all step fields ✅, MudStepper ✅, Back/Next nav ✅, loading spinner ✅, success redirect to `/` ✅, failure error+retry ✅, `@attribute [Authorize]` ✅, Dashboard welcome with name ✅, user info from AuthState ✅, wizard state in component only ✅, build clean ✅, zero Cognito ✅.

---

---

## Cycle 2 — Review Report

**Agent:** Hawkeye (Clint Barton)
**Commit:** `9f5a6d2`
**Date:** 2026-05-06
**Verdict:** NEEDS-CHANGES — C1 ✅ C2 ✅ fixed; 1 new Important issue requires action

---

### Spec Compliance

Files changed in `9f5a6d2`:
- `Services/IUserProvisioningService.cs` — ✅ in scope
- `Services/UserProvisioningService.cs` — ✅ in scope
- `Components/Pages/Onboarding.razor` — ✅ in scope
- `pipeline/ADO2845-REVIEW-REPORT.md` + `pipeline/brief-c2.md` — ✅ pipeline artifacts, not source

No out-of-scope changes. ✅

---

### C1 Verification — WizardData flows into SOUL.md

| Check | Result |
|-------|--------|
| `WizardData` record exists with all 9 fields | ✅ |
| Interface `ProvisionAsync` adds `WizardData? wizardData = null` | ✅ |
| Implementation signature matches interface | ✅ |
| `BuildSoulMdContent` uses 8 of 9 fields (all except AccentColor — correct, UI-only) | ✅ |
| S3 write calls `BuildSoulMdContent(displayName, wizardData)` | ✅ |
| `BuildWizardData()` collects all 9 UI fields | ✅ |
| `FinishWizard()` passes `wizardData:` to `ProvisionAsync` | ✅ |
| Data not discarded — end-to-end path verified | ✅ |

**C1: FIXED ✅**

---

### C2 Verification — Empty EntraOid guard

| Check | Result |
|-------|--------|
| `ProvisionAsync` throws `ArgumentException` for null/whitespace `entraOid`, after GUID guard | ✅ |
| `OnInitializedAsync` sets `_provisionError = true` + `_errorMessage`, returns early if `_entraOid` empty | ✅ |

**C2: FIXED ✅**

---

### Issues Found in Cycle 2

#### Important — blocks PASS

**I1 — `_additionalContext` silently discarded**
- **File:** `Onboarding.razor`
- **Issue:** The UI field _"Anything else you'd like your assistant to know?"_ (`_additionalContext`) is rendered in Step 3, bound to `_additionalContext`, and accepts user input — but it is **not in `WizardData`** and never passed to `BuildSoulMdContent`. User-supplied context is silently dropped at provisioning time.
- **Impact:** Functionally equivalent to C1 for that field. The user fills it in, the wizard shows it was collected, it gets thrown away.
- **Fix (option A):** Add `AdditionalContext: string?` to `WizardData`, populate it from `BuildWizardData()`, emit it in `BuildSoulMdContent` under User Context.
- **Fix (option B):** Remove the `_additionalContext` UI field entirely and note it's out of scope.

#### Important — tracked, does not block

**I2 — Dead if/else in `BuildSoulMdContent` Personality section**
- **File:** `UserProvisioningService.cs`
- **Issue:** Both branches of the `if (wizardData != null)` / `else` for `## Personality` emit identical content. Dead code. Simplify to unconditional `AppendLine`.

**I3 — `UseCases` null-reference risk in `BuildSoulMdContent`**
- **File:** `UserProvisioningService.cs`
- **Issue:** `wizardData.UseCases.Count > 0` — no null guard. Low risk in current call paths but a correctness gap.
- **Fix:** `wizardData.UseCases?.Count > 0`

#### Nitpicks

**N1 — `IsNullOrEmpty` vs `IsNullOrWhiteSpace` inconsistency**
Blazor guard uses `IsNullOrEmpty(_entraOid)`, service uses `IsNullOrWhiteSpace`. A whitespace OID passes the UI guard and hits the service throw. Functionally caught, but align for consistency.

**N2 — `AccentColor` unused in provisioning**
Likely intentional (UI-only theming). Add a comment to the `WizardData` field noting it's UI-only.

---

### Cycle 1 Important Issues Carryover

- **I3 (redirect flash):** Not addressed — tracked, does not block.
- **I4 (double DB query):** Not addressed — tracked, does not block.
- **I5 (return value discarded):** Not addressed — tracked, does not block.

---

### Required Fix Before PASS

**Tony: fix I1 only.** Either wire `_additionalContext` through to SOUL.md or remove the UI field. Everything else is tracked but not blocking.

---

### CC Review

**Command:** `cat /tmp/review-c2-brief.md | claude --model sonnet --print --dangerously-skip-permissions`
**Model:** Claude Sonnet
**Findings:** C1 ✅, C2 ✅, I1 (additionalContext drop) ❌ flagged, I2 (dead if/else) flagged, I3 (UseCases null risk) flagged
