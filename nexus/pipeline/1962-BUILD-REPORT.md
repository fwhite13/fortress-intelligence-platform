# BUILD REPORT — ADO #1962
**Agent:** Tony Stark — BUILD cycle 1
**Date:** 2026-04-15

## Summary
Fixed `DownloadSpec` in `SubmissionDetail.razor` to use JS interop (`window.open`) instead of `Nav.NavigateTo(forceLoad: true)`, allowing the browser to correctly handle `Content-Disposition: attachment` download responses without Blazor navigation interception.

## Changes

**File:** `nexus/src/FortressNexus.Web/Components/Pages/SubmissionDetail.razor`

1. Added `@inject IJSRuntime JS` to the inject section
2. Changed `DownloadSpec` from `void` to `async Task`, replacing `Nav.NavigateTo` with `await JS.InvokeVoidAsync("open", url, "_self")`
3. Updated all three `MudMenuItem OnClick` handlers to `async () => await DownloadSpec(...)`

## Build Result
```
1 Warning(s)
0 Error(s)
Time Elapsed 00:00:04.41
```
Pre-existing warning (CS8601) — not introduced by this change.

## Commit
**Hash:** `d0f6850`
**Message:** `fix(ADO#1962): use JS interop for spec download to bypass Blazor navigation interceptor`
**Pushed:** `origin/main`
