# FAM OS Sprint 3 Spec — UI/UX Restyling
**Author:** Reed Richards (Software Architect)  
**Date:** 2026-03-19  
**Sprint Goal:** Match the visual language of Lauren's mockup (`IAAPA_Portal_v2_restyled.html`)  
**Scope:** Dashboard + Pipeline + OpportunityWorkspace + NavMenu/MainLayout + Shared components  
**Constraint:** MudBlazor v7 only. Zero functional changes. All changes inside `famos/src/FamOs.Web/`.

---

## 1. Objective

Sprint 3 transforms FAM OS from its current Fortress-navy-first theme into the cleaner, more modern visual language shown in Lauren's IAAPA Portal mockup. The mockup uses a deeper navy sidebar, lighter content area, Fraunces serif for page headings, Plus Jakarta Sans for body text, sky-blue accent, and a card system with left-rule accent bars for KPIs.

After Sprint 3:
- The sidebar matches the mockup's `#002050` navy with sky-blue active states
- KPI stat cards use the left-border accent treatment (4px colored bar)
- Page headings use `Fraunces` serif, large and airy
- All cards have `border-radius: 12px`, subtle `border: 1px solid #e2e6ed`, and minimal elevation
- Buttons use `border-radius: 7px`, no text-transform, consistent sizing
- The pipeline board uses the mockup's kanban column headers (dot + label + count badge)
- Signal/status chips are rounded pill (`border-radius: 20px`) style
- Body font is `Plus Jakarta Sans` at 14px

---

## 2. Design Reference — Mockup Visual Language

### 2.1 Color Palette (exact values from mockup CSS)

| Token Name | Hex | Usage |
|---|---|---|
| `--navy` | `#002050` | Sidebar bg, appbar bg, dark card headers, primary text |
| `--navy-mid` | `#0a3268` | Hover/active nav backgrounds |
| `--navy-lite` | `#153d78` | Button hover |
| `--sky` | `#0090d0` | Primary accent, active nav border-left, primary buttons |
| `--sky-light` | `#10b0f0` | Hover on sky, highlights |
| `--cream` | `#f2f4f7` | Page background |
| `--white` | `#ffffff` | Card surface, topbar |
| `--text` | `#3a4250` | Primary body text |
| `--muted` | `#6b7585` | Secondary text, table headers, labels |
| `--border` | `#e2e6ed` | Card borders, table lines |
| `--green` | `#059669` | Success, "Bound" status, trend-up |
| `--amber` | `#f0a010` | Warning, Decision Required trend |
| `--red` | `#DC2626` | Error, time risk, urgent |
| `--blue` | `#2563EB` | Info, "Quotes In" status |

**KPI card accent colors:**
- `.kpi-navy::before` → `#002050`
- `.kpi-gold::before` (sky treatment) → `#0090d0`
- `.kpi-green::before` → `#059669`
- `.kpi-amber::before` → `#f0a010`
- `.kpi-blue::before` → `#2563EB`

### 2.2 Typography

| Role | Font | Size | Weight | Notes |
|---|---|---|---|---|
| Body default | Plus Jakarta Sans | 14px | 400 | Replaces Inter |
| Page heading (`<h2>`) | Fraunces (serif) | 23px | 400 | `letter-spacing: -0.3px` |
| Page subtext | Plus Jakarta Sans | 12.5px | 400 | `color: var(--muted)` |
| KPI value | Fraunces (serif) | 30px | 400 | `line-height: 1.1` |
| KPI label | Plus Jakarta Sans | 10px | 700 | uppercase, `letter-spacing: 0.7px` |
| Table header | Plus Jakarta Sans | 10px | 700 | uppercase, `letter-spacing: 0.6px` |
| Button | Plus Jakarta Sans | 12.5px | 600 | `text-transform: none` |
| Nav item | Plus Jakarta Sans | 12.5px | 500 | |
| Section label | Plus Jakarta Sans | 9.5px | 700 | uppercase, `letter-spacing: 1.2px` |
| Card title | Plus Jakarta Sans | 12.5px | 700 | `color: #002050` |

**Font import (Google Fonts):**
```html
<link href="https://fonts.googleapis.com/css2?family=Plus+Jakarta+Sans:wght@300;400;500;600;700;800&family=Fraunces:wght@600;700;800&display=swap" rel="stylesheet">
```

### 2.3 Layout Structure

```
┌─────────────────────────────────────────────────────┐
│  SIDEBAR (262px, #002050)                           │
│  ├── Logo section (white bg, 44px logo)             │
│  ├── Search input (dark glass)                      │
│  ├── Nav section labels (9.5px uppercase muted)     │
│  ├── Nav items (12.5px, active: sky border-left)    │
│  └── User footer (avatar + name + role)             │
│                                                     │
│  MAIN CONTENT                                       │
│  ├── TOPBAR (54px, white, border-bottom)            │
│  │   ├── Breadcrumb + role chip                     │
│  │   └── Search + action buttons                   │
│  └── CONTENT AREA (padding: 24px 28px, cream bg)   │
│      ├── Page header (Fraunces h2 + subtitle)       │
│      ├── KPI grid (4-col, accent-bar cards)         │
│      └── Card grid (white cards, 12px radius)       │
└─────────────────────────────────────────────────────┘
```

### 2.4 Card System

```css
/* Standard card */
background: white;
border-radius: 12px;
border: 1px solid #e2e6ed;
padding: 16px 18px;

/* Card title */
font-size: 12.5px; font-weight: 700; color: #002050;

/* KPI card — left accent bar */
position: relative; overflow: hidden;
::before { content:""; position:absolute; top:0; left:0; width:4px; height:100%; background: <accent-color>; }
```

### 2.5 Button System

```css
/* Primary */
background: #002050; color: white; border-radius: 7px;
padding: 7px 13px; font-size: 12.5px; font-weight: 600;

/* Sky (was "gold" in mockup) */
background: #0090d0; color: white; border-radius: 7px;

/* Outline */
background: white; color: #3a4250;
border: 1.5px solid #e2e6ed; border-radius: 7px;

/* Small: padding 5px 10px; font-size: 11.5px */
/* XSmall: padding 3px 8px; font-size: 10.5px; font-weight: 700 */
```

