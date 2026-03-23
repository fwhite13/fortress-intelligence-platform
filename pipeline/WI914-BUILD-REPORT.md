# Build Report: WI#914 — FIRM VPBot Join Error Fix

**Agent:** Tony Stark
**Date:** 2026-03-20
**Priority:** HIGH
**WI:** 914

---

## Summary

Single-file fix in `Meetings.razor` — replaced bare `HttpClient` injection (which had no `BaseAddress`) with `IHttpClientFactory` + `Navigation.ToAbsoluteUri()` to produce an absolute URI at runtime. Resolves the "An invalid request URI was provided" error when VPBot attempts to join a meeting.

---

## CC Invocation

```bash
cd ~/projects/fip
cat /home/fredw/.openclaw/workspace/ai/claw-command/pipeline/WI914-BUILD-BRIEF.md | claude --model sonnet --dangerously-skip-permissions -p
```

---

## Root Cause

`Meetings.razor` was injecting `HttpClient Http` directly. The bare `HttpClient` instance has no `BaseAddress` set, so a relative URI like `/api/meetings/join` throws:
> "An invalid request URI was provided. Either the request URI must be an absolute URI or BaseAddress must be set."

---

## Changes Made

### File: `firm/src/FortressIntelligenceRM.Web/Components/Pages/Meetings.razor`

**Line 3 — inject swap:**
```diff
- @inject HttpClient Http
+ @inject IHttpClientFactory HttpClientFactory
```

**`JoinMeetingWithParams` method (~line 213) — PostAsJsonAsync call:**
```diff
- var response = await Http.PostAsJsonAsync("/api/meetings/join",
-     new { meetingUrl = meetingUrl, title = meetingTitle });
+ var http = HttpClientFactory.CreateClient();
+ var uri = Navigation.ToAbsoluteUri("/api/meetings/join");
+ var response = await http.PostAsJsonAsync(uri, new { meetingUrl = meetingUrl, title = meetingTitle });
```

No other files modified.

---

## Build Results

```
Build succeeded.
  1 Warning(s)   ← pre-existing CS0414 (_joining field unused) — NOT introduced by this change
  0 Error(s)
Time Elapsed 00:00:03.69
```

---

## Commit

```
486828f WI914: FIRM Meetings.razor — fix HttpClient BaseAddress (use IHttpClientFactory + absolute URI)
```

---

## Self-Review Checklist

- [x] `@inject HttpClient Http` removed from `Meetings.razor`
- [x] `@inject IHttpClientFactory HttpClientFactory` added
- [x] `PostAsJsonAsync` call uses `Navigation.ToAbsoluteUri(...)` for absolute URI
- [x] Zero new .NET build errors (0 errors, 1 pre-existing warning)
- [x] No files outside `firm/src/FortressIntelligenceRM.Web/`

---

## Notes

- `IHttpClientFactory` is already registered via `builder.Services.AddHttpClient()` in `Program.cs` — no `Program.cs` changes needed
- `NavigationManager Navigation` was already injected at line 4 — no new inject required for it
- The pre-existing `CS0414` warning on `_joining` is unrelated to this fix and was present before

---

**Status:** ✅ BUILD COMPLETE — Ready for Clint's review
