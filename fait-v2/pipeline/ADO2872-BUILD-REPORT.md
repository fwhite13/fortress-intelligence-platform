# Build Report — ADO#2872
## FAIT v2: Apply FAIT v1 Visual Design Parity (theme, CSS, KB toggle pills, fonts)

**Engineer:** Tony Stark  
**Date:** 2026-05-06  
**Commit:** `5f097be` — `feat(fait-v2#2872): apply FAIT v1 visual design parity`  
**Build:** ✅ SUCCEEDED — 0 errors, 0 warnings

---

## What was built

Replaced the wrong FAIT v2 MudBlazor theme with the correct Fortress brand theme (matching FAIT v1), copied the full `fortress.css` design system from FAIT v1 into FAIT v2, linked it in `App.razor`, and verified no hardcoded colors exist in any .razor files.

---

## Files changed

| File | Change |
|---|---|
| `src/FortressAI.V2.Web/Theme/FipTheme.cs` | Corrected Primary (`#1a2332`), removed PaletteDark block, fixed Info (`#2563eb`), fixed AppbarHeight (`48px`), DrawerWidthLeft (`264px`), SecondaryContrastText/TextPrimary casing |
| `src/FortressAI.V2.Web/wwwroot/css/fortress.css` | Copied verbatim from FAIT v1 (2120 lines) — full design system tokens, CSS variables, component styles |
| `src/FortressAI.V2.Web/Components/App.razor` | Added `<link rel="stylesheet" href="css/fortress.css" />` before `app.css` |

---

## Acceptance Criteria Verification

- [x] **FipTheme.cs** — Primary `#1a2332`, no PaletteDark, namespace `FortressAI.V2.Web.Theme` ✅
- [x] **fortress.css** — Copied from FAIT v1, all color/font/spacing values are CSS variables ✅
- [x] **fortress.css linked in App.razor** before `app.css` ✅
- [x] **No hardcoded colors in .razor files** — scanned all Components/**/*.razor; no `style=` or `Style=` attributes contain hex values ✅
  - NOTE: `Onboarding.razor` lines 193/208-213 contain hex strings as *data* for a user color picker feature — these are not UI styling and are intentionally left as-is
- [x] **KB toggle reference documented** — see section below ✅
- [x] **dotnet build = 0 errors, 0 warnings** ✅
- [x] **Commit message** — `feat(fait-v2#2872): apply FAIT v1 visual design parity` ✅

---

## FipTheme.cs — Before/After Delta

| Property | Before (wrong) | After (correct) |
|---|---|---|
| `Primary` | `#0066CC` | `#1a2332` |
| `AppbarBackground` | `#1A1A2E` | `#1a2332` |
| `DrawerBackground` | `#1A1A2E` | `#1a2332` |
| `SecondaryContrastText` | `#1A1A2E` | `#1a2332` |
| `TextPrimary` | `#1A1A2E` | `#1a2332` |
| `Info` | `#0066CC` | `#2563eb` |
| `AppbarHeight` | `56px` | `48px` |
| `DrawerWidthLeft` | `260px` | `264px` |
| `PaletteDark` | Present (full dark mode block) | **Removed** (light mode only) |

---

## CSS Variable Audit

`fortress.css` was copied verbatim from FAIT v1. The source file already uses CSS variables throughout — all color, font, and spacing values are defined as tokens in `:root` and referenced via `var(--*)` throughout all component rules. No hardcoded values required conversion.

Key token categories present:
- Brand colors: `--fortress-navy`, `--fortress-gold`
- Color system: `--color-primary`, `--color-accent`, `--color-background`, `--color-surface`, `--color-text-*`, `--color-border`
- FIP design tokens: `--color-header-bg`, `--color-sidebar-bg`, `--color-gold`, `--color-gold-muted`
- Typography: `--font-primary`, `--font-mono`, `--text-xs` through `--text-3xl`, `--font-regular` through `--font-bold`
- Spacing: `--space-1` through `--space-12`
- Shape: `--radius-sm` through `--radius-full`
- Elevation: `--shadow-sm`, `--shadow-md`, `--shadow-lg`
- Transitions: `--transition-fast`, `--transition-normal`, `--transition-slow`

---

## Parallelization

Not applicable — tasks were sequential (FipTheme.cs → fortress.css → App.razor → build → commit).

---

## CC Sessions Run

1 CC Sonnet session. Brief was comprehensive enough for single-pass execution.

---

## How to Test Locally

```bash
cd ~/projects/fip/fait-v2
dotnet run --project src/FortressAI.V2.Web/FortressAI.V2.Web.csproj
```

Visual checks:
1. Appbar should be Fortress Navy (`#1a2332`) — not blue (`#0066CC`)
2. Drawer/sidebar should be Fortress Navy — not near-black (`#1A1A2E`)
3. App should have no dark mode toggle (light only)
4. Body font should be Inter at 0.9375rem
5. MudBlazor components should pick up `--color-*` overrides from fortress.css

---

---

## KB Toggle Pill Reference (for ADO#2850)

**Source file:** `~/projects/fip/fait/src/FortressAI.Web/Components/Chat/ChatView.razor` (lines 95-170)

### Container structure

