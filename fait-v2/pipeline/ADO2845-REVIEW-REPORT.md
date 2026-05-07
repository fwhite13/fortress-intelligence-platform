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

## Cycle 2 — pending C1+C2 fixes