### 2.6 Navigation Items

```css
/* Default */
color: rgba(255,255,255,0.6); border-left: 2px solid transparent;
padding: 7px 10px; border-radius: 7px;

/* Active */
background: rgba(0,144,208,0.13); color: #fff;
border-left-color: #0090d0;

/* Hover */
background: rgba(255,255,255,0.05); color: rgba(255,255,255,0.85);
```

### 2.7 Pipeline Kanban Cards (`.kcard` in mockup)

```css
/* Column header (.kcol-header) */
background: white; border: 1px solid #e2e6ed; border-radius: 10px;
padding: 9px 10px; margin-bottom: 8px;
/* Contains: colored dot (9px circle) + label + count badge */

/* Kanban card */
background: white; border: 1px solid #e2e6ed; border-radius: 10px;
padding: 11px; cursor: pointer;
/* Hover: border-color: #0090d0; box-shadow: 0 4px 12px rgba(0,144,208,0.1) */

/* Card name */
font-weight: 700; font-size: 12.5px; color: #002050;

/* Card detail */
font-size: 11px; color: #6b7585;

/* Premium */
font-weight: 700; font-size: 11px; color: #059669;
```

### 2.8 Status Pills / Chips

```css
/* Base */
font-size: 10.5px; font-weight: 700;
padding: 2px 9px; border-radius: 20px; display: inline-block;

/* Stage variants */
.s-intake   { background: #dbeafe; color: #1d4ed8 }
.s-review   { background: #ede9fe; color: #6d28d9 }
.s-sub      { background: #fef3c7; color: #92400e }
.s-quotes   { background: #e0f2fe; color: #0369a1 }
.s-prop     { background: #fdf4ff; color: #9333ea }
.s-bound    { background: #d1fae5; color: #065f46 }
```

---

## 3. MudBlazor Theme — Updated `FipTheme.cs`

**File:** `src/FamOs.Web/Theme/FipTheme.cs`

Replace the entire `Create()` method body with the following:

```csharp
using MudBlazor;

namespace FamOs.Web.Theme;

/// <summary>
/// FAM OS Theme — Sprint 3 restyling.
/// Matches Lauren's IAAPA Portal v2 mockup visual language.
/// Light mode only. MudBlazor v7.
/// </summary>
public static class FipTheme
{
    public static MudTheme Create() => new MudTheme
    {
        PaletteLight = new PaletteLight
        {
            // --- Core Brand ---
            Primary           = "#002050",      // navy — sidebar, appbar, primary buttons
            PrimaryContrastText = "#ffffff",
            Secondary         = "#0090d0",      // sky-blue — active states, highlights
            SecondaryContrastText = "#ffffff",
            Tertiary          = "#f0a010",      // amber — warnings

            // --- Surfaces ---
            Background        = "#f2f4f7",      // --cream page background
            Surface           = "#ffffff",      // card/panel surface
            AppbarBackground  = "#002050",      // matches sidebar navy
            AppbarText        = "#ffffff",

            // --- Drawer/Sidebar ---
            DrawerBackground  = "#002050",
            DrawerText        = "rgba(255,255,255,0.85)",
            DrawerIcon        = "#0090d0",      // sky accent for nav icons

            // --- Text ---
            TextPrimary       = "#3a4250",      // --text body color
            TextSecondary     = "#6b7585",      // --muted
            TextDisabled      = "rgba(58,66,80,0.38)",
            ActionDefault     = "#6b7585",

            // --- Semantic ---
            Success           = "#059669",
            Warning           = "#f0a010",
            Error             = "#DC2626",
            Info              = "#2563EB",

            // --- Table / Structure ---
            TableLines        = "#e2e6ed",      // --border
            TableHover        = "#f8f9fb",

            // --- Buttons ---
            // MudBlazor uses Primary for Variant.Filled Color.Primary
            // Secondary for Color.Secondary buttons (sky)
        },

        Typography = new Typography
        {
            Default = new Default
            {
                FontFamily  = new[] { "Plus Jakarta Sans", "system-ui", "-apple-system", "sans-serif" },
                FontSize    = "0.875rem",   // 14px
                FontWeight  = "400",
                LineHeight  = 1.5,
                LetterSpacing = "0em",
            },
            H1 = new H1 { FontFamily = new[] { "Fraunces", "Georgia", "serif" }, FontSize = "2rem",    FontWeight = "400" },
            H2 = new H2 { FontFamily = new[] { "Fraunces", "Georgia", "serif" }, FontSize = "1.4375rem", FontWeight = "400", LetterSpacing = "-0.3px" },
            H3 = new H3 { FontFamily = new[] { "Fraunces", "Georgia", "serif" }, FontSize = "1.25rem",  FontWeight = "400" },
            H4 = new H4 { FontFamily = new[] { "Fraunces", "Georgia", "serif" }, FontSize = "1.875rem", FontWeight = "400", LineHeight = "1.1" }, // KPI value
            H5 = new H5 { FontFamily = new[] { "Plus Jakarta Sans", "sans-serif" }, FontWeight = "700", FontSize = "0.78125rem" }, // card titles
            H6 = new H6 { FontFamily = new[] { "Plus Jakarta Sans", "sans-serif" }, FontWeight = "700", FontSize = "0.78125rem" },
            Subtitle1 = new Subtitle1 { FontSize = "0.78125rem", FontWeight = "700", LineHeight = "1.3" },
            Subtitle2 = new Subtitle2 { FontSize = "0.71875rem", FontWeight = "600" },
            Body1 = new Body1 { FontSize = "0.875rem",  FontWeight = "400", LineHeight = "1.5" },
            Body2 = new Body2 { FontSize = "0.71875rem", FontWeight = "400", LineHeight = "1.45", LetterSpacing = "0em" },
            Button = new MudBlazor.Button
            {
                FontFamily    = new[] { "Plus Jakarta Sans", "sans-serif" },
                FontSize      = "0.78125rem",  // 12.5px
                FontWeight    = "600",
                TextTransform = "none",
                LetterSpacing = "0em",
            },
            Caption = new Caption { FontSize = "0.6875rem", FontWeight = "400" },
            Overline = new Overline
            {
                FontSize    = "0.59375rem",  // 9.5px
                FontWeight  = "700",
                TextTransform = "uppercase",
                LetterSpacing = "1.2px",
            },
        },

        LayoutProperties = new LayoutProperties
        {
            AppbarHeight    = "54px",       // matches mockup topbar height
            DrawerWidthLeft = "262px",      // matches mockup --sidebar-w
        },

        Shadows = new Shadow
        {
            // Minimal shadow system — cards rely on borders, not heavy shadows
            Elevation = new[]
            {
                "none",
                "0 1px 2px rgba(0,0,0,0.05)",
                "0 1px 3px rgba(0,0,0,0.08), 0 1px 2px rgba(0,0,0,0.04)",
                "0 2px 4px rgba(0,0,0,0.06), 0 1px 2px rgba(0,0,0,0.04)",
                "0 4px 6px rgba(0,0,0,0.06), 0 2px 4px rgba(0,0,0,0.04)",
                "0 4px 12px rgba(0,144,208,0.10)",   // elevation[5] — card hover (sky tint)
                "0 8px 16px rgba(0,0,0,0.08)",
                "0 12px 24px rgba(0,0,0,0.08)",
                "0 16px 32px rgba(0,0,0,0.08)",
                "0 20px 40px rgba(0,0,0,0.10)",
                "0 24px 48px rgba(0,0,0,0.12)",
                "0 32px 56px rgba(0,0,0,0.12)",
                "0 40px 64px rgba(0,0,0,0.12)",
                "0 48px 72px rgba(0,0,0,0.12)",
                "0 56px 80px rgba(0,0,0,0.12)",
                "0 64px 88px rgba(0,0,0,0.12)",
                "0 72px 96px rgba(0,0,0,0.12)",
                "0 80px 104px rgba(0,0,0,0.12)",
                "0 88px 112px rgba(0,0,0,0.12)",
                "0 96px 120px rgba(0,0,0,0.12)",
                "0 104px 128px rgba(0,0,0,0.12)",
                "0 112px 136px rgba(0,0,0,0.12)",
                "0 120px 144px rgba(0,0,0,0.12)",
                "0 128px 152px rgba(0,0,0,0.12)",
                "0 136px 160px rgba(0,0,0,0.12)",
            }
        },
    };
}
```

