# WI830 Deploy Report
**Agent:** War Machine (James Rhodes) — `devops`
**Date:** 2026-03-17
**Deploy time:** 03:52 – 04:00 EDT (~8 minutes)

---

## Summary

WI830 — FfP Sprint 3 (final sprint WI) deployed successfully to **fred-dev** and **fait-prod**. Tables, template injection, and chart-as-image are live. PowerPointApi 1.8 manifest active on both environments. FfE bundle unchanged (0jKgr1fV).

---

## Pre-Deploy Snapshot

| Service    | Task Definition | Running |
|------------|----------------|---------|
| fred-dev   | fred-dev:118   | 1       |
| fait-prod  | fait-prod:31   | 1       |

- fip HEAD at deploy start: `999bf25` (WI830 feature commit)
- fip HEAD at deploy end: `4660f52` (wwwroot dist update commit)

---

## Rollback Plan (documented pre-deploy)

```bash
# fred-dev → fred-dev:118
aws ecs update-service --cluster fortress-tools-cluster --service fred-dev \
  --task-definition fred-dev:118 --force-new-deployment \
  --region us-east-1 --profile fortress-tools-deployer

# fait-prod → fait-prod:31
aws ecs update-service --cluster fortress-tools-cluster --service fait-prod \
  --task-definition fait-prod:31 --force-new-deployment \
  --region us-east-1 --profile fortress-tools-deployer

cd ~/projects/fip
git checkout HEAD~1 -- fait/src/FortressAI.Web/wwwroot/ppt-addin/
git commit -m "rollback: restore ppt-addin to pre-WI830"
git push origin main
# Then trigger CodeBuild manually (project: fip-fait-build)
```

---

## Step Table

| Step | Action                                         | Status    | Notes |
|------|------------------------------------------------|-----------|-------|
| 0    | Pre-deploy snapshot                            | ✅ DONE   | fred-dev:118, fait-prod:31, both running=1 |
| 0    | Rollback plan documented                       | ✅ DONE   | See above |
| 1    | ADO: DEPLOY STARTING comment                   | ✅ DONE   | Comment ID 723965 |
| 2    | `npm install` (chart.js new in Sprint 3)       | ✅ DONE   | Clean, 0 vulnerabilities |
| 2    | `npm run build`                                | ✅ DONE   | 437.49 KB taskpane.js (expected) |
| 3    | Copy dist/ → wwwroot/ppt-addin/                | ✅ DONE   | manifest.xml PowerPointApi MinVersion="1.8" confirmed |
| 4    | `git commit + push` to fip main                | ✅ DONE   | Commit: `4660f52` |
| 5    | CodeBuild: fip-fait-build (manual start)       | ✅ DONE   | SUCCEEDED — `fip-fait-build:630a3ed6-4668-47d2-b34b-d758f17d9625` |
| 6    | fred-dev ECS stabilize                         | ✅ DONE   | fred-dev:118 — STABLE (buildspec force-deploys fred-dev, kb-latest refreshed) |
| 7    | Register fait-prod:32                          | ✅ DONE   | Image: `fred-chat:4660f5294402d30e586ee7c4e2c88d61530258e7` |
| 8    | fait-prod ECS update to :32                    | ✅ DONE   | fait-prod:32 — STABLE |
| 9    | Health checks — fred-dev                       | ✅ DONE   | All 200s — see below |
| 9    | Health checks — fait-prod                      | ✅ DONE   | All 200s — see below |
| 10   | ADO: DEPLOY COMPLETE comment                   | ✅ DONE   | Comment ID 723967 |

---

## Health Check Results

### fred-dev (`fait.dev.fortressam.ai`)

| Check | Result |
|-------|--------|
| `/health` | **200** ✅ |
| `/_content/FipShared/css/fip-tokens.css` | **200** ✅ |
| `/excel-addin/src/taskpane/index.html` | **200** ✅ |
| FfE bundle hash | **taskpane-0jKgr1fV.js** ✅ (unchanged) |
| `/ppt-addin/src/taskpane/index.html` | **200** ✅ |
| manifest.xml PowerPointApi | **`MinVersion="1.8"`** ✅ |

### fait-prod (`fait.fortressam.ai`)

| Check | Result |
|-------|--------|
| `/health` | **200** ✅ |
| `/ppt-addin/src/taskpane/index.html` | **200** ✅ |
| manifest.xml PowerPointApi | **`MinVersion="1.8"`** ✅ |

---

## Deployment Artifacts

| Artifact | Value |
|----------|-------|
| CodeBuild build ID | `fip-fait-build:630a3ed6-4668-47d2-b34b-d758f17d9625` |
| ECR image tag | `fred-chat:4660f5294402d30e586ee7c4e2c88d61530258e7` |
| ECR image pushed | 2026-03-17 03:55:59 EDT |
| fip commit (wwwroot update) | `4660f52` |
| fred-dev task def | `fred-dev:118` (kb-latest refreshed in-place) |
| fait-prod task def | `fait-prod:32` |
| FfP bundle size | 437.49 KB taskpane.js (gzip: 140.43 KB) |
| FfE bundle hash | `0jKgr1fV` (unchanged ✅) |

---

## Ops Notes

- `npm install` required before build (chart.js new dependency in Sprint 3)
- CodeBuild does NOT auto-trigger from GitHub push — started manually via env vars (not `--profile`, which returns AccessDenied for `codebuild:StartBuild`)
- fred-dev stays on `:118` — buildspec refreshes `kb-latest` image in-place
- fait-prod registered as `:32` with explicit commit-tagged image

---

## What Shipped

- `pptTableWriter.ts` — `createTableOnSlide` (PowerPoint table insertion)
- `pptChartRenderer.ts` — Chart.js canvas render → chart-as-image in slides
- `pptTemplateService.ts` — `insertSlidesFromBase64` (template injection)
- `TablePreview.tsx`, `ChartPreview.tsx`, `TemplateGallery.tsx` — UI components
- `/table`, `/chart`, `/template` routes in SlashCommandPicker
- Manifests updated to PowerPointApi 1.8

---

**Verdict: ✅ DEPLOY COMPLETE — FfP Sprint 3 fully live. Natasha to verify.**
