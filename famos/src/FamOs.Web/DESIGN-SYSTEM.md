# FAM OS Design System — Component Standards
## MANDATORY — Tony and Clint must enforce this on every PR

---

## Buttons

All buttons use CSS classes only. Never set Variant, Color, or Size inline on MudButton.

### Primary action (filled, prominent)
```razor
<MudButton Class="famos-btn-primary" OnClick="...">Label</MudButton>
```

### Secondary action (outlined, standard)
```razor
<MudButton Class="famos-btn-outline" OnClick="...">Label</MudButton>
```

### Small secondary (header toolbar, panel headers)
```razor
<MudButton Class="famos-btn-outline-sm" OnClick="...">Label</MudButton>
```

### Destructive (delete, close/lost)
```razor
<MudButton Class="famos-btn-danger" OnClick="...">Label</MudButton>
```

**❌ NEVER do this:**
```razor
<MudButton Variant="Variant.Filled" Color="Color.Primary" Size="Size.Small" ...>
<MudButton Variant="Variant.Outlined" Color="Color.Default" Size="Size.Medium" ...>
```
If it has inline Variant/Color/Size, Clint rejects the PR.

---

## Text Inputs / Search / Filter

All text inputs use CSS classes only. Width is always constrained by the container or a class.

### Standard input
```razor
<MudTextField Class="famos-input" @bind-Value="..." Placeholder="..." />
```

### Search (topbar)
```razor
<MudTextField Class="famos-input-search" @bind-Value="..." Placeholder="Search..." 
    Adornment="Adornment.Start" AdornmentIcon="@FamosIcons.Search" />
```

### Filter (inline, narrowing a list)
```razor
<MudTextField Class="famos-input-filter" @bind-Value="..." Placeholder="Filter..."
    Adornment="Adornment.Start" AdornmentIcon="@FamosIcons.Filter" />
```

**❌ NEVER set Style="width:..." or Style="max-width:..." inline on inputs.** Width is handled by CSS classes.

---

## Icons

Use the `FamosIcons` static class for all icons. Never use `Icons.Material.Filled.*` directly in components.

```csharp
// FamosIcons.cs
public static class FamosIcons
{
    public const string Search = Icons.Material.Outlined.Search;
    public const string Filter = Icons.Material.Outlined.FilterList;
    public const string Add = Icons.Material.Outlined.Add;
    public const string Close = Icons.Material.Outlined.Close;
    public const string Edit = Icons.Material.Outlined.Edit;
    public const string Delete = Icons.Material.Outlined.Delete;
    public const string ChevronRight = Icons.Material.Outlined.ChevronRight;
    public const string Warning = Icons.Material.Outlined.Warning;
    public const string Check = Icons.Material.Outlined.Check;
    public const string Upload = Icons.Material.Outlined.Upload;
    public const string Download = Icons.Material.Outlined.Download;
}
```

This means icon style (outlined vs filled) is changed in ONE place, not hunted across 40 files.

---

## Select / Dropdown

```razor
<MudSelect Class="famos-select" @bind-Value="..." Label="...">
    <MudSelectItem Value="...">...</MudSelectItem>
</MudSelect>
```

Never set Dense, Variant, or Margin inline.

---

## CSS Class Definitions (famos.css)

Every class above must be defined in `wwwroot/css/famos.css`. Current definitions:
- `.famos-btn-primary` — filled navy button
- `.famos-btn-outline` — outlined button, standard height
- `.famos-btn-outline-sm` — outlined button, small (28px height)
- `.famos-btn-danger` — outlined red button (ADD THIS — not yet defined)
- `.famos-input` — standard text input
- `.famos-input-search` — topbar search input (240px, rounded)
- `.famos-input-filter` — inline filter input (280px max)
- `.famos-select` — standard select/dropdown

---

## Clint Review Checklist (mandatory gate)

Before approving any PR, Clint checks:
- [ ] No `Variant=`, `Color=`, `Size=` on any MudButton
- [ ] No `Style="width:..."` or `Style="max-width:..."` inline on inputs
- [ ] No `Icons.Material.*` used directly in components — uses `FamosIcons.*`
- [ ] No `Dense=`, `Margin=`, `Variant=` inline on MudTextField or MudSelect
- [ ] Every new UI element uses an existing CSS class OR a new class is added to famos.css

If any of these are violated, **reject the PR** with a specific comment pointing to this file.

---

## Rationale

Every time a component gets inline styles, we create a one-off that diverges from the design. 
When Fred says "make all buttons consistent," we have to find and fix every file. 
With CSS classes: change one line in famos.css, everything updates everywhere.
