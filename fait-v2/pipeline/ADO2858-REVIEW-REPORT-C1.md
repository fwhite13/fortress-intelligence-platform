# Review Report — ADO#2858 (Workspace Explorer UI)

**Review Cycle:** 1
**Commit:** `c3c242d`
**Reviewer:** Hawkeye (Clint Barton)
**Date:** 2026-05-07

---

## Verdict: NEEDS-CHANGES

13 of 15 checklist items pass. Two issues block PASS — one must-fix (hardcoded CSS values), one should-fix (off-by-one in folder file count badge).

---

## CC Review Summary

CC invoked via: `cat ADO2858-REVIEW-BRIEF.md | claude --model sonnet --print --dangerously-skip-permissions`

CC identified both issues correctly. Manually verified in source files:
- CSS violations confirmed at `app.css` lines 1139, 1142, 1151, 1152
- Off-by-one confirmed at `WorkspaceService.cs` line 35

No false positives dismissed — both findings are real.

---

## Spec Compliance Check

**§ Changed files — all present and confirmed:**
- `Services/IWorkspaceService.cs` ✅
- `Services/WorkspaceService.cs` ✅
- `Components/Pages/Workspace.razor` ✅
- `wwwroot/css/app.css` ✅
- `Program.cs` ✅

**Out of scope:** No out-of-scope changes detected ✅

---

## Consistency Audit

| Check | Result |
|-------|--------|
| `IWorkspaceService` registered as Scoped in `Program.cs:153` | ✅ `AddScoped<IWorkspaceService, WorkspaceService>()` |
| `s3Key.StartsWith("workspaces/{userId}/")` in both `GetDownloadUrlAsync` and `DeleteFileAsync` | ✅ Lines 68, 83 |
| `userId` from Entra OID claim (`"oid"` / objectidentifier), not user input | ✅ `Workspace.razor` lines 117–119 |
| Pre-signed URL expiry ≤ 60 min | ✅ 15 min (`WorkspaceService.cs:75`) |
| S3 bucket from config (`AWS:WorkspaceBucket`) | ✅ Line 11 — see Nitpick N1 |
| `IAmazonS3` injected (not shell CLI) | ✅ |
| Folder marker excluded in `ListFilesAsync` (`.Where(o => o.Key != prefix)`) | ✅ Line 52 |
| iframe sandbox attribute present | ✅ `sandbox="allow-scripts"` (Workspace.razor:81) |
| Delete confirmation dialog | ✅ `MudDialog` with Cancel/Confirm (lines 87–96) |
| Search uses computed property `FilteredFiles` (no render-loop mutation) | ✅ Lines 109–112 |
| Empty state shown when no files | ✅ Lines 33–37 |

---

## Critical Issues — 0

No critical issues found.

---

## Important Issues — 2

### I1: Hardcoded font-size / gap values in `app.css` (checklist item #14)

**File:** `wwwroot/css/app.css`
**Lines:** 1139, 1142, 1151, 1152
**Category:** CSS token violation

The existing codebase uses CSS custom properties for all sizing. Four workspace selectors use raw values instead:

```css
/* Line 1139 */
.workspace-sidebar__title { ... font-size: 0.7rem; ... }

/* Line 1142 */
.workspace-folder-count { ... font-size: 0.7rem; ... }

/* Line 1151 */
.workspace-file-meta { font-size: 0.75rem; ... }

/* Line 1152 */
.workspace-file-actions { display: flex; gap: 2px; ... }
```

**Fix:** Replace with CSS variables already defined in the design system:

```diff
- .workspace-sidebar__title { font-weight: 600; color: var(--color-text-secondary); font-size: 0.7rem; text-transform: uppercase; margin-bottom: var(--space-2); }
+ .workspace-sidebar__title { font-weight: 600; color: var(--color-text-secondary); font-size: var(--text-xs); text-transform: uppercase; margin-bottom: var(--space-2); }

- .workspace-folder-count { margin-left: auto; font-size: 0.7rem; color: var(--color-text-secondary); }
+ .workspace-folder-count { margin-left: auto; font-size: var(--text-xs); color: var(--color-text-secondary); }

- .workspace-file-meta { font-size: 0.75rem; color: var(--color-text-secondary); }
+ .workspace-file-meta { font-size: var(--text-sm); color: var(--color-text-secondary); }

- .workspace-file-actions { display: flex; gap: 2px; flex-shrink: 0; }
+ .workspace-file-actions { display: flex; gap: var(--space-1); flex-shrink: 0; }
```

> Verify `--text-xs`, `--text-sm`, and `--space-1` are defined in `fip-tokens.css` / `app.css` root vars. Use the closest matching token if names differ.

---

### I2: Off-by-one in `GetFolderStructureAsync` folder file count badge

**File:** `Services/WorkspaceService.cs`
**Line:** 35
**Category:** Logic error

`ListFilesAsync` (line 52) correctly excludes the folder marker object with `.Where(o => o.Key != prefix)`. But `GetFolderStructureAsync` (line 35) uses the raw count:

```csharp
// Line 35 — WRONG: includes folder marker object
FileCount = response.S3Objects.Count,
```

When a folder marker key exists (`workspaces/{userId}/{folder}/`), the sidebar badge will display N+1 files.

**Fix:**
```diff
- FileCount = response.S3Objects.Count,
+ FileCount = response.S3Objects.Count(o => o.Key != prefix),
```

Note: `prefix` is already in scope at line 24.

---

## Nitpicks — 1

**N1:** `WorkspaceService.cs:11` — hardcoded fallback bucket name:
```csharp
private string Bucket => _config["AWS:WorkspaceBucket"] ?? "fortress-user-workspaces";
```
The fallback `"fortress-user-workspaces"` will silently mask a missing config entry in a new environment. Consider throwing instead:
```csharp
private string Bucket => _config["AWS:WorkspaceBucket"] 
    ?? throw new InvalidOperationException("AWS:WorkspaceBucket is not configured.");
```
Not blocking.

---

## Positive Observations

- Security model is solid: `userId` from Entra OID (not input), prefix validation on both download and delete, 15-min pre-signed URL TTL — all correct.
- `FilteredFiles` as a computed property is the right pattern — no mutation in the render loop.
- MudDialog confirmation before delete is properly wired with a null-guard in `DeleteConfirmed`.
- `ListFilesAsync` already has the correct folder-marker exclusion (the I2 issue only affects the count badge, not the file list itself).
- `IWorkspaceService` correctly registered as `Scoped`.

---

## What Tony Needs to Fix

1. **`app.css` lines 1139, 1142, 1151, 1152** — Replace `0.7rem`, `0.75rem`, and `2px` with CSS custom properties. Check which `--text-*` and `--space-*` tokens are defined and use the closest match.

2. **`WorkspaceService.cs:35`** — Change `response.S3Objects.Count` to `response.S3Objects.Count(o => o.Key != prefix)`.

Both fixes are surgical and non-breaking. No new files required.

---

_Hawkeye out._
