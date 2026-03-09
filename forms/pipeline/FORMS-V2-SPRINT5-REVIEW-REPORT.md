# Review Report: FORMS v2 Sprint 5 — SurveyJS Generation (Commit 031a7c4)

**Reviewer:** Hawkeye  
**Date:** 2026-03-03  
**Branch:** main  
**Commit:** 031a7c4 (feat), ba6b5bc (build report)

### Verdict: NEEDS-CHANGES

---

## Consistency Audit

**Files Cross-Referenced:**

| Check | Result |
|-------|--------|
| `GeneratorService.cs` model ID string ↔ Task Brief spec (`us.anthropic.claude-sonnet-4-6`) | ✅ Matches |
| `ProjectQuestionSet.razor` nav target ↔ `ProjectSurveyPreview.razor` route | ✅ `/projects/{ProjectId}/survey-preview` — consistent |
| `survey-interop.js` `renderSurvey` function name ↔ `ProjectSurveyPreview.razor` JS call | ✅ `renderSurvey` — consistent |
| `survey-interop.js` `downloadFile` function ↔ `ProjectSurveyPreview.razor` JS call | ✅ `downloadFile` — consistent |
| `GeneratorService` `GenerateSurveyJsonAsync(int projectId, string tone)` ↔ razor call | ✅ Signatures match |
| `FormFieldCodes` loaded via `IDbContextFactory` in `GenerateSurveyJsonAsync` | ✅ Correct |
| `Where(f => f.ProjectId == projectId)` — no navigation properties before `Include()` | ✅ Clean; no nav properties used in `Where()` |

**Undocumented Dependencies Checked:**

- `downloadFile` is also called in `ProjectQuestionSet.razor` (Export JSON) — ✅ same function, compatible usage
- `survey-interop.js` `initSurveyPreview` (legacy Sprint 4 function) — still present, not broken by Sprint 5 additions ✅

---

## Critical Issues: 0

No critical bugs, consistency mismatches, or security issues found.

---

## Important Issues: 2

### I1: `StateHasChanged()` Missing After `_generating = false` in `finally` Block

**File:** `FortressFormTools.Web/Components/Pages/ProjectSurveyPreview.razor` (lines 153–157)

**Issue:** The `finally` block sets `_generating = false` but does not call `StateHasChanged()`. On the **success path**, this works accidentally — `StateHasChanged()` was already called at line 144 before the await. But on the **error path** (catch fires), the UI will not re-render to:
1. Re-enable the Generate button
2. Display the `MudAlert` error message

The `catch` block sets `_errorMessage` at line 152, then `_generating = false` fires in `finally` — but without a subsequent `StateHasChanged()`, Blazor may not re-render promptly (depends on whether the async continuation re-enters the render cycle). This is timing-sensitive and unreliable.

**Evidence:**
```csharp
catch (Exception ex)
{
    _errorMessage = $"Generation failed: {ex.Message}";   // ← set here
}
finally
{
    _generating = false;   // ← no StateHasChanged() after this
}
```

**Impact:** On generation failure, the Generate button may remain visually disabled and the error alert may not appear until the next UI interaction triggers a re-render.

**Fix:**
```diff
  finally
  {
      _generating = false;
+     StateHasChanged();
  }
```

---

### I2: `IDialogService` Not Injected in `ProjectSurveyPreview.razor`

**File:** `FortressFormTools.Web/Components/Pages/ProjectSurveyPreview.razor`

**Issue:** The Task Brief specifies `IDialogService` should be present for any dialogs. The current page has no `@inject IDialogService DialogService` and no dialog usage. This is not a bug today, but the brief explicitly calls it out as a requirement — likely intended for a future "Are you sure you want to regenerate?" confirmation when a survey already exists, or for displaying generation result details.

The page currently does nothing to warn the user before regenerating over an existing result (silently overwrites `_surveyJson`).

**Evidence:** No `IDialogService` in injections:
```razor
@inject IDbContextFactory<AppDbContext> DbFactory
@inject FortressFormTools.Web.Services.GeneratorService GeneratorService
@inject IJSRuntime JS
@inject NavigationManager Nav
// ← IDialogService missing
```

**Impact:** No guard before regeneration — users can accidentally overwrite a result they wanted to keep. The spec called this out; it's absent.

**Fix:** Add `@inject IDialogService DialogService` and a confirmation dialog when `_surveyJson != null` and user clicks Generate:

```razor
@inject IDialogService DialogService
```

```csharp
private async Task GenerateSurveyAsync()
{
    if (_surveyJson != null)
    {
        var confirmed = await DialogService.ShowMessageBox(
            "Regenerate Survey?",
            "This will replace the current survey. Continue?",
            yesText: "Regenerate", cancelText: "Cancel");
        if (confirmed != true) return;
    }
    // ... rest of method
}
```

---

## Nitpicks: 2

- **N1:** `CopyJson` uses `navigator.clipboard.writeText` via JS interop (`"navigator.clipboard.writeText"`) — this is not a defined `window.*` function but a dotted property path. Works in modern Chromium but may silently fail in some environments. A proper `window.copyToClipboard(text)` wrapper in `survey-interop.js` would be more reliable and consistent with the existing `downloadFile`/`renderSurvey` pattern. Not blocking.

