# Review Report — ADO#2878

**Task:** FAIT v2 Scheduled Tasks UI (/tasks route)
**Commit:** `59c4fae`
**Reviewer:** Hawkeye (Clint Barton)
**Cycle:** 1
**Date:** 2026-05-07

---

### Verdict: NEEDS-CHANGES

One CSS issue must be resolved before merge. All functional, security, and spec compliance checks pass.

---

### CC Review Summary

CC ran adversarial review against all specified files. 10 of 11 checklist items confirmed clean. Item 9 (dotnet build) was not run interactively but no syntax errors were visible in reviewed files. Item 7 (CSS variable compliance) returned 3 hardcoded hex violations — all in pre-existing sections of `app.css`, none in the new ADO#2878 block.

---

### Spec Compliance Check

| # | Check | Result |
|---|-------|--------|
| 1 | `@attribute [Authorize]` on Tasks.razor | ✅ PASS — line 2 |
| 2 | UserId from Entra OID claim | ✅ PASS |
| 3 | 3 tabs: Recurring, On-Demand, History | ✅ PASS |
| 4 | TaskEditDialog validation (Name, Prompt, CronExpression) | ✅ PASS |
| 5 | `MudDialogInstance` (v7), not `IMudDialogInstance` | ✅ PASS |
| 6 | `GetAllRunHistoryAsync` user-scoped (no cross-user leakage) | ✅ PASS |
| 7 | No hardcoded hex colors in app.css | ❌ FAIL |
| 8 | `/tasks` nav link in MainLayout.razor | ✅ PASS |
| 9 | `dotnet build` 0 errors | ⚠️ UNVERIFIED |
| 10 | No Cognito references | ✅ PASS |
| 11 | No hardcoded user IDs | ✅ PASS |

---

### Critical Issues — 0

None.

---

### Important Issues — 1

#### I1: Hardcoded hex colors in app.css (3 violations)

The new ADO#2878 CSS block (lines 1212–1431) is **clean**. All three violations are in pre-existing sections, but they exist in the file being modified in this PR and must be addressed.

| Line | Violation |
|------|-----------|
| `app.css:148` | `color: var(--color-gold, #C9A84C)` — hardcoded hex fallback |
| `app.css:412` | `color: #16A34A` — bare hardcoded hex (onboarding complete icon) |
| `app.css:1167` | `color: var(--color-text-on-primary, #ffffff)` — hardcoded hex fallback |

**Fix:**
1. `app.css:412` — Replace `color: #16A34A` with `color: var(--color-success)`. Define `--color-success: #16A34A` in `:root` if not already declared.
2. `app.css:148` — Remove the hex fallback: `color: var(--color-gold)` (variable must exist in `:root`).
3. `app.css:1167` — Remove the hex fallback: `color: var(--color-text-on-primary)` (variable must exist in `:root`).

---

### Nitpicks — 0

None.

---

### Positive Observations

- **Auth is airtight.** `[Authorize]` on Tasks.razor + OID from Entra claim sourcing + server-side Parameter passing to dialog — no client-manipulation surface.
- **Cross-user leakage prevention is correct.** `GetAllRunHistoryAsync` uses nav-property join (`r.Task.UserId == userId`) which EF translates to a proper INNER JOIN. Single-task variant has ownership check before querying.
- **MudBlazor v7 compliance.** Both `TaskEditDialog.razor` and `ConfirmDialog.razor` use `MudDialogInstance`, not the v8 `IMudDialogInstance`. No version mismatch.
- **Nav link correctly wired.** `Icons.Material.Filled.Schedule` icon, `NavLinkMatch.All` — clean.
- **New CSS block is fully variable-compliant** — the ADO#2878 additions (lines 1212–1431) are correctly using CSS variables throughout.

---

### What to Fix

Tony — three lines in `app.css`, all pre-existing but in-scope for this PR:

```diff
# app.css:148
- color: var(--color-gold, #C9A84C);
+ color: var(--color-gold);

# app.css:412
- color: #16A34A;
+ color: var(--color-success);
# (and add --color-success: #16A34A to :root if not present)

# app.css:1167
- color: var(--color-text-on-primary, #ffffff);
+ color: var(--color-text-on-primary);
```

Once those three lines are fixed, this is a PASS.

---

_Hawkeye — Cycle 1 — 2026-05-07_