---

## 4. Layout Changes

### 4.1 `App.razor` — Add Google Font Import

**File:** `src/FamOs.Web/Components/App.razor`

Add inside `<head>`, before existing stylesheets:

```html
<link href="https://fonts.googleapis.com/css2?family=Plus+Jakarta+Sans:wght@300;400;500;600;700;800&family=Fraunces:wght@600;700;800&display=swap" rel="stylesheet">
```

Final head order:
```html
<link href="https://fonts.googleapis.com/css2?family=Plus+Jakarta+Sans:wght@300;400;500;600;700;800&family=Fraunces:wght@600;700;800&display=swap" rel="stylesheet">
<link rel="stylesheet" href="/_content/MudBlazor/MudBlazor.min.css" />
<link rel="stylesheet" href="/_content/FipShared/css/fip-tokens.css" />
<link rel="stylesheet" href="/css/famos.css" />
```

### 4.2 `MainLayout.razor` — Updated Drawer Header & Footer

**File:** `src/FamOs.Web/Components/Layout/MainLayout.razor`

Change: `MudDrawer` remains the same structure. Update the drawer header content to remove the SVG shield and use a cleaner text treatment. Update main content padding to match the mockup's `24px 28px`.

**Replace drawer header block** (the `<MudDrawerHeader>` element and its contents):

```razor
<MudDrawerHeader Style="padding: 16px 16px 14px; border-bottom: 1px solid rgba(255,255,255,0.08); background: white;">
    <div style="display: flex; align-items: center; gap: 8px;">
        <span style="font-family: 'Fraunces', Georgia, serif; font-size: 18px; font-weight: 700; color: #002050; letter-spacing: -0.3px;">FAM OS</span>
        <span style="font-size: 10px; font-weight: 700; background: rgba(0,144,208,0.12); color: #0090d0; padding: 2px 8px; border-radius: 10px; text-transform: uppercase; letter-spacing: 0.8px;">Beta</span>
    </div>
    <div style="font-size: 11px; color: #6b7585; margin-top: 2px;">Fortress Affinity Management OS</div>
</MudDrawerHeader>
```

**Replace `<MudMainContent>` line:**

```razor
<MudMainContent Style="padding-top: 54px !important;">
    <div style="padding: 24px 28px;">
        @Body
    </div>
</MudMainContent>
```

**Replace drawer footer (`<div class="fip-drawer-footer">`):**

```razor
<div style="padding: 11px 14px; border-top: 1px solid rgba(255,255,255,0.07); display: flex; align-items: center; gap: 9px;">
    <div style="width: 30px; height: 30px; border-radius: 9px; background: linear-gradient(135deg, #0090d0, #10b0f0); color: #fff; display: flex; align-items: center; justify-content: center; font-size: 11px; font-weight: 800; flex-shrink: 0;">
        @_userInitial
    </div>
    <div>
        <div style="font-size: 12px; font-weight: 700; color: #fff;">@(_userName.Length > 0 ? _userName : "User")</div>
        <div style="font-size: 10px; color: rgba(255,255,255,0.4); margin-top: 1px;">FAM OS</div>
    </div>
</div>
```

> **Note:** The FipNavBar top appbar is shared infrastructure — do NOT modify it. Only the MudDrawer and MudMainContent are in scope.

### 4.3 `NavMenu.razor` — Restyled Nav Items

**File:** `src/FamOs.Web/Components/Layout/NavMenu.razor`

Replace the entire file content with:

