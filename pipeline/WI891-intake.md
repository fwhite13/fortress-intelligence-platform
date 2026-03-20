# WI#891 — FAM OS: Affinity-Branded Header + Config-Driven Branding System

**Priority:** 2 (High)
**Tags:** famos; branding; ui; sprint3; affinity; config

## Goal
Replace the FIP/FAM OS header with a config-driven affinity-branded experience. The first instance is TIG (Truckers Insurance Group). Each affinity group gets their own logo, portal name, and optionally their own color scheme.

## Reference
The IAAPA Portal mockup (IAAPA_Portal_v2_restyled.html) is the visual reference:
- Logo sits at the top of the LEFT SIDEBAR (not a top header bar) in a white-background `sb-logo` box
- Logo is an `<img>` tag: `max-width:100%; height:44px; object-fit:contain; object-position:left`
- Portal name appears in nav breadcrumb as "{Affinity Name} › Dashboard"
- The FIP top header bar is REMOVED entirely

## What to Build

### 1. Affinity Config Model
Add `AffinityConfig` to the data model (or appsettings for Phase 1):
```
AffinityId       string
DisplayName      string   // e.g. "Truckers Insurance Group"
PortalName       string   // e.g. "TIG Dashboard" (shown in nav/page titles)
LogoPath         string   // e.g. "/images/affinity/tig-logo.png"
PrimaryColor     string?  // optional CSS override
AccentColor      string?  // optional CSS override
```

For Phase 1, reading from `appsettings.json` or a static config is fine. A DB-backed config comes later.

### 2. Logo Asset
The TIG logo files are available on the build server at:
`/home/fredw/.openclaw/workspace/memory/projects/tig-dgt/Full Logo - Color/`
- `Full Logo - Color.svg` ← USE THIS (vector, best quality)
- `Full Logo - Color.png` (fallback)
- `Full Logo - Color.jpg` (fallback)

Copy the SVG to: `fip/famos/src/FamOs.Web/wwwroot/images/affinity/tig-logo.svg`

### 3. Layout Changes (MainLayout.razor / NavMenu.razor)
- REMOVE the FIP top header bar completely (the navy bar that says "FAM OS BETA")
- In the LEFT SIDEBAR, add a logo section at the top:
  ```html
  <div class="sb-logo">
    <img src="@affinityConfig.LogoPath" alt="@affinityConfig.DisplayName" style="height:44px;max-width:100%;object-fit:contain;object-position:left;" />
  </div>
  ```
- The breadcrumb/title in the nav should read: `@affinityConfig.PortalName` instead of "FAM OS"
- Page `<title>` tag should use `@affinityConfig.PortalName`

### 4. CSS for sb-logo
Add to `famos.css`:
```css
.sb-logo {
  padding: 16px 16px 14px;
  border-bottom: 1px solid rgba(255,255,255,0.08);
  background: white;
}
.sb-logo img {
  max-width: 100%;
  height: 44px;
  object-fit: contain;
  object-position: left;
}
```

### 5. TIG Config (appsettings.json or AffinityConfig.cs)
For Phase 1, hardcode TIG as the active affinity config:
```json
"AffinityConfig": {
  "AffinityId": "tig",
  "DisplayName": "Truckers Insurance Group",
  "PortalName": "TIG Dashboard",
  "LogoPath": "/images/affinity/tig-logo.svg"
}
```

## Acceptance Criteria
- [ ] FIP top header bar is gone
- [ ] TIG logo appears at top of left sidebar on white background, 44px tall
- [ ] Portal name reads "TIG Dashboard" in nav breadcrumb/page title (not "FAM OS")
- [ ] Page title tag updated
- [ ] No layout breakage on pipeline board, opportunity workspace, task center
- [ ] Config is driven by AffinityConfig (not hardcoded strings scattered throughout)
- [ ] Future affinity swap = change config only, no code changes

## Build
- Monorepo: `~/projects/fip/`
- Logo source: `/home/fredw/.openclaw/workspace/memory/projects/tig-dgt/Full Logo - Color/Full Logo - Color.svg`
- Copy to wwwroot before build

## Notes
- Do NOT change color scheme yet — TIG colors pending color extraction from logo
- Do NOT do multi-affinity switching UI yet — single active affinity from config only
- This is purely cosmetic + config scaffolding; no functional changes
