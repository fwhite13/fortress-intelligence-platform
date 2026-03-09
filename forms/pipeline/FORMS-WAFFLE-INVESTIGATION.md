# FORMS Waffle Investigation

## Root Cause

**Stale pre-built publish artifacts.** The `Dockerfile.deploy` copies pre-compiled DLLs from `FortressFormTools.Web/publish/` instead of building from source. These publish artifacts were last rebuilt at commit `8247976` (Feb 27), but the MainLayout.razor with the waffle menu was modified in 4 subsequent commits (`c166fdc`, `a8c8071`, `37bf3bf`, etc.) **without ever rebuilding the publish directory**.

The compiled `FortressFormTools.Web.dll` in the publish directory (417KB, Feb 27) did NOT contain the latest Razor component changes. Blazor Server compiles Razor components into the DLL — so the stale DLL served an old version of MainLayout without the current waffle code.

### Why the code "existed" but wasn't visible:
- **Source code** (`MainLayout.razor`) ✅ Had the waffle
- **Compiled DLL** (`publish/FortressFormTools.Web.dll`) ❌ Stale — compiled from older source
- **Dockerfile.deploy** copies the stale DLL, not the source → live site shows old layout

### Key evidence:
- Old DLL: `b48a58c1` (417,280 bytes, Feb 27)
- New DLL: `20b829ab` (429,056 bytes, Mar 1)
- 12KB difference = multiple Razor component changes missing from deploy

## Fix Applied

Rebuilt publish artifacts from current source using:
```bash
dotnet publish FortressFormTools.Web/FortressFormTools.Web.csproj -c Release -o FortressFormTools.Web/publish /p:UseAppHost=false
```

Updated both `FortressFormTools.Web/publish/` and `publish/` directories with fresh build output.

## Build: succeeded ✅

## Commit: `bd48b3e`

## Recommendation

The `Dockerfile.deploy` pattern of committing pre-built artifacts is fragile and caused this exact issue. Consider:
1. **Always use `Dockerfile`** (multi-stage build from source) for deployments
2. **Or add a CI step** that rebuilds publish artifacts on every commit
3. **Remove publish/ from git tracking** — build artifacts shouldn't be in version control
