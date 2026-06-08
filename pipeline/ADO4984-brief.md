# CC Task Brief: ADO#4984 — Add [AllowAnonymous] to Preview Action

## Context
`Program.cs` sets `FallbackPolicy = DefaultPolicy` (require auth on all endpoints). `ArtifactPreviewController.Preview` has neither `[Authorize]` nor `[AllowAnonymous]`, so the ASP.NET Core auth middleware rejects requests with 401 before the action runs. The controller's own HMAC token validation logic never executes. Fix: add `[AllowAnonymous]` to the `Preview` action only.

## File to Modify
`fait/src/FortressAI.Web/Controllers/ArtifactPreviewController.cs`

## Exact Change Required
Find this block (around line 41):
```csharp
    [HttpGet("{id:guid}/preview")]
    public async Task<IActionResult> Preview(
```

Replace it with:
```csharp
    [HttpGet("{id:guid}/preview")]
    [AllowAnonymous]
    public async Task<IActionResult> Preview(
```

## Using Statement
`Microsoft.AspNetCore.Authorization` is already imported at line 8. Do NOT add it again.

## Constraints — CRITICAL
- DO NOT change `[Authorize]` on `ConvertPptx` (line ~125)
- DO NOT change `[Authorize]` on `PreviewStatus` (line ~173)
- DO NOT modify any other method in the file
- DO NOT change any other file
- This is a one-line addition only

## Verification After Change
After making the change, output:
1. The diff of changes made
2. Confirm `[Authorize]` still exists on ConvertPptx and PreviewStatus
3. Confirm `[AllowAnonymous]` now exists on Preview
