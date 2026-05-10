# Review Report — ADO#3189

**Task:** 4.3-A: /memory page: topic list + markdown viewer/editor  
**Commit:** `027ce8c8`  
**Reviewer:** Hawkeye (Clint Barton, `code-reviewer`)  
**Review Cycle:** 1 of 2  
**Date:** 2026-05-10

---

## Verdict: NEEDS-CHANGES

---

## CC Review Summary

CC invoked:
```bash
cat /tmp/clint-review-brief-3189.md | claude --model sonnet --print --dangerously-skip-permissions
```
Working directory: `/home/fredw/projects/fip/fait`

CC performed full analysis across all 10 critical/important focus areas and 4 nitpicks. One real issue confirmed (Item 7 — reserved slug guard). All other findings came back clean. No false positives dismissed.

---

## Spec Compliance Check

**Files Added:**
- `src/FortressAI.Web/Components/Pages/Memory.razor` — ✅ created

**Files Modified:**
- `src/FortressAI.Web/Components/Layout/MainLayout.razor` — ✅ nav entry added (`/memory` with `Icons.Material.Filled.Psychology`)

**Scope:** No out-of-scope changes detected.

**Acceptance Criteria:**
- [x] `/memory` page exists with two-column layout (topic list left, editor right) — ✅
- [x] Nav entry added to MainLayout — ✅
- [x] All S3 ops through `IMemoryFileService` — ✅ No direct S3/DB calls in Memory.razor
- [x] `PageTitle` = "Memory — FAIT" — ✅

**Spec compliance verdict:** ✅ COMPLIANT (does not block verdict; single implementation defect does)

---

## Consistency Audit

**Files Cross-Referenced:**
- `Memory.razor` ↔ `MainLayout.razor` — ✅ nav link `/memory` matches `@page "/memory"`
- `Memory.razor` ↔ `IMemoryFileService` interface — service boundary clean, no leakage

**Undocumented Dependencies:** None found.

---

## Issues Found

| Severity | File | Location | Issue | Fix |
|----------|------|----------|-------|-----|
| Important | `Memory.razor` | `CreateTopicAsync()` ~line 356 | No reserved slug guard; user can create slug "memory" → unhandled API 400 | Add guard + Snackbar error before `WriteTopicAsync` call |
| Nitpick | `Memory.razor` | `ConfirmDeleteAsync()` ~line 300 | Method is `void` with no await; `Async` suffix misleading | Rename to `ConfirmDelete()` |

---

## Critical Issues: 0

All five critical checks **PASS**.

### C1 — `@foreach` closure capture ✅ PASS
`var localTopic = topic` declared **inside** the foreach body. `OnClick` captures `localTopic`. Correct.

### C2 — `_isDirty` lifecycle ✅ PASS
- Set `true`: `OnEditorInput` ✅
- Set `false` after Save: `SaveTopicAsync` ✅
- Set `false` on topic select: `SelectTopicAsync` (before state overwrite) + `LoadTopicContentAsync` (harmless double-reset) ✅
- Set `false` after Delete: `DeleteTopicAsync` ✅
- Set `false` in `CreateTopicAsync` path: after new topic selected ✅
- Exception path: `_isDirty` is set false in `SelectTopicAsync` before `LoadTopicContentAsync` is called — no stale dirty state possible on throw ✅

### C3 — No direct S3 / DB calls ✅ PASS
Entire `@code` block uses only `MemoryService.*`. No `IAmazonS3`, `IDbContextFactory`, `SaveChangesAsync`, `FromSqlRaw`, or AWS SDK namespace present.

### C4 — `IAsyncDisposable` / location handler cleanup ✅ PASS
- `_locationChangingHandler` declared as `IDisposable?` ✅
- Stored from `RegisterLocationChangingHandler` in `OnInitializedAsync` ✅
- `_locationChangingHandler?.Dispose()` called in `DisposeAsync()` ✅
- `DisposeAsync()` returns `ValueTask` ✅
- `@implements IAsyncDisposable` declared at top of file ✅

### C5 — Slug auto-generation ✅ PASS
- Lowercase via `.ToLowerInvariant()` ✅
- Spaces → hyphens via `.Replace(' ', '-')` ✅
- Non-alphanumeric (except `-`) stripped by char loop ✅
- Consecutive hyphens collapsed by `Regex.Replace("-+", "-")` ✅
- Leading/trailing dashes stripped by `.Trim('-')` ✅
- Edge cases (special chars, empty string) handled correctly ✅

