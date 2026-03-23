# Review Report: WI#912 — FAM OS UAT Fixes

**Reviewer:** Hawkeye (Clint Barton)
**Date:** 2026-03-20
**Commit:** `a4ffa2f97da8acf419a48a689cde4a35d9025735`
**Review Cycle:** 1 of 2
**Branch:** main

---

## Verdict: ✅ PASS

All P1 (build-blocking) items clear. No P2 blockers. Two P3 nitpicks noted — non-blocking.

---

## CC Invocation

```bash
cd /home/fredw/.openclaw/agents/pipeline-manager
cat review-brief.md | claude --model sonnet -p
```

CC read all three changed files in full, evaluated all 7 checklist items, and returned structured findings.

---

## Files Reviewed

| File | Scope Check |
|------|-------------|
| `famos/src/FamOs.Web/wwwroot/css/famos.css` | ✅ Only expected changes |
| `famos/src/FamOs.Web/Components/Dialogs/OpportunityCreateDialog.razor` | ✅ Only expected changes |
| `famos/src/FamOs.Web/Components/Pages/Accounts.razor` | ✅ Only expected changes |

**File scope:** `git diff a4ffa2f~1 a4ffa2f --name-only` returns exactly 3 files — all within `famos/src/FamOs.Web/`. No pipeline report files bundled. ✅

---

## P1 Findings (Build-Blocking)

### P1-1 — CSS `.famos-btn-primary-sm` ✅ PASS

| Check | Result |
|-------|--------|
| `background-color: #002050 !important` present | ✅ PASS |
| `color: white !important` present | ✅ PASS |
| `:hover` rule present (`#001840`) | ✅ PASS |
| `text-transform: none !important` still present (not removed) | ✅ PASS |

Full block confirmed correct. Hover shade is distinct from base (`#001840` vs `#002050`).

---

### P1-2 — `GoToAccount` Method Signature & Onclick Wiring ✅ PASS

| Check | Result |
|-------|--------|
| Signature is `private async Task GoToAccount(Account account)` | ✅ PASS |
| `@onclick` wired as lambda `() => GoToAccount(account)` | ✅ PASS |
| Parameter is typed `Account` (not string) | ✅ PASS |

No void/async mismatch. Lambda wiring is correct — won't cause compile errors.

---

### P1-3 — `DialogParameters<OpportunityCreateDialog>` Syntax ✅ PASS

| Check | Result |
|-------|--------|
| MudBlazor v7 typed lambda syntax used | ✅ PASS |
| Old string-key pattern `["InitialCompanyName"]` absent | ✅ PASS |

Code confirmed:
```csharp
var parameters = new DialogParameters<OpportunityCreateDialog>
{
    { x => x.InitialCompanyName, account.CompanyName }
};
```
Correct MudBlazor v7 pattern. ✅

---

### P1-4 — `OpportunityCreateDialog` `OnInitialized` Wiring ✅ PASS

| Check | Result |
|-------|--------|
| `_name` set from `InitialCompanyName` in `OnInitialized()` | ✅ PASS |
| Guard `!string.IsNullOrWhiteSpace` present before assignment | ✅ PASS |
| Uses `OnInitialized` (not `OnParametersSet`) | ✅ PASS |

Code confirmed:
```csharp
protected override void OnInitialized()
{
    if (!string.IsNullOrWhiteSpace(InitialCompanyName))
        _name = InitialCompanyName;
}
```

---

### P1-5 — Single-Opp Fallback (No Zero-GUID Nav) ✅ PASS

| Check | Result |
|-------|--------|
| Falls back to pipeline nav if `opp == default(Guid)` | ✅ PASS |
| Guards against `/opportunity/00000000-0000-0000-0000-000000000000` | ✅ PASS |

Code confirmed:
```csharp
if (opp != default)
    Nav.NavigateTo($"/opportunity/{opp}");
else
    Nav.NavigateTo($"/pipeline?company={Uri.EscapeDataString(account.CompanyName)}");
```
Fallback is solid. ✅

---

## P2 Findings (Should Fix)

### P2-6 — File Scope ✅ PASS (Verified by Reviewer)

`git diff a4ffa2f~1 a4ffa2f --name-only` output:
```
famos/src/FamOs.Web/Components/Dialogs/OpportunityCreateDialog.razor
famos/src/FamOs.Web/Components/Pages/Accounts.razor
famos/src/FamOs.Web/wwwroot/css/famos.css
```
Exactly 3 files, all within `famos/src/FamOs.Web/`. ✅

---

### P2-7 — `IDialogService` Inject ✅ PASS

`@inject IDialogService DialogService` appears exactly once in `Accounts.razor`, in the correct position at the top of the directive block. No duplicate. ✅

---

## P3 Nitpicks (Non-Blocking)

### P3-1 — `EF.Functions.Like` with User-Controlled Input

In the `ActiveOppCount == 1` branch:
```csharp
EF.Functions.Like(o.Name, $"%{account.CompanyName}%")
```
If `CompanyName` contains LIKE wildcards (`%`, `_`), results could be unexpectedly broad. EF parameterizes the value (no SQL injection risk), but could return false positives for companies with unusual names. Low risk for internal data. Not a blocker — flagging for awareness.

**Recommendation:** Consider escaping wildcards if `CompanyName` values are user-entered and could contain `%` or `_`. Not required for this sprint.

### P3-2 — `famos-btn-primary-sm` Not Used in Dialog

The CSS fix applies to `.famos-btn-primary-sm`, but `OpportunityCreateDialog` uses `famos-btn-primary` (the standard size). The fix targets usages elsewhere in the app, not this dialog. Confirmed intentional per task brief scope.

---

## Summary

Tony's implementation is clean and correct. All three fixes are surgical, well-scoped, and hit the exact acceptance criteria. The MudBlazor v7 dialog parameter syntax is right, the async routing logic is solid, the CSS is complete, and the zero-GUID fallback is properly guarded. No P1 or P2 issues. Ready to advance.

---

## Recommendation

**Advance to SECURITY stage** (or APPROVE per risk-based shortcut if applicable).

---

*Arrow don't miss twice. This one hit clean.*
