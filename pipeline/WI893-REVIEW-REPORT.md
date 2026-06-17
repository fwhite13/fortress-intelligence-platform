# Review Report: WI893 — FAM OS Affinity Branding
**Reviewer:** Hawkeye (Clint Barton) — `code-reviewer`  
**Commit:** `ed39554`  
**Cycle:** 1  
**Date:** 2026-03-19  
**Verdict:** ⚠️ NEEDS-CHANGES

---

## Spec Compliance Checklist

### ✅ 1. `appsettings.json` — AffinityConfig Section
| Field | Expected | Actual | Status |
|---|---|---|---|
| AffinityId | "tig" | "tig" | ✅ |
| DisplayName | "Truckers Insurance Group" | "Truckers Insurance Group" | ✅ |
| PortalName | "TIG Dashboard" | "TIG Dashboard" | ✅ |
| LogoPath | "/images/affinity/tig-logo.svg" | "/images/affinity/tig-logo.svg" | ✅ |

---

### ✅ 2. `AffinityConfig.cs`
- Namespace: `FamOs.Web` ✅ (top-level, not nested)
- Properties present: `AffinityId`, `DisplayName`, `PortalName`, `LogoPath` (string), `PrimaryColor` (string?), `AccentColor` (string?) ✅
- Sensible defaults: `AffinityId="famos"`, `DisplayName="Fortress Affinity Management OS"`, `PortalName="FAM OS"`, `LogoPath=""` ✅
- Default `LogoPath=""` is intentional — MainLayout's null-guard (`string.IsNullOrEmpty`) handles this correctly ✅

---

### ✅ 3. `Program.cs` — DI Registration
```csharp
builder.Services.Configure<AffinityConfig>(
    builder.Configuration.GetSection("AffinityConfig"));
```
Present at lines 114–115. ✅

---

### ✅ 4. `MainLayout.razor` — Branding & Cleanup (with one issue — see findings)

| Check | Status |
|---|---|
| FipNavBar completely removed (no leftover attributes/partial tags) | ✅ |
| `@using FipShared.*` removed from file-level directives | ✅ |
| `@inject IWebHostEnvironment` removed | ✅ |
| `sb-logo` div with `<img>` using `@_affinity.LogoPath` / `@_affinity.DisplayName` | ✅ |
| `MudMainContent` has NO `padding-top: 54px` override | ✅ |
| `IOptions<AffinityConfig>` injected via `@inject` | ✅ |
| `_affinity` field initialized in `OnInitializedAsync` | ✅ |
| User footer text uses `@_affinity.PortalName` | ✅ |
| Null guard on `LogoPath` before rendering `<img>` | ✅ (`string.IsNullOrEmpty` check) |
| No hardcoded "FAM OS" strings | ✅ (both former occurrences replaced) |
| Stray FipShared refs in MainLayout directives | ✅ None |

**⚠️ Issue found — see Finding #1 below.**

---

### ✅ 5. `wwwroot/css/famos.css` — Sidebar Logo Classes
| Check | Status |
|---|---|
| `.sb-logo` class present | ✅ |
| `.sb-logo img` class present | ✅ |
| `.sb-logo-text` class present | ✅ |
| `height: 44px` on img | ✅ |
| `object-fit: contain` | ✅ |
| `object-position: left` | ✅ |

---

### ✅ 6. `Components/App.razor` — Title
`<title>TIG Dashboard</title>` ✅ (was "FAM OS" or similar)

---

## Regression Checks

| Check | Status | Notes |
|---|---|---|
| `@namespace` on `OpportunityWorkspace.razor` | ✅ | `FamOs.Web.Components.Pages` |
| `@namespace` on all Panels | ✅ | All 7 panels have `FamOs.Web.Components.Panels` |
| `_Imports.razor` unchanged by this commit | ✅ | Not touched in ed39554 |
| `_Imports.razor` Dialogs/Panels/Shared/Services usings intact | ✅ | All present |
| `MudDialogInstance` (not `IMudDialogInstance`) in dialogs | ✅ | Both dialog files use `MudDialogInstance` |
| `GoToPipeline()` in `Dashboard.razor` | ✅ | Present at line 51 |
| `Shadows.Elevation` NOT in `FipTheme.cs` | ✅ | Comment confirms intentional omission (WI890) |
| `wwwroot/images/affinity/tig-logo.svg` present | ✅ | File exists |

**Note on `_Imports.razor` FipShared usings:** `@using FipShared.Components` and `@using FipShared.Models` remain in `_Imports.razor` — these are **pre-existing** (introduced in WI870, not this commit). Not a regression from WI893.

---

## Findings

### 🔴 Finding #1 — `ToggleDrawer()` is Dead Code / Responsive Drawer Broken (Important)

**File:** `MainLayout.razor`, line 87  
**Issue:** `ToggleDrawer()` is defined but nothing calls it. `FipNavBar` previously provided the hamburger menu button (`OnMenuClick="ToggleDrawer"`). With `FipNavBar` removed, there is **no `MudAppBar`** and **no hamburger button** in the current layout.

**Impact:** On mobile/tablet viewports (below `Breakpoint.Md`), the `MudDrawer` starts closed and there is no UI affordance to open it. The app is effectively **unnavigable on mobile**. On desktop, the drawer defaults open (`_drawerOpen = true`) so it works — but the dead method is also a code quality issue.

**Fix required:**
1. Either add a `MudAppBar` with a hamburger `MudIconButton` that calls `ToggleDrawer()`, OR
2. Remove `ToggleDrawer()` and set `Variant="DrawerVariant.Persistent"` if mobile is explicitly out of scope for this sprint (and document it).

**Recommended fix (minimal):**
```razor
<MudLayout>
    <MudAppBar Elevation="0" Color="Color.Transparent" Dense="true">
        <MudIconButton Icon="@Icons.Material.Filled.Menu" Color="Color.Inherit" 
                       Edge="Edge.Start" OnClick="ToggleDrawer" />
    </MudAppBar>
    <MudDrawer ...>
```

Or if mobile is out of scope: remove `ToggleDrawer()` entirely and use `DrawerVariant.Persistent`.

---

## Summary

WI893 is clean and well-executed. All 6 primary spec items pass. The config-driven branding approach is solid — `IOptions<AffinityConfig>` injection is correct, the null guard on `LogoPath` is thoughtful, and the `MudMainContent` `padding-top` removal is clean. Regressions from WI870 and WI890 are intact.

One blocking issue: **`ToggleDrawer()` is dead code** — the hamburger mechanism was removed with `FipNavBar` and not replaced. This breaks responsive/mobile drawer behavior. Needs a fix before this ships.

**Verdict: NEEDS-CHANGES**  
Fix `ToggleDrawer()` dead code / mobile drawer access — then this is a PASS.
