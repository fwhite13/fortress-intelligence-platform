# Build Report — ADO#1553: Add NEXUS Tile to FIP App Selector

**Date:** 2026-04-02  
**Engineer:** Tony Stark  
**Commit:** fd904fc  
**Branch:** main

---

## What Was Built

Added NEXUS as the fourth tile in the FIP app selector grid. Follows the identical `IsComingSoon` gate pattern used by FIRM and FORMS. Added `NexusUrl` to `appsettings.json` under the `Apps` section (which was created fresh — it did not previously exist).

---

## Files Changed

- `src/FortressIntelligencePlatform.Web/Components/Pages/Home.razor`  
  — Added NEXUS tile block after FORMS block. Uses `IsComingSoon("nexus")` gate, 📋 emoji, `Config["Apps:NexusUrl"]` href.

- `src/FortressIntelligencePlatform.Web/appsettings.json`  
  — Added `Apps` section with all four app URLs: FaitUrl, FirmUrl, FormsUrl, NexusUrl.  
  — **Note:** `Apps` section did not exist prior to this commit. All four URLs added together for completeness and consistency.

---

## Parallelization Used

No — single-file Razor change + single JSON config update. Sequential direct edits, no CC required.

---

## CC Sessions Run

None — trivial two-file change handled with direct edits.

---

## Acceptance Criteria Verification

- [x] NEXUS tile appears after FORMS in the app grid
- [x] `IsComingSoon("nexus")` gate controls Coming Soon vs live state
- [x] 📋 emoji used consistently in both branches of the conditional
- [x] `Apps:NexusUrl` config key wired to `https://nexus.fortressam.ai`
- [x] Build: **SUCCEEDED — 0 warnings, 0 errors**

---

## Known Edge Cases / Things to Scrutinize

- `appsettings.json` previously had no `Apps` section at all — the FAIT tile's `Config["Apps:FaitUrl"]` reference would have resolved to null at runtime before this commit. All four URLs are now present and correct.
- No `appsettings.Production.json` overrides exist in this repo (confirmed by scope of change). Production URLs will need to be set via ECS task definition environment variables or AWS SSM — standard FIP deployment pattern.

---

## How to Test Locally

```bash
cd /home/fredw/projects/fip
dotnet run --project fip/src/FortressIntelligencePlatform.Web/FortressIntelligencePlatform.Web.csproj
# Navigate to http://localhost:5000 — NEXUS tile should appear in the grid
# To test Coming Soon state: set FIP__ComingSoonApps=nexus in env
```

---

## Build Output

```
Build succeeded.
    0 Warning(s)
    0 Error(s)

Time Elapsed 00:00:03.07
```