---

## Important Issues: 1

### I1 — Reserved slug guard missing ❌ **NEEDS-CHANGES**

**File:** `Memory.razor`  
**Method:** `CreateTopicAsync()`, line ~356  
**Category:** Correctness  

**Issue:** No validation prevents a user from creating a topic with slug "memory" (the page's own route and potentially a system-reserved identifier). If user enters title "Memory" (or any casing), `GenerateSlug` produces `"memory"`. `WriteTopicAsync` is called without a guard. The API returns HTTP 400. `CreateTopicAsync` has **no try/catch**, so the exception propagates unhandled — the dialog closes, no Snackbar fires, user gets a silent crash.

**Evidence:**
```csharp
private async Task CreateTopicAsync()
{
    if (string.IsNullOrWhiteSpace(_newTitle) || string.IsNullOrWhiteSpace(_newSlug)) return;
    var slug = _newSlug.Trim();
    var title = _newTitle.Trim();
    _showNewDialog = false;
    // ← NO reserved slug guard here
    await MemoryService.WriteTopicAsync(Session.UserId, slug, title, string.Empty); // 400 if slug == "memory"
    // ← NO try/catch; exception propagates silently
```

**Impact:** User sees dialog close but no success message, no topic created, no error shown. Confusing and broken UX. If the API does worse than 400 (e.g., corrupts existing "MEMORY" system key), impact could be elevated.

**Fix:**
```diff
  var slug = _newSlug.Trim();
  var title = _newTitle.Trim();
  _showNewDialog = false;
+ if (slug.Equals("memory", StringComparison.OrdinalIgnoreCase))
+ {
+     Snackbar.Add("\"memory\" is a reserved slug. Choose a different title.", Severity.Error);
+     _showNewDialog = true;  // re-open dialog so user can correct
+     return;
+ }
  await MemoryService.WriteTopicAsync(Session.UserId, slug, title, string.Empty);
```

---

## Nitpick: 1

### N1 — `ConfirmDeleteAsync` misnaming
`private void ConfirmDeleteAsync()` performs no async work (just sets `_showDeleteConfirm = true`). The `Async` suffix is misleading. Rename to `ConfirmDelete()`. Not a functional defect; update the `OnClick` binding accordingly.

---

## Checks That Passed (Important)

**I2 — Topic switch with dirty state ✅**  
`SelectTopicAsync` checks `_isDirty` **before** overwriting `_selectedSlug`/`_selectedTitle`. Correct ordering.

**I3 — Reserved slug error handling ❌ (captured above as I1)**

**I4 — Null content handling ✅**  
`_editorContent = content ?? string.Empty` present. `_editorContent` initialized to `string.Empty`. Null-safe.

**I5 — `Session.IsAuthenticated` guard ✅**  
Render guard at `@if (!Session.IsAuthenticated)` and code guard `if (!Session.IsAuthenticated) return` in `OnInitializedAsync`. No service call possible without auth.

**I6 — CSS variable rule ✅**  
No hardcoded brand colors. `var(--color-gold)`, `var(--mud-palette-action-hover)`, `var(--font-bold)` used correctly. Structural layout values (px for heights, gaps) are acceptable.

---

## Nitpick Checks

**N2 — TimeAgo UTC ✅** `DateTime.UtcNow - dt.ToUniversalTime()` — UTC-correct.

**N3 — `Icons.Material.Filled.Psychology` ✅** Valid MudBlazor icon (Psychology is in Material Icons filled set).

**N4 — PageTitle ✅** `<PageTitle>Memory — FAIT</PageTitle>` present at line 5.

---

## What Tony Needs to Fix

**One change required before PASS:**

In `Memory.razor`, `CreateTopicAsync()`, add reserved slug guard before the `WriteTopicAsync` call:

```csharp
if (slug.Equals("memory", StringComparison.OrdinalIgnoreCase))
{
    Snackbar.Add("\"memory\" is a reserved slug. Choose a different title.", Severity.Error);
    _showNewDialog = true;
    return;
}
```

That's it. Everything else is clean. Fix this one thing and resubmit.

---

_Hawkeye / code-reviewer — ADO#3189 Cycle 1_