- **N2:** `GenerateSurveyJsonAsync(int projectId, string tone)` — the `tone` parameter is passed to the prompt but not validated. A caller could pass an empty string or arbitrary value. Given this is only called from a controlled Razor dropdown with known values, not a security issue — but a quick `string.IsNullOrWhiteSpace(tone) ? "professional" : tone` default guard would make it more robust. Not blocking.

---

## Positive Observations

- ✅ **CLI code fully removed.** Zero `ProcessStartInfo`, `Process`, `System.Diagnostics` in `GeneratorService.cs`. Clean break.
- ✅ **`IAmazonBedrockRuntime` constructor-injected** correctly — matches the codebase pattern from `CrossReferenceService`.
- ✅ **`RunClaudeAsync` uses `InvokeModelRequest` with `us.anthropic.claude-sonnet-4-6`** — exact correct model ID.
- ✅ **`GenerateSurveyJsonAsync(int projectId, string tone)` exists** — correct signature, in-memory only (no DB save).
- ✅ **`FormFieldCode` records loaded via `IDbContextFactory`** — correct Blazor Server pattern.
- ✅ **Markdown fence stripping** (`ExtractJson`) is robust — handles ```json, finds first `{`...last `}` as fallback.
- ✅ **JSON validation** on Bedrock response — `JsonDocument.Parse` with informative throw including raw response.
- ✅ **No navigation properties in `Where()`** — both `Where()` calls filter on scalar foreign key (`ProjectId`, `QuestionSetId`).
- ✅ **`@page "/projects/{ProjectId:int}/survey-preview"`** — correct route with type constraint.
- ✅ **`IDbContextFactory` (not HttpClient) for DB reads** in `OnInitializedAsync`.
- ✅ **`<HeadContent>` loads SurveyJS CDN page-locally** — not polluting global `_Host.cshtml`.
- ✅ **Generate button disables during generation** (`Disabled="@_generating"`).
- ✅ **`MudAlert` for error display** — wired to `_errorMessage`.
- ✅ **Export uses `downloadFile` JS interop** — correct and consistent with existing usage.
- ✅ **DOM timing handled with `Task.Delay(150)`** — acceptable alternative to `Task.Yield()` for ensuring Blazor re-renders before the JS call hits the DOM.
- ✅ **`StateHasChanged()` called after generation completes** (success path) before JS interop.
- ✅ **"Generate Survey →" button disabled when status != "Approved"** (`Disabled="@(_questionSetStatus != "Approved")"`).
- ✅ **Navigates to `/projects/{ProjectId}/survey-preview`** — consistent with page route.
- ✅ **`renderSurvey` JS function** gracefully handles both legacy `Survey.Model` bundle and modular `SurveyCore`/`SurveyUI` — and degrades gracefully when neither is loaded.
- ✅ **Double-dollar raw string (`$$"""`)** is a sharp fix for the interpolated braces issue in the SurveyJS prompt. Good catch in the build notes.

---

## Acceptance Criteria Verification

| Criterion | Status |
|-----------|--------|
| All ProcessStartInfo / Process / CLI code GONE | ✅ Verified — zero matches |
| `IAmazonBedrockRuntime` constructor-injected | ✅ Verified |
| `RunClaudeAsync` uses `InvokeModelRequest` with `us.anthropic.claude-sonnet-4-6` | ✅ Verified |
| `GenerateSurveyJsonAsync(int projectId, string tone)` exists | ✅ Verified |
| Loads `FormFieldCode` records via `IDbContextFactory` | ✅ Verified |
| Strips markdown fences from Bedrock response | ✅ Verified (ExtractJson) |
| Validates JSON parse before returning | ✅ Verified (throws with raw response) |
| No navigation properties in `Where()` before `Include()` | ✅ Verified |
| Route `@page "/projects/{ProjectId:int}/survey-preview"` | ✅ Verified |
| Uses `IDbContextFactory` (NOT HttpClient) for DB reads | ✅ Verified |
| `IDialogService` for any dialogs | ❌ Missing — not injected, no confirmation dialog |
| `StateHasChanged()` after generation completes | ⚠️ Partial — present on success path; missing from `finally` block (error path gap) |
| `<HeadContent>` loads SurveyJS CDN (not globally) | ✅ Verified |
| Generate button disables during generation | ✅ Verified |
| Error shown in MudAlert if generation fails | ✅ Verified (but see I1 — may not render without StateHasChanged in finally) |
| Export uses `downloadFile` JS interop | ✅ Verified |
| Preview tab calls `renderSurvey` after tab switch timing | ✅ Verified — `Task.Delay(150)` before JS call |
| "Generate Survey →" button disabled when status != "Approved" | ✅ Verified |
| Navigates to `/projects/{ProjectId}/survey-preview` | ✅ Verified |

---

## Summary

**2 Important / 2 Nitpick / 0 Critical**

The implementation is solid. The Bedrock SDK migration is clean, the JS interop is well-structured with graceful fallbacks, and the page-level SurveyJS CDN loading is the right pattern. Two items need fixes before this can pass:

1. **`StateHasChanged()` in `finally`** — prevents the error state from reliably rendering (button stays disabled on failure).
2. **`IDialogService` injection + regeneration confirmation** — explicitly required by spec; guards against silent overwrites.

Fix both, resubmit, and this is a PASS.

---

_Reviewed by Hawkeye — 2026-03-03_
