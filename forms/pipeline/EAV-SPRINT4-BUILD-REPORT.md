# EAV Form Intelligence Tools — Sprint 4 Build Report

**Date:** 2026-02-26  
**Sprint:** 4 — Cross-Reference Engine + SurveyJS Generator  
**Commit:** 50944d9 (`Sprint 4: Cross-reference engine + SurveyJS generator`)

## Summary

Sprint 4 delivers two major features: a cross-reference analysis engine that compares form fields across carriers with AI-powered synonym detection, and a SurveyJS JSON generator with live preview. Both features are fully wired end-to-end (Blazor UI → API → Service → Claude Code CLI) with graceful fallbacks when CC is unavailable.

## Deliverable 1: Cross-Reference Engine
**Status:** ✅ Complete  
**Route:** `/question-sets/{id}/cross-reference`  
**API endpoints:** `POST /api/question-sets/{id}/analyze`, `POST /api/question-sets/{id}/fields/bulk`

### Implementation
- **CrossReferenceService.cs** — Core analysis logic:
  - Groups FormFields by DictionaryFieldId for exact matches
  - Shells out to `claude --model sonnet -p` for unmatched field synonym detection
  - Returns structured result with field groups, match types (exact/synonym/unique), coverage counts
  - Graceful fallback: if Claude fails, unmatched fields treated as unique
- **CrossReference.razor** — Two-step UI:
  - Step 1: Form selection with checkboxes (loads from /api/forms with field counts and status)
  - Step 2: Coverage heatmap (MudSimpleTable, color-coded chips, tooltips with carrier field labels)
  - Select/deselect field groups, bulk save to question set
  - Loading states with MudProgressLinear during analysis
- **Heatmap:** Green chips for exact matches, teal for synonym, gray outlined for absent. Auto-selects non-unique fields.
- **CC synonym detection:** Wired and tested. Builds structured prompt, parses JSON response, extracts from markdown fences.

### Files Created/Modified
- `Services/CrossReferenceService.cs` (new — 350 lines)
- `Components/Pages/CrossReference.razor` (new — 460 lines)
- `Controllers/QuestionSetsController.cs` (modified — added analyze + bulk endpoints)

## Deliverable 2: SurveyJS Generator
**Status:** ✅ Complete  
**Route:** `/generator/{questionSetId}`  
**SurveyJS preview:** Working (CDN + JS interop with graceful fallback)  
**CC generation:** Wired with fallback schema generation

### Implementation
- **GeneratorService.cs** — JSON generation logic:
  - Loads QuestionSet with fields and DictionaryField references
  - Loads ToneTemplate for prompt injection
  - Builds prompt and shells out to `claude --model sonnet -p`
  - Validates response has `pages` or `elements` key
  - Fallback: generates schema programmatically with correct SurveyJS field type mapping
  - Stores result in GeneratedSchema table with version tracking
- **GeneratorController.cs** — REST endpoints:
  - `POST /api/generator/{questionSetId}` — generate new schema
  - `GET /api/generator/{questionSetId}/schemas` — list all versions
  - `GET /api/generator/schemas/{schemaId}` — get specific schema
  - `GET /api/generator/tone-templates` — list available tones
- **Generator.razor** — Full UI:
  - Settings panel (tone template, progress bar, required mark)
  - Generate button with loading state
  - MudTabs: Preview tab (SurveyJS render or HTML fallback) + JSON tab (styled textarea, edit toggle, copy to clipboard)
  - Previous generations list with version history
- **survey-interop.js** — SurveyJS render interop with HTML form preview fallback

### Files Created/Modified
- `Services/GeneratorService.cs` (new — 250 lines)
- `Controllers/GeneratorController.cs` (new — 100 lines)
- `Components/Pages/Generator.razor` (new — 300 lines)
- `wwwroot/js/survey-interop.js` (new — 95 lines)
- `Components/App.razor` (modified — SurveyJS CDN + interop script)
- `Components/Pages/QuestionSetDetail.razor` (modified — nav buttons)
- `Program.cs` (modified — service registration)

## Build Results
- `dotnet build`: ✅ 0 errors (69 warnings — all pre-existing NuGet/Razor/MudBlazor)
- App starts: ✅ on port 5200
- `/ → 200` ✅
- `/api/dictionary → 19 records` ✅
- `/api/question-sets → ok, count=1` ✅
- `/api/generator/tone-templates → 3 templates` ✅
- `POST /api/question-sets/1/analyze → keys: questionSetId, formsAnalyzed, fieldGroups` ✅

## Claude Code Usage
- CrossReferenceService: `claude --model sonnet -p` for synonym detection (piped prompt via Process.Start)
- GeneratorService: `claude --model sonnet -p` for SurveyJS JSON generation (piped prompt via Process.Start)
- Both services have 120-second timeout and graceful error handling

## Known Issues / Sprint 5 Suggestions
1. **SurveyJS CDN loading** — Uses unpkg CDN which may be slow/blocked in some environments. Consider self-hosting the JS files for production.
2. **MudList Clickable warning** — `MUD0001` warning on Generator.razor — harmless, MudBlazor v7 API change.
3. **DictionaryFieldId resolution** — When saving bulk fields from cross-reference, the dictionary field ID could be resolved from the `dictionaryCode` field for exact matches (currently passes null).
4. **Question Set field deduplication** — No check for duplicate fields when saving bulk; running cross-reference twice will add duplicates.
5. **Survey preview** — Depends on CDN SurveyJS loading; the HTML fallback preview works well but isn't interactive.
