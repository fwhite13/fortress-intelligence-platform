# CC Brief: ADO4554 R4 — Security Fast-Follow Fixes

## Working Directory
`/home/fredw/projects/fip/fait/src/FortressAI.Web/`

## Context
Three targeted security fixes identified by CodeSec scan. All are low-effort, surgical changes. Do NOT touch anything else.

---

## Fix 1 — ArtifactSidebarPanel.razor: Remove `allow-popups-to-escape-sandbox`

**File:** `Components/Chat/ArtifactSidebarPanel.razor`

Find the iframe element with sandbox attribute that includes `allow-popups-to-escape-sandbox` and remove that attribute from the list.

Change:
```
sandbox="allow-scripts allow-popups allow-forms allow-popups-to-escape-sandbox"
```
To:
```
sandbox="allow-scripts allow-popups allow-forms"
```

Do NOT change anything else in this file.

---

## Fix 2 — Program.cs: Add startup validation for PREVIEW_TOKEN_SECRET

**File:** `Program.cs`

After the line `var app = builder.Build();` (or the equivalent `builder.Build()` call), but BEFORE `app.Run()`, add this block:

```csharp
// Fail fast on missing PREVIEW_TOKEN_SECRET — do not wait for first request
var previewSecret = app.Configuration["PREVIEW_TOKEN_SECRET"];
if (string.IsNullOrWhiteSpace(previewSecret))
    throw new InvalidOperationException(
        "PREVIEW_TOKEN_SECRET must be configured before startup. Set this environment variable in the ECS task definition.");
```

Place it logically near other startup validation or configuration checks. Do NOT change anything else.

---

## Fix 3 — ArtifactPreviewController.cs: Add X-Content-Type-Options: nosniff

**File:** `Controllers/ArtifactPreviewController.cs`

In the response setup block where `Response.ContentType` is set (and near the `Cache-Control` header set), add:

```csharp
Response.Headers["X-Content-Type-Options"] = "nosniff";
```

Place it immediately after or near the existing `Response.ContentType = ...` line. Do NOT change any logic, only add this one header line.

---

## Constraints
- Touch ONLY the three files listed above
- Do NOT refactor, reformat, or change any other logic
- Do NOT add any new dependencies or using statements (X-Content-Type-Options assignment needs no new using)
- Each fix is surgical — minimum diff

## Acceptance Criteria
- [ ] `ArtifactSidebarPanel.razor`: `allow-popups-to-escape-sandbox` is gone from sandbox attribute
- [ ] `Program.cs`: startup throws `InvalidOperationException` if `PREVIEW_TOKEN_SECRET` is missing/whitespace
- [ ] `ArtifactPreviewController.cs`: response includes `X-Content-Type-Options: nosniff` header

## After Making Changes
Run: `dotnet build /home/fredw/projects/fip/fait/src/FortressAI.Web/FortressAI.Web.csproj`
Report: 0 new errors (existing warnings OK).
