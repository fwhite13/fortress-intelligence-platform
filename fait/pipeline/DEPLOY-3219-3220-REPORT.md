# Deploy Report — ADO#3219 + ADO#3220

**Date:** 2026-05-10  
**Agent:** War Machine (James Rhodes) — DevOps  
**Target:** `fred-dev`  
**Deployed by:** rhodey-deploy-3219-3220 subagent

---

## WIs Included in This Build

| ADO    | Title                                 | Status   |
|--------|---------------------------------------|----------|
| #3219  | Dialog width fix                      | ✅ Included |
| #3220  | RunNowAsync bug fixes                 | ✅ Included |
| #3214  | ProtectedSessionStorage resumption guard | ❌ Excluded — review not yet PASS at build time |

**Build commit:** `c9ba9fce` (feat(fait#3219,3220): dialog width fix + RunNowAsync bug fixes)  
**Decision:** `REVIEW-3214-REPORT.md` was not present / did not contain PASS → built from `c9ba9fce` only.

---

## Build

- **Dockerfile:** `fait/Dockerfile.debian`
- **Build context:** `/home/fredw/projects/fip` (monorepo root)
- **Image tag:** `fred-chat:c9ba9fce`
- **ECR repo:** `742932328420.dkr.ecr.us-east-1.amazonaws.com/fred-chat`
- **Image digest:** `sha256:70068aad7dd590794a391fb58ac18bfad8dc00fa844d6ea50fcc9629f18a4c83`
- **Build result:** ✅ PASS (warnings only, no errors)
- **--no-cache:** Yes

---

## Deployment

| Step | Result |
|------|--------|
| Pre-flight (credentials) | ✅ fortress-tools-deployer confirmed |
| Pre-flight (ECR repo exists) | ✅ fred-chat exists |
| Docker build | ✅ Success |
| ECR push (`:c9ba9fce`) | ✅ Success |
| ECR push (`:latest`) | ✅ Success |
| Task def registered | ✅ `fred-dev:172` |
| ECS service updated | ✅ `fred-dev` → `fred-dev:172` |
| Services stable wait | ✅ STABLE |
| Task health | ✅ HEALTHY |
| Running count | ✅ 1/1 |

---

## Verification

```
Image:  742932328420.dkr.ecr.us-east-1.amazonaws.com/fred-chat:c9ba9fce
Digest: sha256:70068aad7dd590794a391fb58ac18bfad8dc00fa844d6ea50fcc9629f18a4c83
Status: RUNNING / HEALTHY
```

**CloudWatch logs:** Clean — no errors. Application started, DB init complete, MCP tools registering normally.

---

## Task Definitions

| Revision | Image | Notes |
|----------|-------|-------|
| fred-dev:171 | `fred-chat:32430067` | Previous (rollback target) |
| fred-dev:172 | `fred-chat:c9ba9fce` | **Current — this deploy** |

---

## Rollback

If needed: `aws ecs update-service --cluster fortress-tools-cluster --service fred-dev --task-definition fred-dev:171`

---

## Notes

- `#3214` was NOT included in this build. When `REVIEW-3214-REPORT.md` is written with PASS, a follow-up deploy from `3b7415a3` (or HEAD at that time) will include it.
