# WI#888 — FAM OS HOTFIX: MudTheme IndexOutOfRangeException (500 on all pages)

**Priority:** 1 (Critical — site is down)
**Tags:** famos; hotfix; mudblazor; theme

## Symptom
`https://famos.dev.fortressam.ai` returns 500 on every request.

## Root Cause (from logs)
```
System.IndexOutOfRangeException: Index was outside the bounds of the array.
   at System.Array.GetValue(Int32 index)
   at MudBlazor.MudThemeProvider.GenerateTheme(StringBuilder theme)
   at MudBlazor.MudThemeProvider.BuildTheme()
```

The Sprint 3 MudTheme in `FamOs.Web/Theme/FipTheme.cs` has a malformed array — likely in one of:
- `Palette` (missing required entries)
- `Shadows.Elevation[]` — MudBlazor v7 expects exactly 25 entries (index 0–24); fewer = IndexOutOfRange
- `ZIndex` properties
- `Typography` sub-arrays

## Fix
1. Open `fip/famos/src/FamOs.Web/Theme/FipTheme.cs`
2. Find the MudTheme definition from Sprint 3
3. Check `new Shadow()` / `Elevation` array — must have exactly 25 string entries
4. Check any other array-type theme properties for correct length
5. If Elevation array is the culprit, either populate all 25 entries or remove the Shadows override entirely (MudBlazor defaults are fine — Sprint 3 only needed color/font changes)

## Priority
Site is completely down. Fix, build, deploy immediately. Standing dev approval in effect.

## Build
- Monorepo: `~/projects/fip/`
- CodeBuild: `fip-famos-build`
- ECS: `famos-dev`
