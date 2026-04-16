# Review Report — ADO #1962

**Agent:** Hawkeye — REVIEW cycle 1
**Date:** 2026-04-15
**Commit:** `d0f6850` — `fix(ADO#1962): use JS interop for spec download to bypass Blazor navigation interceptor`

---

### Verdict: ✅ PASS

---

### Spec Compliance Check

**Brief:** ADO #1962 — DownloadSpec fix: replace `Nav.NavigateTo(forceLoad: true)` with JS interop `window.open` to bypass Blazor navigation interceptor.

**Files modified (per git diff):**
- `nexus/src/FortressNexus.Web/Components/Pages/SubmissionDetail.razor` — ✅ modified as specified

**Out of scope:**
- `SubmissionExportController.cs` — ✅ NOT modified (confirmed: git show d0f6850 returned empty for that file)
- No other files touched beyond the pipeline brief doc

**Spec compliance verdict:** ✅ COMPLIANT

---

### Consistency Audit

**Downstream consumers checked:**
- Download menu `MudMenuItem` handlers → `DownloadSpec(string format)` → `JS.InvokeVoidAsync("open", url, "_self")` — full chain verified
- URL pattern `/nexus/{Id}/export?format={format}` — matches `SubmissionExportController` route
- `Nav.NavigateTo` remaining calls are for page navigation only (resume, review, post-delete redirect) — none are download paths

---

### Mechanical Checks (5/5 PASS)

| # | Check | Result |
|---|-------|--------|
| 1 | `@inject IJSRuntime JS` present | ✅ PASS — line 17 |
| 2 | `DownloadSpec` is `async Task` | ✅ PASS — `private async Task DownloadSpec(string format)` |
| 3 | Calls `JS.InvokeVoidAsync("open", url, "_self")` | ✅ PASS — exact parameters confirmed |
| 4 | `SubmissionExportController.cs` NOT modified | ✅ PASS — git diff confirms untouched |
| 5 | `dotnet build` → 0 errors | ✅ PASS — 1 pre-existing CS8601 warning (FileStorageService, not introduced by this commit) |

---

### CC Review Summary

CC reviewed all 9 checks. All passed cleanly. Notable:

- **Null guard** — `DownloadSpec` has explicit `if (_submission is null || _activeSpec is null) return;` guard. Double-protected since the download menu is itself gated on `_activeSpec is not null` in the template.
- **`"open"` vs `"window.open"`** — `JS.InvokeVoidAsync("open", ...)` is correct Blazor idiom; runtime resolves against `window`. No issue.
- **`_self` target** — correct for `Content-Disposition: attachment`. Browser intercepts the response as a file download without navigating the page away. `_blank` would have opened an unnecessary new tab.
- **No stray download-path `Nav.NavigateTo`** — three remaining `NavigateTo` calls are all page navigations (resume, review, post-delete). None are download paths.

No false positives, no dismissed findings. CC found zero issues.

---

### Critical Issues: 0
### Important Issues: 0
### Nitpicks: 0

---

### Positive Observations

- Clean, minimal diff — exactly the three changes needed, nothing extra
- Null guard is defensive and correct even with the template-level gate already in place
- The `window.open` + `_self` approach is idiomatic and avoids the new-tab UX noise that `_blank` would introduce

---

### Build

```
1 Warning(s)  ← pre-existing CS8601 in FileStorageService.cs, not introduced by this commit
0 Error(s)
Time Elapsed 00:00:04.50
```