```razor
@using Microsoft.AspNetCore.Components.Routing

<div style="padding: 12px 10px 4px;">
    <div style="font-size: 9.5px; font-weight: 700; color: rgba(255,255,255,0.3); text-transform: uppercase; letter-spacing: 1.2px; padding: 0 8px; margin-bottom: 4px;">
        Main
    </div>

    <NavLink href="/" Match="NavLinkMatch.All" class="famos-nav-item" ActiveClass="famos-nav-item--active">
        <span class="famos-nav-icon">
            <MudIcon Icon="@Icons.Material.Filled.Dashboard" Size="Size.Small" />
        </span>
        Dashboard
    </NavLink>

    <NavLink href="/pipeline" Match="NavLinkMatch.Prefix" class="famos-nav-item" ActiveClass="famos-nav-item--active">
        <span class="famos-nav-icon">
            <MudIcon Icon="@Icons.Material.Filled.ViewKanban" Size="Size.Small" />
        </span>
        Pipeline
    </NavLink>

    <NavLink href="/tasks" Match="NavLinkMatch.Prefix" class="famos-nav-item" ActiveClass="famos-nav-item--active">
        <span class="famos-nav-icon">
            <MudIcon Icon="@Icons.Material.Filled.CheckBox" Size="Size.Small" />
        </span>
        Task Center
    </NavLink>

    <div style="height: 1px; background: rgba(255,255,255,0.07); margin: 8px 14px;"></div>

    <div style="font-size: 9.5px; font-weight: 700; color: rgba(255,255,255,0.3); text-transform: uppercase; letter-spacing: 1.2px; padding: 0 8px; margin-bottom: 4px; margin-top: 8px;">
        Coming Soon
    </div>

    <span class="famos-nav-item famos-nav-item--disabled">
        <span class="famos-nav-icon">
            <MudIcon Icon="@Icons.Material.Filled.Business" Size="Size.Small" />
        </span>
        Accounts
        <span class="famos-nav-badge">Soon</span>
    </span>

    <span class="famos-nav-item famos-nav-item--disabled">
        <span class="famos-nav-icon">
            <MudIcon Icon="@Icons.Material.Filled.BarChart" Size="Size.Small" />
        </span>
        Reports
        <span class="famos-nav-badge">Soon</span>
    </span>
</div>
```

> **Note:** The `NavLink` Blazor component handles active class automatically. The custom CSS classes are defined in Section 7.

---

## 5. Page-by-Page Changes

### 5.1 `Dashboard.razor`

**File:** `src/FamOs.Web/Components/Pages/Dashboard.razor`

**Current issues vs. mockup:**
- Page heading is plain `<MudText Typo="Typo.h5">` — needs Fraunces serif treatment
- Stat cards use `MudCard` with inline borders — need the left-accent-bar KPI card style
- Button is a plain `MudButton` — needs the mockup's "Pipeline View →" style

**Replace the outer page header:**

```razor
<div class="famos-page-header famos-page-header-row">
    <div>
        <h2 class="famos-page-h2">FAM OS Dashboard</h2>
        <p class="famos-page-sub">Active Pipeline · @DateTime.Now.ToString("MMMM yyyy")</p>
    </div>
    <div style="display: flex; gap: 8px; margin-top: 4px;">
        <MudButton Variant="Variant.Outlined" Color="Color.Default" Size="Size.Small"
                   Class="famos-btn-outline-sm" OnClick="GoToPipeline">
            Pipeline View →
        </MudButton>
    </div>
</div>
```

**Replace the 4-column MudGrid stat cards** with `StatCard` components (see Section 6):

```razor
<div class="famos-kpi-grid mb-5">
    <StatCard Label="Active Opportunities" Value="@_summary.TotalActive.ToString()"
              AccentClass="kpi-navy" Sub="Currently in pipeline" />
    <StatCard Label="Time Risk" Value="@_summary.TimeRiskCount.ToString()"
              AccentClass="kpi-red" Sub="Need immediate action" />
    <StatCard Label="Decision Needed" Value="@_summary.DecisionNeeded.ToString()"
              AccentClass="kpi-amber" Sub="Awaiting client response" />
    <StatCard Label="Bound This Month" Value="@_summary.BoundThisMonth.ToString()"
              AccentClass="kpi-green" Sub="Successfully closed" />
</div>
```

**Replace the `MudButton` at the bottom** (remove it — the header row button replaces it).

### 5.2 `Pipeline.razor`

**File:** `src/FamOs.Web/Components/Pages/Pipeline.razor`

**Current issues vs. mockup:**
- Page header div is ad-hoc inline styles — needs page header component treatment
- `famos-pipeline-column-header` lacks the colored-dot treatment from mockup
- Column containers need updated border-radius / colors

**Replace the header `<div>`:**

```razor
<div class="famos-page-header famos-page-header-row mb-4">
    <div>
        <h2 class="famos-page-h2">Pipeline</h2>
        <p class="famos-page-sub">@(_byStage.Values.Sum(l => l.Count)) active opportunities</p>
    </div>
    <MudButton Variant="Variant.Filled" Color="Color.Primary" StartIcon="@Icons.Material.Filled.Add"
               Class="famos-btn-primary" OnClick="OpenCreateDialog">
        New Opportunity
    </MudButton>
</div>
```

**Replace each column's header div** in the `@foreach` (inside the `.famos-pipeline-board` loop):

```razor
<div class="famos-pipeline-column-header">
    <span class="famos-kcol-dot" style="background: @GetStageColor(col.Stage);"></span>
    <span class="famos-kcol-label">@col.DisplayName</span>
    <span class="famos-kcol-count">@GetStageCount(col.Stage)</span>
</div>
```

**Add helper method to `@code` block:**

```csharp
private static string GetStageColor(LifecycleStage stage) => stage switch
{
    LifecycleStage.Intake           => "#1d4ed8",
    LifecycleStage.UnderwritingPrep => "#6d28d9",
    LifecycleStage.Marketed         => "#d97706",
    LifecycleStage.QuotesReceived   => "#0369a1",
    LifecycleStage.ClientDecision   => "#9333ea",
    LifecycleStage.Binding          => "#0090d0",
    LifecycleStage.Bound            => "#059669",
    _                               => "#6b7585",
};
```

### 5.3 `OpportunityWorkspace.razor`

**File:** `src/FamOs.Web/Components/Pages/Opportunity/OpportunityWorkspace.razor`

