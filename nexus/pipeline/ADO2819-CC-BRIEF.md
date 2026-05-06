# CC Brief — ADO#2819: NexusReview.razor Upgrade

## Task
Upgrade `/home/fredw/projects/fip/nexus/src/FortressNexus.Web/Components/Pages/NexusReview.razor`

## What to change (in order)

### 1. Role-based access guard (LoadAsync)

After loading `_submission`, check access:
- IsAdmin (NexusAdmin) → allow
- IsReviewer (NexusReviewer) → allow
- Caller UPN == `_submission.SubmittedBy` → allow (submitter can read their own)
- Otherwise → call `NavigationManager.NavigateTo("/nexus")` and show snackbar "Access denied." then return

```
inject NavigationManager NavigationManager
```

Use:
```csharp
var upn = await UserContextService.GetUpnAsync();
var isAdmin = await UserContextService.IsAdminAsync();
var isEditor = await UserContextService.IsNexusEditorAsync();
bool isSubmitter = _submission.SubmittedBy == upn;

if (!isAdmin && !isEditor && !isSubmitter)
{
    Snackbar.Add("Access denied.", Severity.Warning);
    NavigationManager.NavigateTo("/nexus");
    return;
}
```

Store `_isAdmin = isAdmin` and `_upn = upn` as fields for use later.

### 2. Status chip in header

In the header `<div class="nexus-review-header">`, after the title text and the Approved chip (keep the existing `@if (_specDoc.IsApproved)` chip), add a status chip BEFORE the toggle icon button:

```razor
<MudChip T="string" Color="Color.Info" Size="Size.Small" Class="nexus-review-status-chip">@_submission.Status.ToString()</MudChip>
```

### 3. Section-by-section inline editing

Replace the existing right panel content (the `@if (_specDoc.IsApproved)` block that shows the read-only markdown OR the `MudTextField Lines="30"` editor) with section-based editing.

**New fields needed:**
```csharp
private bool _isAdmin;
private string _upn = "";
private List<SpecSection> _sections = new();
private HashSet<int> _editingSections = new();
private bool _hasSections;

private record SpecSection(int Index, string Heading, string Body);
```

**ParseSections method:**
```csharp
private static List<SpecSection> ParseSections(string content)
{
    var sections = new List<SpecSection>();
    if (string.IsNullOrWhiteSpace(content)) return sections;

    // Split on lines that start with ## (h2 headings)
    var lines = content.Split('\n');
    var currentHeading = "";
    var currentBody = new System.Text.StringBuilder();
    int index = 0;

    foreach (var line in lines)
    {
        if (line.StartsWith("## "))
        {
            if (index > 0 || currentHeading.Length > 0)
            {
                sections.Add(new SpecSection(index, currentHeading, currentBody.ToString().Trim()));
                index++;
            }
            else if (currentBody.Length > 0)
            {
                // preamble before first heading
                sections.Add(new SpecSection(index, "", currentBody.ToString().Trim()));
                index++;
            }
            currentHeading = line.Substring(3).Trim();
            currentBody.Clear();
        }
        else
        {
            currentBody.AppendLine(line);
        }
    }

    // Last section
    if (currentHeading.Length > 0 || currentBody.Length > 0)
    {
        sections.Add(new SpecSection(index, currentHeading, currentBody.ToString().Trim()));
    }

    return sections;
}
```

Actually, use this cleaner approach — split by `## ` heading boundaries using regex:

```csharp
private static List<SpecSection> ParseSections(string content)
{
    var sections = new List<SpecSection>();
    if (string.IsNullOrWhiteSpace(content)) return sections;

    var pattern = @"(?=^## |\A)";
    var parts = System.Text.RegularExpressions.Regex.Split(content, pattern, 
        System.Text.RegularExpressions.RegexOptions.Multiline);
    
    int index = 0;
    foreach (var part in parts)
    {
        var trimmed = part.Trim();
        if (string.IsNullOrEmpty(trimmed)) continue;
        
        if (trimmed.StartsWith("## "))
        {
            var firstNewline = trimmed.IndexOf('\n');
            if (firstNewline < 0)
            {
                sections.Add(new SpecSection(index++, trimmed.Substring(3).Trim(), ""));
            }
            else
            {
                var heading = trimmed.Substring(3, firstNewline - 3).Trim();
                var body = trimmed.Substring(firstNewline + 1).Trim();
                sections.Add(new SpecSection(index++, heading, body));
            }
        }
        else
        {
            // preamble before any ## heading
            sections.Add(new SpecSection(index++, "", trimmed));
        }
    }
    return sections;
}
```

After loading `_editedContent` in `LoadAsync`, call:
```csharp
_sections = ParseSections(_editedContent);
_hasSections = _sections.Any(s => !string.IsNullOrEmpty(s.Heading));
```

**ReassembleSections method:**
```csharp
private string ReassembleSections()
{
    var sb = new System.Text.StringBuilder();
    foreach (var s in _sections)
    {
        if (!string.IsNullOrEmpty(s.Heading))
        {
            sb.AppendLine($"## {s.Heading}");
            sb.AppendLine();
        }
        if (!string.IsNullOrEmpty(s.Body))
        {
            sb.AppendLine(s.Body);
            sb.AppendLine();
        }
    }
    return sb.ToString().TrimEnd();
}
```