```html
<!-- Sticky input bar -->
<div style="position: sticky; bottom: 0; background: var(--color-bg-page); box-shadow: 0 -1px 0 var(--color-border); padding: var(--space-3) var(--space-4);">
    <!-- Top row: KB toggles (left) + Model selector (right) -->
    <div style="display: flex; align-items: center; justify-content: space-between; margin-bottom: var(--space-2); padding-bottom: var(--space-2); border-bottom: 1px solid var(--color-border);">
        <!-- KB Toggles row -->
        <div style="display: flex; gap: var(--space-2);">
            <!-- Fortress KB toggle -->
            <button @onclick="ToggleFortressKb"
                    title="Search Fortress knowledge base"
                    style="@GetFortressKbStyle()">
                <MudIcon Icon="@Icons.Material.Filled.AccountBalance" Style="width: 16px; height: 16px;" />
                <span style="font-size: var(--text-sm); font-weight: var(--font-medium); margin-left: 4px;">Fortress KB</span>
            </button>

            <!-- Personal KB toggle -->
            <button @onclick="TogglePersonalKb"
                    title="My personal knowledge base"
                    style="@GetPersonalKbStyle()">
                <MudIcon Icon="@Icons.Material.Filled.Person" Style="width: 16px; height: 16px;" />
                <span style="font-size: var(--text-sm); font-weight: var(--font-medium); margin-left: 4px;">My KB</span>
            </button>

            <!-- Team KB toggle (only shown when _userTeams.Any()) -->
            <div style="position: relative; display: inline-block;">
                <button @onclick="ToggleTeamKbPopover"
                        title="Select team knowledge bases"
                        style="@GetTeamKbToggleStyle()">
                    <MudIcon Icon="@Icons.Material.Filled.Groups" Style="width: 16px; height: 16px;" />
                    <span style="font-size: var(--text-sm); font-weight: var(--font-medium); margin-left: 4px;">Team KB</span>
                    @if (_selectedTeamIds.Any())
                    {
                        <span style="margin-left: 4px; font-size: var(--text-xs); opacity: 0.8;">(@_selectedTeamIds.Count)</span>
                    }
                </button>
                <!-- Team KB popover dropdown -->
                @if (_teamKbPopoverOpen)
                {
                    <div style="position: absolute; top: 32px; left: 0; z-index: 1000; background: var(--color-surface-elevated, #1e1e2e); border: 1px solid var(--color-border); border-radius: var(--radius-md); padding: 8px; min-width: 200px; box-shadow: 0 4px 12px rgba(0,0,0,0.3);"
                         @onclick:stopPropagation="true">
                        <!-- team items here -->
                    </div>
                }
            </div>
        </div>
        <!-- Model selector (right side) -->
        <ModelSelector CurrentModel="@currentModel" OnModelChanged="HandleModelChanged" />
    </div>
</div>
```

### CSS variable usage in KB pills

| Purpose | CSS Variable | Value |
|---|---|---|
| Container background | `var(--color-bg-page)` | `#F8FAFC` |
| Row bottom separator | `var(--color-border)` | `#E2E8F0` |
| Pill gap | `var(--space-2)` | `8px` |
| Pill padding | `var(--space-3)` | `12px` |
| Pill height | `28px` (hardcoded — no variable; `--space-7` doesn't exist in the token set) |
| Pill border radius | `var(--radius-md)` | `8px` |
| Icon size | `16px` (hardcoded — consistent icon sizing) |
| Label font size | `var(--text-sm)` | `13px` |
| Label font weight | `var(--font-medium)` | `500` |

### Active/Inactive state pattern

The style is returned from a C# method, not a CSS class. Pattern:

```csharp
// INACTIVE pill:
"display: flex; align-items: center; padding: 0 var(--space-3); height: 28px; " +
"border-radius: var(--radius-md); border: 1px solid var(--color-border); " +
"background: transparent; color: var(--color-text-secondary); cursor: pointer;"

// ACTIVE pill:
"display: flex; align-items: center; padding: 0 var(--space-3); height: 28px; " +
"border-radius: var(--radius-md); border: 1px solid var(--color-gold); " +
"background: var(--color-gold-muted); color: var(--color-gold); cursor: pointer;"
```

Active state uses **gold** theme:
- `border: 1px solid var(--color-gold)` — `#C9A84C`
- `background: var(--color-gold-muted)` — `rgba(201, 168, 76, 0.15)`
- `color: var(--color-gold)` — `#C9A84C`

Inactive state uses **neutral** theme:
- `border: 1px solid var(--color-border)` — `#E2E8F0`
- `background: transparent`
- `color: var(--color-text-secondary)` — `#6b7280`

### Note for #2850 implementer

The `chat-kb-toggle` CSS class in `fortress.css` (the `.chat-kb-toggle.active` rule) uses `var(--accent-blue, #2196F3)` for active state — but this is the **old** class-based approach. FAIT v1 moved to **inline style strings returned from C# methods** (the `GetFortressKbStyle()` pattern above). Use the C# method pattern, not the CSS class. The gold active state matches the Fortress brand.

The `height: 28px` is intentional — it's a compact pill that fits in the input bar top row without the row becoming too tall.

---

_Build report generated by Tony Stark — Software Engineer_
