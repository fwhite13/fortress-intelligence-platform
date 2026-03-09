# FORMS v2 Sprint 5 — Build Report

**Date:** 2026-03-03
**Branch:** main
**Commit:** 031a7c4
**Status:** ✅ BUILD SUCCEEDED — 0 errors

---

## Summary

Sprint 5 delivers SurveyJS JSON generation from an approved Question Set's `FormFieldCode` records. Users can navigate to the new Survey Preview page, select a tone, generate a live SurveyJS form via Bedrock AI, preview it in-browser, copy the raw JSON, and export it as a file.

---

## Changes Made

### 1. `FortressFormTools.Web/Services/GeneratorService.cs` — REPLACED CLI with Bedrock SDK

- **Removed:** `System.Diagnostics` using, all `ProcessStartInfo`/`Process` code, old `RunClaudeAsync(string prompt, string model)` method
- **Added:** Constructor injection of `IAmazonBedrockRuntime _bedrockRuntime`
- **Updated:** `RunClaudeAsync(string prompt)` now uses `InvokeModelRequest` with `us.anthropic.claude-sonnet-4-6` — exact pattern from `CrossReferenceService.cs`
- **Added:** New `GenerateSurveyJsonAsync(int projectId, string tone)` method:
  - Loads `FormProject.Name` from DB
  - Loads all `FormFieldCode` records for project, ordered by SectionName + SortOrder
  - Builds Bedrock prompt with field type mapping and SurveyJS structure example
  - Strips markdown fences via `ExtractJson()`
  - Validates JSON parses — throws with raw response if not
  - Returns JSON string (in-memory only, no DB save)
- **Preserved:** All existing Sprint 4 methods (`GenerateSurveyJsonAsync(questionSetId, toneTemplateId, settings)`, `GenerateFallbackSchema`, `MapFieldToSurveyElement`, `GetNextVersionAsync`, `ExtractJson`)

### 2. `FortressFormTools.Web/Components/Pages/ProjectSurveyPreview.razor` — NEW PAGE

- Route: `/projects/{ProjectId:int}/survey-preview`
- Loads project name on init
- Tone selector (Professional/Conversational/Formal/Simple), default "professional"
- Generate button → calls `GeneratorService.GenerateSurveyJsonAsync`, indeterminate progress bar while running
- Error display via `MudAlert` on failure
- Two tabs after generation:
  - **Preview** tab: `<div id="survey-container">` rendered via `renderSurvey` JS interop with 150ms DOM settle delay
  - **JSON** tab: `<pre>` block with Copy button
- Export button (shown post-generation) downloads `{projectName}-survey.json` via `downloadFile` JS interop
- `<HeadContent>` loads SurveyJS CDN (survey-core + survey-js-ui + defaultV2 CSS) on this page only

### 3. `FortressFormTools.Web/wwwroot/js/survey-interop.js` — APPENDED `renderSurvey`

- Added `window.renderSurvey(containerId, surveyJson)` function
- Tries `Survey.Model` (legacy bundle) → `SurveyCore`/`SurveyUI` (modular) → graceful fallback message
- Error-safe: wraps in try/catch, renders error HTML in container on failure

### 4. `FortressFormTools.Web/Components/Pages/ProjectQuestionSet.razor` — TOOLBAR LINK

- Added "Generate Survey →" `MudButton` (Color.Info, Filled) in the toolbar
- Disabled unless `_questionSetStatus == "Approved"`
- On click: navigates to `/projects/{ProjectId}/survey-preview`

---

## Build Result

```
Build succeeded.
    0 Error(s)
    N Warning(s) — all pre-existing CS8669 nullable context warnings in auto-generated Razor files
```

No new warnings introduced.

---

## Technical Notes

- **Raw string literal fix:** The prompt in `GenerateSurveyJsonAsync` uses `$$"""..."""` (double-dollar raw string) so interpolation uses `{{variable}}` and literal braces `{`/`}` remain unescaped in the JSON example line
- **CC CLI timeout:** CC CLI pipe mode timed out (>180s); implementation completed in-context using Bedrock fallback
- **No DB changes:** Fully in-memory generation — no new migrations, no new entities, no ALTER TABLE
- `GeneratorService` remains registered as `Scoped` in Program.cs — no DI changes needed

---

## Files Changed

| File | Change |
|------|--------|
| `FortressFormTools.Web/Services/GeneratorService.cs` | CLI→Bedrock, new project-level generation method |
| `FortressFormTools.Web/Components/Pages/ProjectSurveyPreview.razor` | New page (created) |
| `FortressFormTools.Web/wwwroot/js/survey-interop.js` | Added `renderSurvey` function |
| `FortressFormTools.Web/Components/Pages/ProjectQuestionSet.razor` | Added "Generate Survey →" toolbar button |