**Per-section save on blur:**
```csharp
private async Task SaveSectionAsync(int sectionIndex)
{
    _editingSection.Remove(sectionIndex);
    if (_specDoc is null) return;
    _isSaving = true;
    StateHasChanged();
    try
    {
        _editedContent = ReassembleSections();
        await SpecService.SaveDraftAsync(_specDoc.Id, _editedContent, _upn);
        _lastSavedAt = DateTime.UtcNow;
    }
    catch (Exception ex)
    {
        Snackbar.Add($"Save failed: {ex.Message}", Severity.Error);
    }
    finally
    {
        _isSaving = false;
        StateHasChanged();
    }
}
```

**Section body update helper:**
```csharp
private void UpdateSectionBody(int index, string newBody)
{
    var s = _sections[index];
    _sections[index] = s with { Body = newBody };
}
```

**Right panel markup** — replace the existing `@if (_specDoc.IsApproved)` / else block with:

```razor
@if (_specDoc.IsApproved)
{
    <MudPaper Elevation="1" Class="nexus-review-original-viewer">
        <div class="nexus-spec-content">
            @RenderMarkdown(_specDoc.EditedContent ?? _specDoc.Content)
        </div>
    </MudPaper>
}
else if (_hasSections)
{
    @* Section-by-section editor *@
    foreach (var section in _sections)
    {
        var idx = section.Index;
        <div class="nexus-review-section">
            <div class="nexus-review-section-header">
                @if (!string.IsNullOrEmpty(section.Heading))
                {
                    <MudText Typo="Typo.h6" Class="nexus-review-section-title">@section.Heading</MudText>
                }
                @if (!_specDoc.IsApproved)
                {
                    <MudIconButton Icon="@Icons.Material.Filled.Edit"
                                   Size="Size.Small"
                                   Color="Color.Primary"
                                   Class="nexus-review-section-edit-btn"
                                   OnClick="@(() => _editingSections.Add(idx))" />
                }
            </div>
            @if (_editingSections.Contains(idx))
            {
                <MudTextField Value="@section.Body"
                              ValueChanged="@((string v) => UpdateSectionBody(idx, v))"
                              Lines="8"
                              Variant="Variant.Outlined"
                              Class="nexus-review-section-editor"
                              @onblur="@(async () => await SaveSectionAsync(idx))" />
            }
            else
            {
                <div class="nexus-spec-content nexus-review-section-body">
                    @RenderMarkdown(section.Body)
                </div>
            }
        </div>
    }
}
else
{
    @* Fallback: no ## headings — full content editor *@
    <MudTextField @bind-Value="_editedContent"
                  Lines="30"
                  Variant="Variant.Outlined"
                  Class="nexus-review-editor"
                  Disabled="@_specDoc.IsApproved" />
}
```

### 4. HandleSaveDraft — use stored _upn

The existing `HandleSaveDraft` calls `UserContextService.GetUpnAsync()` again. Change it to use `_upn` (stored during LoadAsync). Also when `_hasSections` is true, reassemble before saving:

```csharp
private async Task HandleSaveDraft()
{
    if (_specDoc is null) return;
    _isSaving = true;
    StateHasChanged();
    try
    {
        if (_hasSections)
            _editedContent = ReassembleSections();
        await SpecService.SaveDraftAsync(_specDoc.Id, _editedContent, _upn);
        _lastSavedAt = DateTime.UtcNow;
        Snackbar.Add("Draft saved.", Severity.Success);
    }
    catch (Exception ex)
    {
        Snackbar.Add($"Save failed: {ex.Message}", Severity.Error);
    }
    finally
    {
        _isSaving = false;
        StateHasChanged();
    }
}
```

### 5. HandleApprove — use stored _upn

Same change: replace `var upn = await UserContextService.GetUpnAsync();` with just `_upn`.

### 6. NavigationManager injection note

The `GetByIdAsync` interface does NOT have an `isAdmin` parameter — no change needed there. The access guard is entirely at the page level. This is correct — load first, then check access.

---

## Key constraints

- ALL CSS must use class names only — no inline styles, no MudBlazor default style props
- Do NOT add `NexusReviewer` to the Approve button — it remains `Roles="@NexusRoles.Admin"` only
- Keep `@attribute [Authorize]` — the role guard in LoadAsync is an additional runtime check
- The `_editingSections` field is `HashSet<int>` — use `.Add()` and `.Remove()`, not toggle
- Do not change the left panel (AI Original) at all
- The `SpecSection` record must be defined inside `@code { }` block
- Add `@inject NavigationManager NavigationManager` at top of file with other injects

---

## File to edit

`/home/fredw/projects/fip/nexus/src/FortressNexus.Web/Components/Pages/NexusReview.razor`

Produce the complete updated file. Do not leave out any existing functionality.

After writing the file, run:
```bash
cd /home/fredw/projects/fip/nexus && dotnet build src/FortressNexus.Web/FortressNexus.Web.csproj --no-restore -v quiet 2>&1 | tail -20
```

Report: build success/failure and what was changed.
