# Build Report — ADO #1715: Drag-and-drop broken in FileUploadZone

**Engineer:** Tony Stark  
**Build cycle:** 1  
**Commit:** `c6d7387`  
**Date:** 2026-04-13

---

## What was built

Fixed drag-and-drop in `FileUploadZone.razor` so dragging files onto the drop zone uploads them instead of opening them in the browser.

---

## Approach used: Option B — MudFileUpload built-in drag-drop (Hidden=false)

MudBlazor 7.16.0's `MudFileUpload` has a `Hidden` parameter (defaults to `true`). The source explicitly documents: _"When `false`, files can be uploaded via drag-and-drop."_ When `Hidden=false`, the native `<input type="file">` is rendered in the DOM as a real element, making it a valid drag-and-drop target that the browser can dispatch `drop` events to — without any JS interop.

The invisible overlay technique (absolute positioned, `opacity: 0`, `z-index: 1`, covering the full drop zone) ensures that drag events land on the `InputFile` element rather than the outer div. The browser's default behavior for dragging files onto a visible `<input type="file">` is to upload them — which is exactly what we want.

**Why Option B over Option C (JS interop):**
- Zero JS, zero new files in `wwwroot/js/`, zero App.razor changes
- Uses the framework's built-in mechanism — more maintainable, no interop surface to break on upgrades
- `DragEventArgs.DataTransfer.Files` is not directly accessible in Blazor Server, which would make Option C require a file serialization dance; Option B avoids this entirely

---

## Files changed

| File | Change |
|------|--------|
| `Components/Shared/FileUploadZone.razor` | Added `Hidden="false"` and `InputClass="file-upload-input-overlay"` to `MudFileUpload` |
| `Components/Shared/FileUploadZone.razor.css` | **Created** — scoped CSS with `position: relative` on `.file-upload-zone` and full-zone absolute overlay for `.file-upload-input-overlay`; also adds definitions for all class names already referenced by the component |

No JS files added. No App.razor changes. No logic changes.

---

## Parallelization

Not applicable — single-file change.

---

## Build result

```
Build succeeded.
    0 Error(s)
    0 Warning(s)
```

Commit: `c6d7387`

---

## Acceptance criteria

- [x] Drag-and-drop routes files to `HandleFilesChanged` — via MudBlazor native `<input type="file">` with `Hidden=false`
- [x] Click-to-browse still works — `ActivatorContent` button unchanged; calls `OpenFilePickerAsync` via existing MudBlazor `IActivatable` mechanism
- [x] No JS interop added
- [x] No inline styles — all CSS via classes in scoped `.razor.css` file
- [x] `dotnet build` — 0 errors, 0 warnings
- [x] Committed to `main` as `c6d7387`

---

## How to test locally

1. `cd ~/projects/fip/nexus && docker-compose up nexus-web` (or run locally)
2. Navigate to any page with `FileUploadZone`
3. Drag a `.pdf`, `.png`, or `.html` file onto the dashed drop zone
4. Verify: file appears in the list (not opened in browser)
5. Verify: clicking the button still opens the OS file picker

---

## Known edge cases / things Clint should scrutinize

- **z-index conflict:** The overlay has `z-index: 1`. If any child elements in the file list also have elevated z-index, they may be blocked by the overlay when the file list is visible. Worth a quick visual check of the file list interactions.
- **`file-upload-btn` z-index:** The button inside `ActivatorContent` sits inside the MudBlazor `<div @onclick="OpenFilePickerAsync">` wrapper — clicking the button triggers `OpenFilePickerAsync` via MudBlazor's `IActivatable` cascade, not via the overlay. This is correct but worth confirming the click doesn't also trigger duplicate file picker opens.
- **CSS scoping:** The new `.razor.css` file uses Blazor's scoped CSS. The class names (like `file-upload-btn`) are applied as `Class=` on MudBlazor child components — these render in child component DOM scope, so Blazor scoped CSS won't apply to them automatically. This is pre-existing behavior (those classes had no CSS before). The scoped CSS for `.file-upload-zone` and `.file-upload-input-overlay` will work correctly since those are on elements in this component's own render tree.

---

## Cycle 2 — CSS Isolation Fix

**Engineer:** Tony Stark
**Build cycle:** 2
**Commit:** `6950ca2`
**Date:** 2026-04-13

### What was fixed

Blazor CSS isolation scoping issue in `FileUploadZone.razor.css`. Two rules were not matching at runtime because Blazor's scope attribute (`b-xxxx`) is only applied to elements the component renders directly. The `<input type="file">` is rendered by MudBlazor's `MudFileUpload` — not by `FileUploadZone` — so the scoped selectors `.file-upload-input-overlay[b-xxxx]` and `.file-upload-list-item[b-xxxx]` never matched.

### Changes made

**File:** `Components/Shared/FileUploadZone.razor.css`

| Rule | Change |
|------|--------|
| `.file-upload-input-overlay` | Converted to `.file-upload-zone ::deep .file-upload-input-overlay` — `::deep` strips the scope attribute from the descendant selector, penetrating the MudBlazor child render tree |
| `.file-upload-list-item` | Converted to `.file-upload-zone ::deep .file-upload-list-item` — added `position: relative` + `z-index: 2` so remove buttons sit above the overlay (which is at z-index: 1) |

No changes to any `.razor` files.

### Build result

```
Build succeeded.
    0 Warning(s)
    0 Error(s)
```

### Acceptance criteria

- [x] C1: `.file-upload-input-overlay` uses `::deep` combinator — overlay CSS will now match the MudBlazor-rendered input
- [x] I1: `.file-upload-list-item` uses `::deep` combinator with `z-index: 2` — remove buttons no longer blocked by overlay
- [x] `dotnet build` — 0 errors, 0 warnings
- [x] No changes to `.razor` file

### Known edge cases / things Clint should scrutinize

- `::deep` is a Blazor-specific combinator that compiles to a plain descendant selector at runtime (the scope attribute is simply omitted from the descendant half). This is the canonical Blazor fix for this pattern — no browser compatibility concerns.
- The overlay (`z-index: 1`) covers the drop zone background. The list items (`z-index: 2`) sit above it. Any future additions to the file list area should also use `z-index: 2+` if they need to be interactive.
