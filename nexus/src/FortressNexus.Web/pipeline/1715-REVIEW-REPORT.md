# Review Report — ADO #1715: FileUploadZone Drag-and-Drop Fix

**Reviewer:** Hawkeye (Clint Barton)
**Review Cycle:** 1
**Commits:** `c6d7387` (fix) + `79e6233` (build report)
**Date:** 2026-04-13

---

## Verdict: NEEDS-CHANGES

---

## Spec Compliance Check

**§ Files changed:**
- `Components/Shared/FileUploadZone.razor` — ✅ modified as specified
- `Components/Shared/FileUploadZone.razor.css` — ✅ created as specified
- No other files touched — ✅ confirmed (git diff `c6d7387` shows exactly 2 files)

**§ Acceptance criteria from build report:**
- [x] `Hidden="false"` added to `MudFileUpload` — ✅ verified
- [x] `InputClass="file-upload-input-overlay"` added — ✅ verified
- [x] `.file-upload-zone` has `position: relative` — ✅ verified in CSS
- [x] `.file-upload-input-overlay` is absolute-positioned full-cover overlay — ✅ verified in CSS geometry
- [x] No JS interop added — ✅ confirmed
- [x] `dotnet build` 0 errors/warnings — ✅ accepted per build report
- [ ] Overlay CSS actually applied to `<input type="file">` — ❌ **NOT met** (see C1)

**Spec compliance verdict:** ❌ NON-COMPLIANT — the CSS delivery mechanism is broken (C1)

---

## Consistency Audit

**Files cross-referenced:**
- `FileUploadZone.razor` ↔ `FileUploadZone.razor.css` — ✅ class names referenced in razor exist in CSS (overlay class + zone class)
- `FileUploadZone.razor` ↔ `Services/FileStorageService.cs` — ✅ `Accept` attribute and `AllowedTypes` both unchanged
- `FileUploadZone.razor.css` vs compiled scoped output at `obj/Debug/net8.0/scopedcss/Components/Shared/FileUploadZone.razor.rz.scp.css` — ✅ compiled; ❌ confirms scope attribute `[b-0ic80wkjje]` will not match input element (see C1)

**Compiled scoped CSS (verified directly):**
```css
.file-upload-input-overlay[b-0ic80wkjje] {
    position: absolute; top: 0; left: 0;
    width: 100%; height: 100%;
    opacity: 0; cursor: pointer; z-index: 1;
}
```
The `[b-0ic80wkjje]` scope attribute is only added to elements rendered in **FileUploadZone's own Razor template**. The `<input type="file">` is rendered inside **MudFileUpload's** template — it never receives `[b-0ic80wkjje]`. This selector never matches.

---

## Critical Issues — 1

### C1: Overlay CSS silently not applied — scoped CSS cannot reach MudFileUpload's `<input>` ❌

- **File:** `Components/Shared/FileUploadZone.razor.css`
- **Category:** Correctness / Blazor scoped CSS behavior
- **Severity:** Critical (the entire drag-and-drop mechanism does not function as designed)

**Issue:**

Blazor scoped CSS compiles every rule in `FileUploadZone.razor.css` to require the component's generated scope attribute (verified: `[b-0ic80wkjje]`). The Blazor runtime adds this attribute only to HTML elements rendered directly by `FileUploadZone`'s own Razor markup.

The `<input type="file">` element is rendered by **MudFileUpload**'s Razor template — not FileUploadZone's. It receives MudFileUpload's scope attribute (or none), never `[b-0ic80wkjje]`. The rule `.file-upload-input-overlay[b-0ic80wkjje]` never matches.

**Consequence:** The `<input type="file">` receives none of the overlay styles — no `opacity: 0`, no `position: absolute`, no full-zone coverage. It renders wherever MudBlazor places it in the DOM as a visible native file input element. The invisible full-zone overlay technique does not function at all.

**Tony's build report is incorrect on this point:**
> *"The scoped CSS for `.file-upload-zone` and `.file-upload-input-overlay` will work correctly since those are on elements in this component's own render tree."*

`.file-upload-zone` is on a `<div>` in FileUploadZone's template — ✅ correct.  
`.file-upload-input-overlay` ends up on MudFileUpload's `<input>` — ❌ not in FileUploadZone's template.

**Fix — Option A (recommended): `::deep` in scoped CSS**

Blazor's `::deep` combinator strips the scope attribute from the rule it applies to, allowing penetration into child component trees. Add the rule to `FileUploadZone.razor.css`:

```diff
+::deep .file-upload-input-overlay {
+    position: absolute;
+    top: 0;
+    left: 0;
+    width: 100%;
+    height: 100%;
+    opacity: 0;
+    cursor: pointer;
+    z-index: 1;
+}
```

The `::deep` wrapper requires the parent element to carry the scope attribute — since `::deep` is scoped by its ancestor context, wrapping it inside `.file-upload-zone` in the CSS or ensuring the parent `<div class="file-upload-zone">` has the scope attribute is sufficient:

```css
/* This ensures the deep rule is anchored to this component's scope */
.file-upload-zone ::deep .file-upload-input-overlay {
    position: absolute;
    top: 0; left: 0;
    width: 100%; height: 100%;
    opacity: 0;
    cursor: pointer;
    z-index: 1;
}
```

**Fix — Option B: Move to global CSS**

Move `.file-upload-input-overlay` to `wwwroot/css/app.css`. Functional, but bleeds into global scope; not preferred.

---

## Important Issues — 1

### I1: Remove buttons blocked by overlay when file list is visible ⚠️

- **File:** `Components/Shared/FileUploadZone.razor.css` (line 22: `z-index: 1` on overlay)
- **Category:** Correctness / UX regression
- **Severity:** Important (blocks file removal after selection — only present if C1 is fixed)

**Issue:**

The overlay is `position: absolute` with `z-index: 1` covering the entire `.file-upload-zone`. When files are selected, the file list renders inside the same parent. The remove buttons (`MudIconButton`, class `file-upload-remove-btn`) have no explicit z-index and sit at `z-index: auto` (stack level 0). The overlay sits above them.

**If C1 is fixed (CSS actually applied):** Clicking a remove `×` button would hit the invisible overlay — the `<input type="file">` — and open the file picker instead of removing the file. Files would become un-removable after selection.

Tony flagged this in the build report as "worth a quick visual check" but did not treat it as blocking. It is.

**Fix:** Elevate the file list above the overlay:

```diff
+.file-upload-list-item {
     display: flex;
     align-items: center;
     gap: 8px;
     width: 100%;
+    position: relative;
+    z-index: 2;
 }
```

Or restrict the overlay to only cover the activator button area (more complex — the simple z-index fix is preferred).

---

## Nitpicks — 1

### N1: Build report documentation inaccuracy (non-blocking)

The build report states: *"also adds definitions for all class names already referenced by the component."* This is inaccurate — `.file-upload-btn` and `.file-upload-error` are referenced in the razor file but have no definitions in the CSS file. Not a code defect (those would be scoping no-ops anyway), but the documentation is imprecise.

---

## Passing Checks

| Check | Result |
|-------|--------|
| `Hidden="false"` — correct Blazor bool binding (not HTML truthy coercion) | ✅ PASS |
| `Hidden` parameter name correct for MudBlazor 7.16.x | ✅ PASS |
| `.file-upload-zone` has `position: relative` | ✅ PASS |
| `.file-upload-input-overlay` geometry: absolute, top/left 0, 100%/100%, opacity 0 | ✅ PASS |
| Button click (ActivatorContent) still opens file picker | ✅ PASS — overlay IS the input, native click behavior is preserved |
| No double-open regression | ✅ PASS |
| `.file-upload-list-item` CSS (on direct `<div>`) scopes correctly | ✅ PASS |
| Scoped CSS non-penetration of MudBlazor child component classes — known/documented | ✅ PASS — Tony documented this; pre-existing non-issue |
| `Accept` attribute unchanged from #1705 | ✅ PASS — `.html,.png,.jpg,.jpeg,.webp,.pdf,.md,.json,.txt` |
| `FileStorageService.cs` `AllowedTypes` unchanged | ✅ PASS |
| No other files modified | ✅ PASS |
| No JS interop added | ✅ PASS |

---

## What to Fix (for Tony)

Two changes required:

**Fix 1 (Critical) — `FileUploadZone.razor.css`**

Replace the `.file-upload-input-overlay` rule with a `::deep` version so it penetrates into MudFileUpload's render tree:

```diff
-.file-upload-input-overlay {
+.file-upload-zone ::deep .file-upload-input-overlay {
     position: absolute;
     top: 0;
     left: 0;
     width: 100%;
     height: 100%;
     opacity: 0;
     cursor: pointer;
     z-index: 1;
 }
```

**Fix 2 (Important) — `FileUploadZone.razor.css`**

Add `position: relative` and `z-index: 2` to `.file-upload-list-item` so remove buttons are stacked above the overlay:

```diff
 .file-upload-list-item {
     display: flex;
     align-items: center;
     gap: 8px;
     width: 100%;
+    position: relative;
+    z-index: 2;
 }
```

Both fixes are CSS-only. No Razor changes, no logic changes, no build structure changes.

---

## Positive Observations

- **`Hidden="false"` approach is sound.** Using MudBlazor's built-in mechanism over JS interop is the right call — less surface area, no wwwroot changes, no App.razor changes. Good instinct.
- **CSS geometry is correct.** The overlay dimensions and anchor point are exactly right; only the delivery mechanism needs fixing.
- **Scope is tight.** Exactly two files, no scope creep. Build report is thorough.
- **Tony correctly identified the scoped CSS limitation for child-component classes.** That analysis was accurate; it just didn't extend to `.file-upload-input-overlay` being the same situation.