**Current issues vs. mockup:**
- Workspace header uses ad-hoc inline flex div — needs page header treatment
- Stage chip uses `Color.Primary` (navy) — should use stage-specific color
- The Park/Close buttons are plain outlined — need sizing update
- Activity timeline section heading is `MudText Typo.h6` — should use card-title class

**Replace the workspace header block:**

```razor
<div class="famos-page-header famos-page-header-row mb-4">
    <div>
        <h2 class="famos-page-h2" style="font-size: 20px;">@_opp.Name</h2>
        <div style="display: flex; gap: 8px; align-items: center; flex-wrap: wrap; margin-top: 4px;">
            <span class="famos-status-pill @GetStagePillClass(_opp.LifecycleStage)">
                @GetStageLabel(_opp.LifecycleStage)
            </span>
            <SignalChip Signal="_opp.DominantSignal" />
            @if (!string.IsNullOrEmpty(_opp.DominantSignalReason))
            {
                <MudText Typo="Typo.caption" Color="Color.Secondary">@_opp.DominantSignalReason</MudText>
            }
        </div>
    </div>
    @if (!_opp.IsClosed)
    {
        <div style="display: flex; gap: 8px;">
            <MudButton Variant="Variant.Outlined" Color="Color.Default" Size="Size.Small"
                       Class="famos-btn-outline-sm" OnClick="ParkOpportunity">Park</MudButton>
            <MudButton Variant="Variant.Outlined" Color="Color.Error" Size="Size.Small"
                       Class="famos-btn-outline-sm" OnClick="CloseOpportunity">Close</MudButton>
        </div>
    }
</div>
```

**Add helper method to `@code`:**

```csharp
private static string GetStagePillClass(LifecycleStage stage) => stage switch
{
    LifecycleStage.Intake           => "famos-pill-intake",
    LifecycleStage.UnderwritingPrep => "famos-pill-review",
    LifecycleStage.Marketed         => "famos-pill-sub",
    LifecycleStage.QuotesReceived   => "famos-pill-quotes",
    LifecycleStage.ClientDecision   => "famos-pill-prop",
    LifecycleStage.Binding          => "famos-pill-binding",
    LifecycleStage.Bound            => "famos-pill-bound",
    _                               => "famos-pill-default",
};
```

**Replace the Activity section header:**

```razor
<div class="famos-card-title mt-5 mb-3">Activity</div>
```

---

## 6. New / Modified Shared Components

### 6.1 NEW: `StatCard.razor`

**File:** `src/FamOs.Web/Components/Shared/StatCard.razor`

```razor
@namespace FamOs.Web.Components.Shared

<div class="famos-kpi-card @AccentClass">
    <div class="famos-kpi-label">@Label</div>
    <div class="famos-kpi-value">@Value</div>
    @if (!string.IsNullOrEmpty(Sub))
    {
        <div class="famos-kpi-sub">@Sub</div>
    }
    @if (!string.IsNullOrEmpty(Trend))
    {
        <div class="famos-kpi-trend @TrendClass">@Trend</div>
    }
</div>

@code {
    /// <summary>Short label above the number. Use UPPERCASE short names.</summary>
    [Parameter, EditorRequired] public string Label { get; set; } = "";

    /// <summary>The large displayed value (number, $amount, etc).</summary>
    [Parameter, EditorRequired] public string Value { get; set; } = "";

    /// <summary>Small subtitle below the value.</summary>
    [Parameter] public string Sub { get; set; } = "";

    /// <summary>Optional trend text (e.g. "+18%", "3 urgent").</summary>
    [Parameter] public string Trend { get; set; } = "";

    /// <summary>
    /// CSS modifier for the left accent bar color.
    /// Valid values: kpi-navy | kpi-sky | kpi-green | kpi-amber | kpi-red | kpi-blue
    /// </summary>
    [Parameter] public string AccentClass { get; set; } = "kpi-navy";

    /// <summary>CSS class for the trend badge. Valid: trend-up | trend-warn | trend-flat.</summary>
    [Parameter] public string TrendClass { get; set; } = "trend-flat";
}
```

### 6.2 MODIFIED: `OpportunityCard.razor`

**File:** `src/FamOs.Web/Components/Shared/OpportunityCard.razor`

Replace `MudCard` wrapper with the `.famos-kcard` CSS class treatment:

```razor
@using FamOs.Web.Data.Entities
@inject NavigationManager Nav

<div class="famos-kcard" @onclick="NavigateToOpportunity">
    <div class="famos-kcard-name">@Opportunity.Name</div>
    @if (Opportunity.EstimatedPremium.HasValue)
    {
        <div class="famos-kcard-detail">$@Opportunity.EstimatedPremium.Value.ToString("N0")</div>
    }
    @if (Opportunity.EffectiveDateTarget.HasValue)
    {
        <div class="famos-kcard-detail">Eff: @Opportunity.EffectiveDateTarget.Value.ToString("MMM d, yyyy")</div>
    }
    <div class="famos-kcard-footer">
        <SignalChip Signal="Opportunity.DominantSignal" />
    </div>
</div>

@code {
    [Parameter, EditorRequired] public Opportunity Opportunity { get; set; } = default!;
    private void NavigateToOpportunity() => Nav.NavigateTo("/opportunity/" + Opportunity.Id);
}
```

### 6.3 MODIFIED: `SignalChip.razor`

**File:** `src/FamOs.Web/Components/Shared/SignalChip.razor`

No structural changes needed — the `.famos-signal-chip` CSS classes are updated in Section 7. The component logic stays identical. The only change is the chip now uses `border-radius: 20px` (pill style) instead of `border-radius: 4px`.

---

## 7. CSS/Style Additions

### 7.1 `App.razor` — `<HeadOutlet>` addition (none needed beyond font import)

### 7.2 `famos.css` — Full Replacement

**File:** `src/FamOs.Web/wwwroot/css/famos.css`

Replace entire contents with:

```css
/* ============================================================
   FAM OS — Application Styles
   Sprint 3 Restyling — matches IAAPA Portal v2 mockup
   Extends FipShared fip-tokens.css
   ============================================================ */

/* ── PAGE HEADER ─────────────────────────────────────────── */
.famos-page-header { margin-bottom: 20px; }

.famos-page-header-row {
    display: flex;
    justify-content: space-between;
    align-items: flex-start;
}

.famos-page-h2 {
    font-family: 'Fraunces', Georgia, serif;
    font-size: 23px;
    font-weight: 400;
    color: #002050;
    letter-spacing: -0.3px;
    line-height: 1.2;
    margin: 0;
}

.famos-page-sub {
    font-size: 12.5px;
    color: #6b7585;
    margin-top: 4px;
    margin-bottom: 0;
}

/* ── KPI STAT CARDS ──────────────────────────────────────── */
.famos-kpi-grid {
    display: grid;
    grid-template-columns: repeat(4, 1fr);
    gap: 13px;
    margin-bottom: 20px;
}

.famos-kpi-card {
    background: #ffffff;
    border-radius: 12px;
    padding: 15px 16px;
    border: 1px solid #e2e6ed;
    position: relative;
    overflow: hidden;
}

/* Left accent bar — injected via ::before on modifier class */
.famos-kpi-card::before {
    content: "";
    position: absolute;
    top: 0; left: 0;
    width: 4px;
    height: 100%;
}

.kpi-navy::before   { background: #002050; }
.kpi-sky::before    { background: #0090d0; }
.kpi-green::before  { background: #059669; }
.kpi-amber::before  { background: #f0a010; }
.kpi-red::before    { background: #DC2626; }
.kpi-blue::before   { background: #2563EB; }

.famos-kpi-label {
    font-size: 10px;
    font-weight: 700;
    color: #6b7585;
    text-transform: uppercase;
    letter-spacing: 0.7px;
}

.famos-kpi-value {
    font-family: 'Fraunces', Georgia, serif;
    font-size: 30px;
    color: #002050;
    line-height: 1.1;
    margin: 3px 0;
}

.famos-kpi-sub {
    font-size: 11px;
    color: #6b7585;
}

.famos-kpi-trend {
    position: absolute;
    top: 14px; right: 12px;
    font-size: 10px;
    font-weight: 700;
    padding: 2px 7px;
    border-radius: 20px;
}

.trend-up   { background: #d1fae5; color: #065f46; }
.trend-flat { background: #f0f3f8; color: #6b7a90; }
.trend-warn { background: #fef3c7; color: #92400e; }

/* ── STANDARD CARDS ──────────────────────────────────────── */
.famos-card {
    background: #ffffff;
    border-radius: 12px;
    border: 1px solid #e2e6ed;
    padding: 16px 18px;
}

.famos-card-title {
    font-size: 12.5px;
    font-weight: 700;
    color: #002050;
    margin-bottom: 12px;
    display: flex;
    align-items: center;
    justify-content: space-between;
}

/* ── PIPELINE BOARD ──────────────────────────────────────── */
.famos-pipeline-board {
    display: flex;
    gap: 12px;
    overflow-x: auto;
    padding-bottom: 8px;
    min-height: 520px;
}

.famos-pipeline-column {
    flex: 1;
    min-width: 170px;
    display: flex;
    flex-direction: column;
}

.famos-pipeline-column-header {
    display: flex;
    align-items: center;
    gap: 6px;
    padding: 9px 10px;
    margin-bottom: 8px;
    border-radius: 10px;
    background: #ffffff;
    border: 1px solid #e2e6ed;
}

.famos-kcol-dot {
    width: 9px;
    height: 9px;
    border-radius: 50%;
    flex-shrink: 0;
    display: inline-block;
}

.famos-kcol-label {
    font-size: 10.5px;
    font-weight: 700;
    text-transform: uppercase;
    letter-spacing: 0.6px;
    color: #3a4250;
    flex: 1;
}

.famos-kcol-count {
    font-size: 10px;
    font-weight: 700;
    color: #6b7585;
    background: #f2f4f7;
    padding: 2px 7px;
    border-radius: 8px;
}

/* ── KANBAN CARDS (OpportunityCard) ─────────────────────── */
.famos-kcard {
    background: #ffffff;
    border: 1px solid #e2e6ed;
    border-radius: 10px;
    padding: 11px;
    cursor: pointer;
    transition: all 0.15s;
    margin-bottom: 8px;
}

.famos-kcard:hover {
    border-color: #0090d0;
    box-shadow: 0 4px 12px rgba(0,144,208,0.10);
}

.famos-kcard-name {
    font-weight: 700;
    font-size: 12.5px;
    color: #002050;
    margin-bottom: 2px;
    line-height: 1.3;
}

.famos-kcard-detail {
    font-size: 11px;
    color: #6b7585;
    margin-bottom: 4px;
}

.famos-kcard-footer {
    display: flex;
    align-items: center;
    justify-content: space-between;
    margin-top: 7px;
}

.famos-kcard-premium {
    font-weight: 700;
    font-size: 11px;
    color: #059669;
}

/* ── SIGNAL CHIPS ────────────────────────────────────────── */
.famos-signal-chip {
    font-size: 10.5px;
    font-weight: 700;
    padding: 2px 9px;
    border-radius: 20px;
    display: inline-block;
}

.famos-signal-time-risk        { background: #fee2e2; color: #991b1b; }
.famos-signal-waiting-on-client{ background: #fef3c7; color: #92400e; }
.famos-signal-decision-required{ background: #fed7aa; color: #9a3412; }
.famos-signal-waiting-on-market{ background: #ede9fe; color: #5b21b6; }
.famos-signal-awaiting-client  { background: #dbeafe; color: #1e40af; }
.famos-signal-binding          { background: #d1fae5; color: #065f46; }
.famos-signal-underwriting     { background: #e0f2fe; color: #075985; }
.famos-signal-post-bind        { background: #ccfbf1; color: #134e4a; }
.famos-signal-parked           { background: #f3f4f6; color: #4b5563; }

/* ── STAGE STATUS PILLS (OpportunityWorkspace) ───────────── */
.famos-status-pill {
    font-size: 10.5px;
    font-weight: 700;
    padding: 2px 9px;
    border-radius: 20px;
    display: inline-block;
}

.famos-pill-intake   { background: #dbeafe; color: #1d4ed8; }
.famos-pill-review   { background: #ede9fe; color: #6d28d9; }
.famos-pill-sub      { background: #fef3c7; color: #92400e; }
.famos-pill-quotes   { background: #e0f2fe; color: #0369a1; }
.famos-pill-prop     { background: #fdf4ff; color: #9333ea; }
.famos-pill-binding  { background: #e0f2fe; color: #0090d0; }
.famos-pill-bound    { background: #d1fae5; color: #065f46; }
.famos-pill-default  { background: #f3f4f6; color: #4b5563; }

/* ── SIDEBAR NAVIGATION ──────────────────────────────────── */
/* Custom nav items replace MudNavMenu treatment */
.famos-nav-item {
    display: flex;
    align-items: center;
    gap: 8px;
    padding: 7px 10px;
    border-radius: 7px;
    cursor: pointer;
    font-size: 12.5px;
    font-weight: 500;
    color: rgba(255,255,255,0.6);
    transition: all 0.15s;
    border-left: 2px solid transparent;
    margin-bottom: 1px;
    text-decoration: none;
}

.famos-nav-item:hover {
    background: rgba(255,255,255,0.05);
    color: rgba(255,255,255,0.85);
    text-decoration: none;
}

.famos-nav-item--active,
.famos-nav-item.active {
    background: rgba(0,144,208,0.13);
    color: #ffffff !important;
    border-left-color: #0090d0;
}

.famos-nav-item--disabled {
    opacity: 0.3;
    cursor: not-allowed;
    pointer-events: none;
}

.famos-nav-icon {
    display: flex;
    align-items: center;
    justify-content: center;
    width: 16px;
    height: 16px;
    flex-shrink: 0;
}

/* Scale down MudIcon in nav */
.famos-nav-icon .mud-icon-root {
    font-size: 16px !important;
    width: 16px !important;
    height: 16px !important;
}

.famos-nav-badge {
    margin-left: auto;
    font-size: 9.5px;
    font-weight: 700;
    background: #0090d0;
    color: #fff;
    padding: 1px 7px;
    border-radius: 10px;
    flex-shrink: 0;
}

/* ── DRAWER FOOTER ───────────────────────────────────────── */
.fip-drawer-footer {
    padding: 12px 16px;
    border-top: 1px solid rgba(255,255,255,0.07);
    color: rgba(255,255,255,0.4);
    font-size: 11px;
}

/* ── BUTTON HELPERS ──────────────────────────────────────── */
/* These override MudButton padding/font for non-default variants */
.famos-btn-primary {
    border-radius: 7px !important;
    font-size: 12.5px !important;
    font-weight: 600 !important;
    padding: 7px 13px !important;
}

.famos-btn-outline-sm {
    border-radius: 7px !important;
    font-size: 11.5px !important;
    font-weight: 600 !important;
    padding: 5px 10px !important;
    border-width: 1.5px !important;
    border-color: #e2e6ed !important;
}

/* ── PANEL CARDS (Stage Panels: IntakePanel, etc.) ───────── */
/* Override MudCard elevation inside stage panels */
.mud-card {
    border-radius: 12px !important;
    border: 1px solid #e2e6ed !important;
    box-shadow: none !important;
}

.mud-card:hover {
    box-shadow: 0 2px 8px rgba(0,0,0,0.06) !important;
}

/* ── MudBlazor GLOBAL OVERRIDES ──────────────────────────── */

/* Drawer background — MudBlazor v7 class */
.mud-drawer {
    background-color: #002050 !important;
}

/* Drawer header white logo area is handled inline in razor */

/* Remove MudNavMenu default padding/margin interference */
.mud-nav-menu {
    padding: 0 !important;
}

/* MudTimeline in activity log */
.mud-timeline-item-content {
    padding-bottom: 12px;
}

/* ── RESPONSIVE ──────────────────────────────────────────── */
@media (max-width: 960px) {
    .famos-kpi-grid {
        grid-template-columns: repeat(2, 1fr);
    }
    .famos-pipeline-board {
        min-height: 400px;
    }
}

@media (max-width: 600px) {
    .famos-kpi-grid {
        grid-template-columns: 1fr 1fr;
        gap: 8px;
    }
    .famos-page-h2 {
        font-size: 18px;
    }
}
```

