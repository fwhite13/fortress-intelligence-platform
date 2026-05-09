# Review Report — ADO#3128

### Verdict: NEEDS-CHANGES

---

### Spec Compliance Check

**§2 Codebase Map:**
- `src/FortressAI.Shared/Models/AppUser.cs` — ✅ modified as specified
- `src/FortressAI.Web/Data/AppDbContext.cs` — ✅ modified as specified
- `src/FortressAI.Web/Components/Chat/ChatView.razor` — ✅ modified as specified
- `src/FortressAI.Web/Components/Pages/AssistantSetup.razor` — ✅ created as specified
- `src/FortressAI.Web/Services/DatabaseInitializationService.cs` — ⚠️ modified (not in original checklist — see §5 below)

**§7 Acceptance Criteria:**
- [x] User with `NULL onboarding_completed_at` redirected to `/assistant-setup` on `/chat` load: ✅ Gate confirmed in `OnInitializedAsync`
- [x] Existing users (non-null) completely unaffected: ✅ Gate only fires on `user != null && user.OnboardingCompletedAt == null`
- [x] `/assistant-setup` completes and redirects to `/chat`: ✅ `Nav.NavigateTo("/chat", replace: true)` after `SaveChangesAsync`
- [x] No DB migration needed — columns already exist in `fait_dev.users`: ✅ Confirmed in DatabaseInitializationService (idempotent guard)
- [x] CSS variable rule — all values use `var(--...)`: ❌ ONE violation found (see C1 below)

**Spec compliance verdict:** ❌ NON-COMPLIANT — one CSS variable rule violation blocks PASS

---

### Consistency Audit

**Schema chain — end-to-end verified:**

| Layer | `OnboardingCompletedAt` | `OnboardingStep` |
|---|---|---|
| C# property | `DateTime?` (nullable) | `int?` (nullable) |
| EF mapping | `onboarding_completed_at` | `onboarding_step` |
| DatabaseInitializationService | `DATETIME(6) NULL` | `INT NULL` |

All three layers match exactly. No collision with existing fluent config. Explicit `HasColumnName` is redundant with Pomelo convention but is safe and consistent with the existing pattern (`is_active`, `is_entra_user`, `entra_oid`).

**Cross-file references:**
- `Nav.NavigateTo("/assistant-setup", replace: true)` in ChatView ↔ `@page "/assistant-setup"` in AssistantSetup.razor — ✅ exact match
- `Nav.NavigateTo("/chat", replace: true)` in AssistantSetup ↔ ChatView route `@page "/chat"` — ✅ exact match
- `Session.UserId` used in both gate and setup page — ✅ consistent

---

### CC Review Summary

CC confirmed all functional behavior is correct. One real finding (hardcoded `2px` in spinner CSS) and one process finding (DatabaseInitializationService change not in original checklist). All functional logic — auth ordering, gate placement, fail-open, DB writes, error handling — is clean.

False positives dismissed:
- CC flagged `420px` in card width fallback as a potential issue — I'm calling this a Nitpick, not a blocker. `var(--setup-card-width, 420px)` is a valid fallback pattern.
- CC raised the null-user fail-open scenario — behavior is correct and intentional.

---

### Issues Found

| Severity | File | Location | Issue | Fix |
|----------|------|----------|-------|-----|
| **Important** | `AssistantSetup.razor` | `.setup-spinner` CSS | `border: 2px solid color-mix(...)` — hardcoded `2px` outside CSS variable | Wrap in `var(--border-width-spinner, 2px)` or use `var(--border-width)` if the token value is appropriate |
| Process | `DatabaseInitializationService.cs` | Lines 391–392 | Column guards added — not in original checklist. Good engineering, but unannounced scope change. | No code change needed; document in PR description |
| Nitpick | `AssistantSetup.razor` | `.setup-card` CSS | `width: var(--setup-card-width, min(100%, 420px))` — `420px` hardcoded fallback | Define `--setup-card-width` in theme, or convert to `max-width: var(--setup-card-max-width, 420px); width: 100%;` |

---

### Detailed Findings

#### C1 — Important: Hardcoded `2px` in `.setup-spinner`

**File:** `AssistantSetup.razor` (spinner CSS block)

**Issue:** The project rule is zero hardcoded dimension values outside CSS variables. `.setup-spinner` has:
```css
.setup-spinner {
    border: 2px solid color-mix(in srgb, var(--color-text-on-accent) 30%, transparent);
}
```
The `2px` is a raw pixel value not wrapped in a CSS variable.

**Fix:**
```diff
- border: 2px solid color-mix(in srgb, var(--color-text-on-accent) 30%, transparent);
+ border: var(--border-width-spinner, 2px) solid color-mix(in srgb, var(--color-text-on-accent) 30%, transparent);
```
Or if `--border-width` already resolves to something appropriate for a spinner, use that.

---

#### D1 — Process: DatabaseInitializationService change

**File:** `src/FortressAI.Web/Services/DatabaseInitializationService.cs`

**Issue:** The review pre-flight checklist said "No changes to `DatabaseInitializationService.cs`." Tony added the `ADD COLUMN` guards (lines 391–392) which were not in scope per the original checklist.

**Assessment:** The change is **correct and safe** — idempotent guard, caught by MySQL error 1060, does not affect existing environments where columns already exist. This is good engineering. No code change needed.

**Action:** Document in the PR description that DatabaseInitializationService was modified for new-environment provisioning support.

---

### Spec Fidelity

The implementation correctly implements the acceptance criteria with one exception: the CSS variable rule has one violation (`2px` in spinner border). All functional behavior is correct:

- Gate fires after auth check, before Fargate launch ✅
- Gate uses `DbFactory` (already injected, correct pattern) ✅
- Fail-open on DB error ✅
- `[Authorize]` on setup page ✅
- DB write touches only `DisplayName`, `OnboardingCompletedAt`, `OnboardingStep` ✅
- Loading state correctly managed ✅
- Error display on submit failure ✅
- Both navigation calls use `replace: true` (back-button loop prevention) ✅
- Build: 0 errors, 31 pre-existing warnings ✅

---

### What to fix (NEEDS-CHANGES)

**One required fix:**

**AssistantSetup.razor — `.setup-spinner` CSS:**
Change `border: 2px solid` to use a CSS variable:
```diff
- border: 2px solid color-mix(in srgb, var(--color-text-on-accent) 30%, transparent);
+ border: var(--border-width-spinner, 2px) solid color-mix(in srgb, var(--color-text-on-accent) 30%, transparent);
```

One PR note (no code change):
- In the PR description, note that `DatabaseInitializationService.cs` was modified to add idempotent column guards for new-environment provisioning.

After the spinner fix is applied, this is a PASS.

---

_Reviewed by Hawkeye (Clint Barton) — Cycle 1_
