# CC Review Brief — ADO #1962

## Task
Adversarial review of the DownloadSpec fix in SubmissionDetail.razor.
Commit: d0f6850 — fix(ADO#1962): use JS interop for spec download to bypass Blazor navigation interceptor

## File to Review
nexus/src/FortressNexus.Web/Components/Pages/SubmissionDetail.razor

## What Changed (per build report)
1. Added `@inject IJSRuntime JS` to inject section
2. Changed `DownloadSpec` from `void` to `async Task`, replaced `Nav.NavigateTo` with `await JS.InvokeVoidAsync("open", url, "_self")`
3. Updated all 3 MudMenuItem OnClick handlers to `async () => await DownloadSpec(...)`

## Checks to perform — be adversarial

1. VERIFY `@inject IJSRuntime JS` is present in the inject section (top of file, with the other @inject lines)
2. VERIFY `DownloadSpec` method signature is `private async Task DownloadSpec(string format)` — NOT void
3. VERIFY the method body calls `await JS.InvokeVoidAsync("open", url, "_self")` — exact parameter check: "open", url, "_self"
4. VERIFY the URL construction: `$"/nexus/{Id}/export?format={format}"` — matches the SubmissionExportController route
5. VERIFY all 3 MudMenuItem OnClick handlers: `@(async () => await DownloadSpec("md"))`, `@(async () => await DownloadSpec("docx"))`, `@(async () => await DownloadSpec("pdf"))`
6. CHECK: does DownloadSpec guard against null `_submission` or `_activeSpec`? (It should early-return if either is null)
7. CHECK for any remaining `Nav.NavigateTo` calls that might be for downloads (non-download navigations are fine)
8. LOOK for `window.open` vs just `"open"` — JS interop uses the short form `"open"` which maps to `window.open`. Confirm this is correct Blazor JS interop usage.
9. CHECK: the `_self` target means the download will happen in the same tab. For Content-Disposition: attachment responses, this is correct — the browser handles the download without navigating away. Confirm this is intentional and correct.

## Pass criteria
- All 5 mechanical checks pass
- No logic errors in DownloadSpec
- No remaining Nav.NavigateTo for download paths
- Null guard present

## Report format
List each check as PASS or FAIL with evidence from the code.
