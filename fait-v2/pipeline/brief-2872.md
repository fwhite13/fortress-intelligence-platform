# CC Brief — ADO#2872: Apply FAIT v1 Visual Design Parity

## Working directory
`/home/fredw/projects/fip/fait-v2/`

## Tasks

### Task 1: Fix FipTheme.cs

File: `src/FortressAI.V2.Web/Theme/FipTheme.cs`

Replace the entire file content with the corrected theme below. Keep namespace `FortressAI.V2.Web.Theme`.

The current file has wrong Primary (`#0066CC` → must be `#1a2332`), wrong AppbarBackground/DrawerBackground (`#1A1A2E` → must be `#1a2332`), wrong SecondaryContrastText (`#1A1A2E` → must be `#1a2332`), wrong TextPrimary (`#1A1A2E` → `#1a2332`), wrong Info (`#0066CC` → `#2563eb`), wrong AppbarHeight (`56px` → `48px`), wrong DrawerWidthLeft (`260px` → `264px`), and has a PaletteDark block that must be removed.

Write this exact content:

```csharp
using MudBlazor;

namespace FortressAI.V2.Web.Theme;

/// <summary>
/// Fortress Intelligence Platform unified theme — MudBlazor v7 compatible.
/// Matches FORMS FipTheme exactly, namespace updated for FAIT v2.
/// No PaletteDark — app is always light mode.
/// </summary>
public static class FipTheme
{
    public static MudTheme Create() => new MudTheme
    {
        PaletteLight = new PaletteLight
        {
            Primary = "#1a2332",
            PrimaryContrastText = "#ffffff",
            Secondary = "#d4af37",
            SecondaryContrastText = "#1a2332",
            Background = "#f8f9fa",
            Surface = "#ffffff",
            AppbarBackground = "#1a2332",
            AppbarText = "#ffffff",
            DrawerBackground = "#1a2332",
            DrawerText = "#f0f0f0",
            DrawerIcon = "#d4af37",
            TextPrimary = "#1a2332",
            TextSecondary = "#6b7280",
            TextDisabled = "rgba(0,0,0,0.38)",
            ActionDefault = "#6b7280",
            Success = "#059669",
            Warning = "#d97706",
            Error = "#dc2626",
            Info = "#2563eb",
            TableLines = "#e5e7eb",
            TableHover = "#f3f4f6",
        },
        Typography = new Typography
        {
            Default = new Default
            {
                FontFamily = new[] { "Inter", "system-ui", "-apple-system", "sans-serif" },
                FontSize = "0.9375rem",
                LineHeight = 1.6,
            },
            H4 = new H4 { FontWeight = 700 },
            H5 = new H5 { FontWeight = 600 },
            H6 = new H6 { FontWeight = 600 },
            Button = new MudBlazor.Button
            {
                FontFamily = new[] { "Inter", "sans-serif" },
                FontWeight = 500,
                TextTransform = "none",
                FontSize = "0.9rem",
            },
            Caption = new Caption { FontSize = "0.75rem" }
        },
        LayoutProperties = new LayoutProperties
        {
            AppbarHeight = "48px",
            DrawerWidthLeft = "264px",
        }
    };
}
```

### Task 2: Copy fortress.css from FAIT v1

Source: `/home/fredw/projects/fip/fait/src/FortressAI.Web/wwwroot/css/fortress.css`
Destination: `src/FortressAI.V2.Web/wwwroot/css/fortress.css`

Copy the file exactly. Do NOT modify any CSS variables — the source already uses CSS variables throughout. Do not change any content.

### Task 3: Link fortress.css in App.razor

File: `src/FortressAI.V2.Web/Components/App.razor`

Current content has:
```html
    <link rel="stylesheet" href="_content/MudBlazor/MudBlazor.min.css" />
    <link rel="stylesheet" href="css/app.css" />
```

Change to (add fortress.css BEFORE app.css):
```html
    <link rel="stylesheet" href="_content/MudBlazor/MudBlazor.min.css" />
    <link rel="stylesheet" href="css/fortress.css" />
    <link rel="stylesheet" href="css/app.css" />
```

### Task 4: Scan and fix hardcoded colors in .razor files

Scan all .razor files under `src/FortressAI.V2.Web/Components/` for hardcoded hex colors in style= attributes or MudBlazor Style= props.

The grep already found:
- `Onboarding.razor` lines 193, 208-213: These are DATA values for an accent color picker feature (list of color options + a default value). These are NOT UI styling — they are user-facing color option data. DO NOT change these. They represent choices a user can pick, not the UI's own color values.

No other hardcoded colors were found in .razor inline styles. Confirm this with a scan and note it in comments.

### Task 5: Verify dotnet build succeeds

Run from `/home/fredw/projects/fip/fait-v2/`:
```bash
dotnet build src/FortressAI.V2.Web/FortressAI.V2.Web.csproj --no-restore 2>&1 | tail -20
```

If build fails, fix errors before committing.

### Task 6: Commit

From `/home/fredw/projects/fip/fait-v2/`:
```bash
git add src/FortressAI.V2.Web/Theme/FipTheme.cs \
        src/FortressAI.V2.Web/wwwroot/css/fortress.css \
        src/FortressAI.V2.Web/Components/App.razor
git commit -m "feat(fait-v2#2872): apply FAIT v1 visual design parity"
```

Then output the commit hash.

## Output instructions

After completing all tasks, output:
1. The git commit hash
2. A one-line build result (SUCCEEDED or FAILED with error)
3. Whether any hardcoded color values were found in .razor style attributes (yes/no + details)
4. Confirmation that fortress.css was copied and App.razor was updated

Do NOT output the full CSS file content. Just confirm the tasks completed.
