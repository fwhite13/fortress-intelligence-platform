# Pipeline Completion: WI828

## Outcome: DEPLOYED ✅
**Date:** 2026-03-17
**Total pipeline time:** ~38 minutes (02:03 build → 02:41 confirm)

---

## What Shipped

FfP Sprint 1: Foundation + Core Chat + Apply to Shape. First-ever FfP deployment.

New repo `~/projects/fip/fait-for-powerpoint/` with 32 files:
- **Manifests** — `public/manifest.xml` + `manifest.local.xml` — Presentation host, PowerPointApi 1.5, GUID `b2c3d4e5-f6a7-8901-bcde-f12345678902`, base `/ppt-addin/`
- **`vite.config.ts`** — Port 3001, HTTPS, `base: '/ppt-addin/'`
- **`pptReader.ts`** — `getSlideContext()` reads title, body text, speaker notes via deep-path `items/notes/textFrame/textRange/text` load
- **`pptWriter.ts`** — `applyTextToShape()` via `PowerPoint.run()`, `textFrame.hasText` guard
- **`usePptContext.ts`** — polling hook (2s interval)
- **`ChatPanel.tsx`** — adapted from FfE, PowerPoint context injection, Apply to Shape action
- **`ShapePreview.tsx`** — confirm dialog for Apply to Shape
- **`SettingsPanel.tsx`**, **`App.tsx`**, **`useChat.ts`** — ported/adapted from FfE
- Infrastructure: `faitApi.ts`, `settings.ts`, `global.css` — exact copies from FfE

**fred-dev:** `fred-dev:118` | **fait-prod:** `fait-prod:30` | fip commit `8137304` | Bundle `taskpane.js`

---

## Pipeline Summary

| Stage | Status | Notes |
|-------|--------|-------|
| PLAN | ✅ | Spec: FFP-SPRINT1-SPEC.md |
| BUILD | ✅ | 1 cycle; commit 99d477a; 38 modules, 0 TS errors; 18 tasks |
| REVIEW C1 | ❌ | NEEDS-CHANGES: manifest.local.xml URLs, notes load path, pptWriter dead guard |
| BUILD C2 | ✅ | commit 240c3b3 — 3 surgical fixes |
| REVIEW C2 | ✅ | PASS — 4/4 clean |
| SECURITY | ✅ | PASS — dangerouslySetInnerHTML safe (simpleMarkdown sanitizes first) |
| APPROVE | ✅ | Standing approval |
| DEPLOY | ✅ | 1 cycle; CodeBuild SUCCEEDED; 8/8 health checks 200; no Dockerfile changes needed |
| VERIFY | ✅ | Natasha — PASS; FfE regression clean; FfP rendering on both envs |
| CONFIRM | ✅ | WI#828 → Done |

**Review cycles:** 2 | **Deploy cycles:** 1 | **Security findings:** None blocking

---

## Lessons / Notes

- **First FfP deploy.** `/ppt-addin/` is live on both fred-dev and fait-prod. FAIT Dockerfile's `COPY fait/src/` already covered the new wwwroot subdirectory — no Dockerfile changes needed.
- **manifest.local.xml localhost URLs need `/ppt-addin/` prefix.** Vite 8 `base` applies to dev server too. This is a silent failure — pages 404 without obvious error message.
- **Speaker notes require deep-path load.** `'items/notes/textFrame/textRange/text'` must be in `load()` before `ctx.sync()`. Silent failure otherwise — notes always return empty and the error is swallowed by catch.
- **Office.js proxy guard:** `!target.textFrame` is always false — proxies are never null. Use `!target.textFrame.hasText` for the actual semantic check.
- **FfP Sprint 1 bundle uses no hash** (`taskpane.js`) — Sprint 1 convention. Future sprints may add hashing.
- FfP functional testing (PowerPoint Online: chat, slide context, Apply to Shape) MANUAL REQUIRED.
