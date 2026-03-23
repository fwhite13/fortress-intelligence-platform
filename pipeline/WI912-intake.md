# WI#912 — FAM OS UAT Fix: Button styles + Accounts page click behavior

**Priority:** HIGH — blocking UAT
**Filed:** 2026-03-20 10:20 EDT

## Bug 1 — famos-btn-primary missing text-transform: none (CRITICAL visual)
**Symptom:** "New Opportunity" button on Pipeline page and buttons in OpportunityCreateDialog appear as solid navy with no visible text (icon only).
**Root cause:** `.famos-btn-primary` in `famos.css` is missing `text-transform: none !important`. MudBlazor's default transforms the text to uppercase but the CSS specificity fight causes the text to disappear entirely in some render contexts.
**Fix:** Add `text-transform: none !important;` to `.famos-btn-primary` in `wwwroot/css/famos.css`. Also verify `.famos-btn-primary-sm` has the same.

## Bug 2 — Accounts page: all clicks go to /pipeline (wrong behavior)
**Symptom:** Clicking any account row navigates to `/pipeline?company=X` regardless of opp count.
**Expected behavior:**
- 0 active opps → navigate to `/opportunities/new?company={name}` (new opp pre-populated with account name)
- Exactly 1 active opp → navigate directly to `/opportunity/{oppId}` workspace
- 2+ active opps → navigate to `/pipeline?company={name}` (filtered view — current behavior, correct for this case)
**Fix:** In `Accounts.razor`, update `GoToPipeline()` to:
1. Check `account.ActiveOppCount`
2. If 0 → `Nav.NavigateTo($"/opportunities/new?company={Uri.EscapeDataString(account.CompanyName)}")`
3. If 1 → load the opp Id for that account from DB (need a quick service call or store OppId on the account row model), then `Nav.NavigateTo($"/opportunity/{oppId}")`
4. If > 1 → existing behavior

**Note on OpportunityCreateDialog:** Verify it accepts a `?company=` query param or a `[Parameter]` to pre-populate the account name field when navigating from accounts page.

## Files to change
- `famos/src/FamOs.Web/wwwroot/css/famos.css` — Bug 1
- `famos/src/FamOs.Web/Components/Pages/Accounts.razor` — Bug 2
- `famos/src/FamOs.Web/Services/AccountSyncService.cs` (or equivalent) — may need to store single opp ID on account model for Bug 2 case

## No migration needed for Bug 1. Bug 2 may need a query change to fetch the single opp ID.