### 7.3 `MainLayout.razor.css` — No changes needed

The existing `.fip-drawer-footer` class remains but is now superseded by inline styles on the new footer block. No change needed here.

---

## 8. What NOT to Touch

The following are **completely off-limits** for Sprint 3:

### Domain / Business Logic
- `FamOs.Web/Data/` — all entity files, DbContext, migrations
- `FamOs.Web/Services/` — all service classes
- `FamOs.Web/Services/LifecycleCommandService.cs` — sacred, do not touch
- `FamOs.Web/Services/UserSessionService.cs`
- `FamOs.Web/Services/OpportunityService.cs`
- Any `@code { }` block logic in panels (PursueOpportunity, ParkOpportunity, etc.)
- Domain exceptions, validation rules

### Shared Infrastructure
- `FipShared/` project — do not modify
- `fip-tokens.css` — read-only
- `FipNavBar` component — shared across FIP apps, do not style
- `FipModule` enum, `IWebHostEnvironment` usage
- Auth flows, `RedirectToLogin.razor`, `Routes.razor`

### Panel Business Logic
- `Panels/*.razor` — only `IntakePanel.razor` may receive minor card-wrapper style changes (see below)
- Do NOT change any `@code` section in panels
- Do NOT remove `Pursue/Park/Close/Advance` action bindings
- Do NOT change Lifecycle service call patterns

### Panel Cards — Allowed Minimal Touch
The panel razors may have `MudCard` wrappers styled with inline `Elevation="1"`. That's fine — the global `.mud-card` CSS override in `famos.css` applies automatically. No panel razor edits needed for styling.

---

## 9. Acceptance Criteria

Tony implements, Fred verifies in browser. Check each item:

### Typography
- [ ] Page headings on Dashboard, Pipeline, and OpportunityWorkspace render in **Fraunces serif** (cursive-adjacent letterforms, not Inter)
- [ ] All body text, nav items, buttons render in **Plus Jakarta Sans** (rounded, not Inter)
- [ ] KPI number values on Dashboard render in **Fraunces** at ~30px

