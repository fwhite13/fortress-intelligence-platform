# QA Report: ADO#1553 — NEXUS Tile in FIP App Selector (Image Rebuild)

### Verdict: ✅ PASS

### Environment
- **Target URL:** https://fip.fortressam.ai/
- **Cluster:** fortress-tools-cluster
- **Task Definition:** fip-web:4
- **Test Date:** 2026-04-02
- **Tester:** Natasha Romanoff (Black Widow — QA Analyst)

---

### Test Results

| TC | Test | Result | Details |
|----|------|--------|---------|
| TC1 | fip-web:4 running with new image | ✅ PASS | taskDef=fip-web:4, rollout=COMPLETED, running=1 |
| TC2 | App responding | ✅ PASS | HTTP 403 (auth wall — not 500, acceptable) |
| TC3 | NEXUS tile in running image source | ✅ PASS | grep count=2 (Apps:NexusUrl / nexus.fortressam.ai present in Home.razor) |
| TC4 | Apps__NexusUrl in task def | ✅ PASS | Value: `https://nexus.fortressam.ai` |
| TC5 | NEXUS not in ComingSoonApps | ✅ PASS | FIP__ComingSoonApps=`forms,firm` — nexus absent ✓ |

---

### Summary

All 5 test cases passed. The fip-portal image was rebuilt from commit `7656ffd` and deployed as fip-web:4. The NEXUS tile is present in the Home.razor source, the env var `Apps__NexusUrl` points to `https://nexus.fortressam.ai`, and NEXUS is correctly absent from the `FIP__ComingSoonApps` list. The app is responding (403 = auth wall, expected for unauthenticated requests).

**Total tests: 5 | Passed: 5 | Failed: 0 | Warnings: 0**

---

_Trust nothing. Verify everything._
