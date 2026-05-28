# Security Brief: ADO4568 — Remove Office Online iframe (Cleanup)

## Task
Perform a security-focused scan of the changed files for ADO4568. This is a deletion/cleanup WI — no new functionality. Risk level: Low.

## Commits
- `360ed18d` — Office Online removal (primary)
- `6a45249d` — fix pass (dead inject removed, placeholder text)

## Changed Files to Read
1. `src/FortressAI.Web/Components/Chat/ArtifactSidebarPanel.razor`
2. `src/FortressAI.Web/Components/Chat/ArtifactPreviewPanel.razor`
3. `src/FortressAI.Web/Services/IWorkspaceFileService.cs`
4. `src/FortressAI.Web/Services/WorkspaceFileService.cs`
5. `src/FortressAI.Web/Program.cs`

## Security Checks Required

### 1. No new injection vectors
The new placeholder UI in `ArtifactSidebarPanel.razor` (the IsOfficeDocType branch) must not render any user-controlled content. Check:
- Is the placeholder text hardcoded or user-supplied?
- Is there any `@((MarkupString)...)` or `@Html.Raw(...)` or dynamic HTML rendering?
- Is the `IsOfficeDocType()` call operating on user-controlled input in a dangerous way?

### 2. Removed code was the only consumer of GetFilePreviewUrlAsync
`GetFilePreviewUrlAsync` was removed from `IWorkspaceFileService` and `WorkspaceFileService`. Check:
- Does any other code in the workspace still call `GetFilePreviewUrlAsync`? Search for all call sites.
- Could any caller have been bypassing CloudFront URL signing for sensitive content access?

### 3. IsOfficeDocType() helper safety
The `IsOfficeDocType()` helper was added as an extension/MIME matching method. Verify:
- It performs only string/extension comparison — no dynamic execution, no eval, no reflection
- It takes a file path/name string and returns bool — no network calls, no IO

### 4. No secrets or credentials in removed/remaining code
Check all 5 changed files for:
- Hardcoded CloudFront private key refs, ARNs, key pair IDs
- Hardcoded Office Online URLs (login.microsoftonline.com, view.officeapps.live.com patterns)
- Any signing secrets or tokens embedded in code
- Any debug/test credentials left behind

### 5. Program.cs after ICloudFrontSignedUrlService removal
The `AddSingleton<ICloudFrontSignedUrlService>` registration was removed. Verify:
- No dangling DI registrations that reference a now-missing type
- No orphaned `IConfiguration` key bindings that reference a missing section
- The app would still start cleanly

## Instructions
Read all 5 files listed above. Also search for:
- Any remaining references to `GetFilePreviewUrlAsync` in the codebase (grep-style search)
- Any remaining references to `ICloudFrontSignedUrlService` or `CloudFrontSignedUrlService` in the codebase
- Any remaining references to Office Online URLs (officeapps.live.com)

For each security check, state: CLEAR / WARN / BLOCK with evidence.

## Output Format
```
## Security Report: ADO4568

### Check 1: No new injection vectors — [CLEAR/WARN/BLOCK]
[evidence]

### Check 2: GetFilePreviewUrlAsync — only consumer removed — [CLEAR/WARN/BLOCK]
[evidence]

### Check 3: IsOfficeDocType() helper safety — [CLEAR/WARN/BLOCK]
[evidence]

### Check 4: No secrets or credentials — [CLEAR/WARN/BLOCK]
[evidence]

### Check 5: Program.cs clean after removal — [CLEAR/WARN/BLOCK]
[evidence]

### Overall Verdict: CLEAR / WARN / BLOCK
[summary]
```