### Colors & Palette
- [ ] Sidebar background is `#002050` (deep navy), distinct from current `#1a2332`
- [ ] Page background is `#f2f4f7` (cream/light gray), not white
- [ ] Card backgrounds are `#ffffff` (white) on the cream background — clear layering
- [ ] Active nav item has sky-blue left border and `rgba(0,144,208,0.13)` background
- [ ] Primary button (New Opportunity, Pursue, etc.) is `#002050` navy, not teal

### KPI Cards
- [ ] Dashboard shows 4 KPI cards with **4px left accent bars** in navy/red/amber/green
- [ ] KPI card label is UPPERCASE, 10px, muted
- [ ] KPI card value uses Fraunces serif, large (30px)

### Pipeline Board
- [ ] Each column header has a **colored dot** + uppercase label + count badge pill
- [ ] Column headers are white cards with border, not the old gray background
- [ ] Kanban cards hover shows sky-blue border (`#0090d0`) and soft sky shadow

### Status Chips & Signal Chips
- [ ] All signal chips are **pill-shaped** (`border-radius: 20px`), not square corners
- [ ] Stage pills on OpportunityWorkspace header are correct colors per stage

### Navigation
- [ ] Nav items render as custom `<a>` elements (not MudNavLink), matching mockup treatment
- [ ] Nav section labels "Main" and "Coming Soon" are visible in uppercase muted style
- [ ] Drawer footer shows avatar circle (sky gradient) + user name + "FAM OS" role text
- [ ] Drawer header shows "FAM OS" in Fraunces serif with white background

### Cards
- [ ] All MudCards have `border-radius: 12px` and `border: 1px solid #e2e6ed`, no box-shadow at rest
- [ ] Card hover shows subtle elevation (2px lift)

### Buttons
- [ ] Buttons have `border-radius: 7px`, not rounded-full or square
- [ ] Button text is NOT all-caps
- [ ] Outline buttons use `border: 1.5px solid #e2e6ed`

### Layout
- [ ] Content area padding is `24px 28px` (matches mockup), not `pa-4` (16px)
- [ ] AppBar/topbar height is 54px (matches mockup), sidebar is 262px wide

---

## 10. File Manifest

### Files to MODIFY

| File | Change |
|---|---|
| `src/FamOs.Web/Theme/FipTheme.cs` | Replace `Create()` with new theme (Section 3) |
| `src/FamOs.Web/Components/App.razor` | Add Google Fonts link in `<head>` (Section 4.1) |
| `src/FamOs.Web/Components/Layout/MainLayout.razor` | Update drawer header, footer, main content padding (Section 4.2) |
| `src/FamOs.Web/Components/Layout/NavMenu.razor` | Replace with custom nav markup (Section 4.3) |
| `src/FamOs.Web/Components/Pages/Dashboard.razor` | Page header, stat cards → StatCard, remove bottom button (Section 5.1) |
| `src/FamOs.Web/Components/Pages/Pipeline.razor` | Page header, column headers with dot/label/count, add GetStageColor (Section 5.2) |
| `src/FamOs.Web/Components/Pages/Opportunity/OpportunityWorkspace.razor` | Page header block, stage pill class, activity heading (Section 5.3) |
| `src/FamOs.Web/Components/Shared/OpportunityCard.razor` | Replace MudCard with famos-kcard div (Section 6.2) |
| `src/FamOs.Web/wwwroot/css/famos.css` | Full replacement (Section 7.2) |

### Files to CREATE

| File | Purpose |
|---|---|
| `src/FamOs.Web/Components/Shared/StatCard.razor` | New KPI stat card component (Section 6.1) |

### Files NOT Modified (confirmed)

- `src/FamOs.Web/Components/Shared/SignalChip.razor` — CSS handles the pill style change; no razor change needed
- `src/FamOs.Web/Components/Layout/MainLayout.razor.css` — kept as-is
- `src/FamOs.Web/Components/App.razor` — only `<head>` gets font import
- All `Panels/*.razor` files — zero changes
- All `Dialogs/*.razor` files — zero changes
- All `Services/*.cs` files — zero changes
- All `Data/*.cs` files — zero changes
- `FipShared` project — zero changes
- `fip-tokens.css` — zero changes

---

## Implementation Notes for Tony

1. **Do the theme first** — `FipTheme.cs` change flows into all MudBlazor components automatically. Build and verify in browser before touching any razor files.

2. **Font import is critical** — `App.razor` gets the Google Font link. Without it, Fraunces falls back to serif which still looks decent, but Plus Jakarta Sans won't load. Do this second.

3. **CSS file is a full replacement** — don't try to merge or append. The Sprint 2 pipeline board styles are fully replicated in the new CSS with improvements. Replace the file entirely.

4. **NavMenu uses Blazor `NavLink`** (not `MudNavLink`) so active class detection works via `ActiveClass="famos-nav-item--active"`. This is standard Blazor routing — it works without JavaScript.

5. **StatCard is a simple parameter-driven component** — it has no `@inject` dependencies. Drop it in `Shared/` and it just works.

6. **The `GetStageColor` helper on Pipeline.razor** is a private static method added to the existing `@code { }` block — add it after `GetStageCount`.

7. **The `GetStagePillClass` helper on OpportunityWorkspace.razor** similarly goes into the existing `@code { }` block.

8. **Do not adjust `DashboardSummary`** — `_summary.TotalActive`, `_summary.TimeRiskCount`, `_summary.DecisionNeeded`, `_summary.BoundThisMonth` are the four existing properties. StatCard just renders them.

9. **MudCard global override** — the `.mud-card` override in `famos.css` uses `!important` to beat MudBlazor specificity. This is intentional and safe for this scope.

10. **Build order:** Theme → App.razor → CSS → NavMenu → Layout → Dashboard → Pipeline → OpportunityWorkspace → StatCard → OpportunityCard. Run `dotnet build` after each file group.

---

*Sprint 3 is UI-only. The pipeline doesn't care how pretty it looks — but the humans who use it do.*
