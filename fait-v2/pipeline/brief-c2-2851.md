# CC Brief — ADO#2851 BUILD cycle 2: Two targeted fixes to TopicList.razor

## Task
Apply exactly two code fixes to `src/FortressAI.V2.Web/Components/Memory/TopicList.razor`.
Do NOT modify any other files.

## Working directory
`/home/fredw/projects/fip/fait-v2`

---

## Fix 1 — C1: Path traversal sanitization in ConfirmCreate (CRITICAL)

In the `@code` block, `ConfirmCreate` method, replace:

```csharp
var slug = _newSlug.Trim().ToLower().Replace(" ", "-");
```

With:

```csharp
var slug = System.Text.RegularExpressions.Regex.Replace(
    _newSlug.Trim().ToLower().Replace(" ", "-"),
    @"[^a-z0-9\-_]", "");
if (string.IsNullOrEmpty(slug)) return;
```

The `if (string.IsNullOrEmpty(slug)) return;` guard must come IMMEDIATELY after the slug assignment, before the `UpsertTopicAsync` call.

---

## Fix 2 — I1: Re-render storm guard in OnParametersSetAsync (IMPORTANT)

In the `@code` block:

1. Add a private field after the existing private fields:
```csharp
private string _lastLoadedUserId = "";
```

2. Replace the existing `OnParametersSetAsync` method:
```csharp
protected override async Task OnParametersSetAsync()
{
    if (!string.IsNullOrEmpty(UserId))
        await LoadTopics();
}
```
With:
```csharp
protected override async Task OnParametersSetAsync()
{
    if (!string.IsNullOrEmpty(UserId) && UserId != _lastLoadedUserId)
    {
        _lastLoadedUserId = UserId;
        await LoadTopics();
    }
}
```

---

## Constraints
- Do NOT touch any other methods, markup, or files
- Preserve all existing code exactly as-is except the two targeted changes above
- The `_lastLoadedUserId` field should be placed with the other private fields (after `private string _newSlug = "";`)

---

## After making the changes:
1. Run: `cd /home/fredw/projects/fip/fait-v2 && dotnet build src/FortressAI.V2.Web/FortressAI.V2.Web.csproj 2>&1`
2. Report: build output (0 errors, 0 warnings expected)
3. Report: the exact final content of the two changed sections so I can verify correctness
