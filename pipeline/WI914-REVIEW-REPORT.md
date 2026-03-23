# Review Report: WI#914 — FIRM VPBot HttpClient Fix

**Reviewer:** Hawkeye (Clint Barton)
**Date:** 2026-03-20
**Cycle:** 1 of 2
**Commit:** `486828f`
**File Reviewed:** `firm/src/FortressIntelligenceRM.Web/Components/Pages/Meetings.razor`

---

## Verdict: ✅ PASS

---

## CC Invocation

```bash
cd ~/projects/fip && cat review-brief.md | claude --model sonnet -p
```

---

## P1 Checks — Build-Blocking

| # | Check | Result | Notes |
|---|-------|--------|-------|
| P1.1 | `@inject HttpClient Http` removed | ✅ PASS | Not present anywhere in the file |
| P1.2 | `@inject IHttpClientFactory HttpClientFactory` present | ✅ PASS | Line 3, inject block |
| P1.3 | `PostAsJsonAsync` uses absolute URI via `Navigation.ToAbsoluteUri()` | ✅ PASS | `var uri = Navigation.ToAbsoluteUri("/api/meetings/join")` — no bare relative string |
| P1.4 | `HttpClientFactory.CreateClient()` used (not shared instance) | ✅ PASS | `var http = HttpClientFactory.CreateClient()` — fresh client per call |
| P1.5 | No other `Http.*` calls remaining in file | ✅ PASS | Zero references to old `Http` field anywhere |

## P2 Checks — Scope

| # | Check | Result | Notes |
|---|-------|--------|-------|
| P2.1 | Only `firm/src/FortressIntelligenceRM.Web/` touched | ✅ PASS | Single-file change, confirmed per Build Report |

---

## Analysis

The fix is textbook-correct for Blazor Server:

- **`IHttpClientFactory`** is the right injection for components that need to make HTTP calls — it manages client lifetime and avoids socket exhaustion
- **`Navigation.ToAbsoluteUri("/api/meetings/join")`** produces an absolute URI (e.g. `https://localhost:7001/api/meetings/join`) at runtime, resolving the "no BaseAddress" trap
- **`HttpClientFactory.CreateClient()`** (no named client needed for a generic call to the app's own API) is appropriate here
- The download link at `Href="@($"/api/meetings/{context.Id}/transcript/download")"` is a plain anchor `<a>` tag rendered by Blazor — it does NOT use the old `Http` field and is unaffected by this change
- Pre-existing `CS0414` warning on `_joining` noted — not introduced by this fix, not in scope

No issues found. No NEEDS-CHANGES items.

---

## Summary

Tony's fix is clean, minimal, and correct. The `IHttpClientFactory` + `Navigation.ToAbsoluteUri()` pattern resolves the VPBot join error. Ready to advance.

---

**Next Stage:** APPROVE → DEPLOY
