# Pipeline Completion: WI813

## Outcome: DEPLOYED ✅
**Date:** 2026-03-16  
**Total pipeline time:** ~99 minutes (11:06 → 12:45 EDT)

---

## What Shipped

Vite build foundation fix for FAIT for Excel:
- HTML entry points replacing IIFE bundle — `dist/src/taskpane/index.html` now produced with hashed ES module bundle
- `@microsoft/office-js` npm package removed (CDN-only loading retained)
- `OfficeRuntime.storage` fallback bug fixed in `settings.ts` + `storage.ts` (localStorage shim)
- `vite-plugin-mkcert` added for local HTTPS dev
- `manifest.xml` and `public/manifest.xml` updated to explicit taskpane path (`/excel-addin/src/taskpane/index.html`)
- `manifest.local.xml` created for local dev sideloading

**fred-dev:** ECS task def `fred-dev:118` | Image `fred-chat:kb-latest` sha256:0a4e5c06

---

## Pipeline Summary

| Stage | Status | Notes |
|-------|--------|-------|
| PLAN | ✅ | Reed Richards spec |
| BUILD | ✅ | Tony — 3 cycles (main build + manifest.local.xml fix + public/manifest.xml fix) |
| REVIEW | ✅ | Clint — PASS 2/30 (2 cycles; path deviation accepted) |
| SECURITY | ✅ | PASS — no findings |
| APPROVE | ✅ | Fred approved 12:09 |
| DEPLOY | ✅ | Rhodey — 2 cycles (full deploy + manifest static update → CodeBuild required) |
| VERIFY | ✅ | Natasha — PASS (2 cycles) |
| CONFIRM | ✅ | WI#813 → Done |

**Review cycles:** 2  
**Deploy cycles:** 2  
**Security findings:** None

---

## Issues Encountered

1. **Vite v8 path structure** — Spec anticipated `dist/taskpane/index.html`; Vite v8 (rolldown) produces `dist/src/taskpane/index.html`. Accepted — manifest URLs updated to match.
2. **`public/manifest.xml` not updated** — Tony updated root `manifest.xml` but Vite deploys from `public/`. Stale copy got into dist. Fixed in cycle 3.
3. **73 fip commits never pushed to GitHub** — Pre-existing. CodeBuild was building stale source. Rhodey pushed all commits during deploy.
4. **buildspec context mismatch (WI797 pre-existing)** — `fait/buildspec.yml` used `fait/` as Docker context; Dockerfile needed monorepo root for FipShared. Fixed by Rhodey in `bfcc11c`.
5. **Manifest is baked into Docker image** — Static file copy to wwwroot is insufficient for manifest changes. Future manifest updates require CodeBuild rebuild.

---

## Artifacts

```
pipeline/
  WI813-STATE.md
  WI813-BUILD-REPORT.md
  WI813-REVIEW-REPORT.md
  WI813-SECURITY-REPORT.md
  WI813-DEPLOY-REPORT.md
  WI813-QA-REPORT.md
  WI813-COMPLETION.md  ← this file
```

---

## Lessons for MEMORY.md

- **public/manifest.xml is the deployed copy** — Vite copies `public/` to `dist/` during build. Any file in `public/` that is also edited in the project root requires updating BOTH copies. Future tony briefs for FfE should call out: "if manifest.xml changes, update BOTH `manifest.xml` AND `public/manifest.xml`."
- **FAIT wwwroot is baked into Docker image** — Not a mounted volume. Static file changes require CodeBuild rebuild to go live.
