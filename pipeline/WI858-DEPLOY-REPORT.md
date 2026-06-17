# Deploy Report: WI858 — FfE Entra Auth Refactor

**Agent:** War Machine (Rhodey / `devops`)
**Date:** 2026-03-18T01:57 UTC
**Status:** 🔴 BLOCKED — Entra prereqs unconfirmed + wwwroot missing auth-dialog.html

---

## Pre-Deploy Snapshot

| Item | Value |
|------|-------|
| fred-dev task def revision | **118** |
| fait-prod task def revision | **32** |
| fip HEAD | `9d033d6` — fix(WI858): whoami adds authScheme field |
| fip HEAD-1 | `83011c0` — WI858: FAIT backend — Entra JWT validation + OID→userId mapping + whoami endpoint |
| fait-for-excel HEAD | `f8f1cff` — fix(WI858): auth-dialog root; remove storage dup; fix hardcoded URL |

---

## Rollback Plan (Documented Before Deploy)

If deploy had proceeded and needed rollback:

```bash
# Rollback fred-dev to revision 118
aws ecs update-service --cluster fortress-tools-cluster --service fred-dev \
  --task-definition fred-dev:118 --region us-east-1

# Rollback fait-prod to revision 32
aws ecs update-service --cluster fortress-tools-cluster --service fait-prod \
  --task-definition fait-prod:32 --region us-east-1
```

---

## Blocking Issues Found

### 🔴 BLOCKER 1 — Entra FfE.Access Scope Not Confirmed

**What's needed:** FfE.Access scope exposed on FIP app registration `887206bc-fac1-436a-a8ed-2150418d76c0`  
**API URI:** `api://887206bc-fac1-436a-a8ed-2150418d76c0/FfE.Access`

**Check performed:**
- `az` CLI: **not installed** on SteamServer
- ADO WI858 comments (all 5): **no comment from Fred confirming scope was added**
- ADO WI831 comments: **0 comments**, no confirmation there either

**Consequence if skipped:** MSAL `loginRedirect()` will fail with `AADSTS70011: The provided value for the input parameter 'scope' is not valid.`

---

### 🔴 BLOCKER 2 — Entra Redirect URIs Not Confirmed

**What's needed:** The following URIs added to the Web platform on the FIP app registration:
- `https://fait.dev.fortressam.ai/excel-addin/auth-dialog.html`
- `http://localhost:3000/excel-addin/auth-dialog.html`

**Check performed:** Same as Blocker 1 — no az CLI, no ADO confirmation.

**Consequence if skipped:** Entra will reject the redirect with `AADSTS50011: The redirect URI specified in the request does not match the redirect URIs configured for the application.`

---

### 🔴 BLOCKER 3 — wwwroot Missing auth-dialog.html (Stale dist/)

**What's needed:** `auth-dialog.html` + compiled `authDialog.js` in:
```
~/projects/fip/fait/src/FortressAI.Web/wwwroot/excel-addin/
```

**Current state of wwwroot/excel-addin/:**
```
assets/
commands.html
manifest.xml
public/
src/taskpane/index.html
```
**`auth-dialog.html` is ABSENT.**

**Root cause:** The `dist/` in `~/projects/fait-for-excel/` is stale — it was built BEFORE WI858 commits. The Vite config now has `auth-dialog` as an entry (`'auth-dialog': 'auth-dialog.html'`), but `npm run build` has not been re-run since those commits were added. The built dist has not been copied to wwwroot.

**The Dockerfile bakes wwwroot directly** (`COPY fait/src/ fait/src/`) — it does NOT run npm build. CodeBuild would ship an image with a missing `auth-dialog.html`, causing 404 when the dialog tries to load.

**Fix required (Tony must do this BEFORE CodeBuild):**
```bash
cd ~/projects/fait-for-excel
npm run build
# Then copy dist to wwwroot:
cp dist/auth-dialog.html ~/projects/fip/fait/src/FortressAI.Web/wwwroot/excel-addin/
# Copy compiled authDialog JS (from assets/):
cp dist/assets/authDialog*.js ~/projects/fip/fait/src/FortressAI.Web/wwwroot/excel-addin/assets/
# OR copy full dist to wwwroot (replacing old content):
rsync -av dist/ ~/projects/fip/fait/src/FortressAI.Web/wwwroot/excel-addin/
git -C ~/projects/fip add fait/src/FortressAI.Web/wwwroot/excel-addin/
git -C ~/projects/fip commit -m "WI858: copy compiled FfE dist to wwwroot (auth-dialog + assets)"
git -C ~/projects/fip push origin main
```

---

## ADO Comments Posted

| Comment ID | Time | Content |
|------------|------|---------|
| 724823 | 01:56:14 | DEPLOY STARTING — War Machine on site, prereqs to verify |
| 724825 | 01:57:09 | DEPLOY BLOCKED — az CLI unavailable, no ADO confirmation of Entra prereqs |

---

## Deploy Steps NOT Executed (Due to Blockers)

- [ ] Step 1: Push fip to GitHub — **NOT DONE** (blocked)
- [ ] Step 2: Push fait-for-excel to GitHub — **NOT DONE** (blocked)
- [ ] Step 3: Preflight script — **NOT DONE** (blocked)
- [ ] Step 4: Trigger CodeBuild `fip-fait-build` — **NOT DONE** (blocked)
- [ ] Step 5: Deploy fred-dev ECS — **NOT DONE** (blocked)
- [ ] Step 6: Health checks (fred-dev) — **NOT DONE** (blocked)
- [ ] Step 7: Deploy fait-prod ECS — **NOT DONE** (blocked)

---

## Required Actions Before Re-Deploy

### Fred (Entra Portal — Azure AD):
1. **Add FfE.Access scope** to app registration `887206bc-fac1-436a-a8ed-2150418d76c0`
   - In Entra Portal → App Registrations → FIP app → Expose an API
   - Scope name: `FfE.Access`
   - Scope URI: `api://887206bc-fac1-436a-a8ed-2150418d76c0/FfE.Access`
2. **Add redirect URIs** to Web platform:
   - `https://fait.dev.fortressam.ai/excel-addin/auth-dialog.html`
   - `http://localhost:3000/excel-addin/auth-dialog.html`
3. **Confirm in ADO WI858 comment** that both are done

### Tony (Code — Before CodeBuild):
1. Run `npm run build` in `~/projects/fait-for-excel/`
2. Copy compiled `auth-dialog.html` and `authDialog*.js` to `~/projects/fip/fait/src/FortressAI.Web/wwwroot/excel-addin/`
3. Commit to fip + push to GitHub
4. Confirm in ADO WI858 comment

### Rhodey (Re-Deploy — After Above Confirmed):
- Source env, verify task defs, trigger CodeBuild, deploy fred-dev, health checks, deploy fait-prod
- Current rollback targets: `fred-dev:118`, `fait-prod:32`

---

## Environment Notes

- `az` CLI: **not installed** on SteamServer
- AWS CLI: operational (credentials load from `fortress_tools/.env.deployer`)
- CodeBuild project: `fip-fait-build`
- ECS Cluster: `fortress-tools-cluster`
- ECR image: `fred-chat:kb-latest`

---

*Deploy not attempted. All blockers must be resolved before CodeBuild is triggered.*
*— War Machine*
