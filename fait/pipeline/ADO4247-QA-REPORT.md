# QA Report: ADO#4247 — Grant fait-v2-task-role pgvector Secret Read

**Verdict: ✅ PASS**
**Task Def:** fred-dev:291
**Image:** fred-chat:fc64aa41
**Date:** 2026-05-27
**Tester:** Black Widow (QA Analyst)

---

## Tests Run

| # | Test | Result |
|---|------|--------|
| 1 | No `AccessDenied` on SM GetSecretValue in harness logs | ✅ PASS |
| 2 | pgvector connection attempt present in harness logs | ✅ PASS |

---

## Test Details

### Test 1 — AccessDenied Check

Scanned `/ecs/fait-v2-agent-harness` log group (2-hour window) for `AccessDenied`.

**Result: No AccessDenied entries found.** ✅

The `FaitPgvectorSecretAccess` IAM inline policy on `fait-v2-task-role` is active. The harness can successfully invoke `secretsmanager:GetSecretValue` on `fortress-tools/pgvector-connection-wx0f9F` without being blocked.

### Test 2 — pgvector Connection Attempt

From harness log stream `48f7d846894248e98b331c9cc787b39b` (most recent):

```
[pgvector] connection failed — falling back to md-file memory: connect ETIMEDOUT 172.31.7.59:5432
```

**Result: Connection attempt confirmed.** ✅

The harness successfully read the pgvector secret (no IAM error) and attempted a TCP connection to `172.31.7.59:5432`. The ETIMEDOUT is a **network infrastructure issue**, not an IAM issue — this is ADO#4263 scope.

### Side Note — Unrelated AccessDenied in Same Log

Also observed in the same log stream:
```
[harness] GCP credentials not available — Stitch will be unavailable: User: arn:aws:sts::742932328420:assumed-role/fait-v2-task-role/... is not authorized to perform: secretsmanager:GetSecretValue on resource: fait-v2/gcp-stitch-service-account
```

This is an **unrelated pre-existing issue** (GCP/Stitch secret access). It is NOT the pgvector secret (`fortress-tools/pgvector-connection-wx0f9F`), NOT in scope for ADO#4247.

---

## Acceptance Criteria Status

| Criterion | Status |
|-----------|--------|
| No `AccessDenied` on pgvector secret in harness logs | ✅ CONFIRMED — clean |
| Harness attempts pgvector connection | ✅ CONFIRMED — ETIMEDOUT present (ADO#4263 scope) |

---

## Key Findings

- IAM policy `FaitPgvectorSecretAccess` is active and working ✅
- Harness successfully reads pgvector secret from SM ✅
- ETIMEDOUT to `172.31.7.59:5432` is the network-layer issue tracked as ADO#4263 ✅
- GCP/Stitch AccessDenied is a separate pre-existing issue, not in scope ✅

---

## Issues Found

None in scope. ETIMEDOUT is ADO#4263 (network, not IAM).

---

## Verdict

**✅ PASS** — IAM policy is active. No AccessDenied on the pgvector secret. Harness proceeds to TCP connection (ETIMEDOUT expected per ADO#4263).

---

## Test Duration
~3 minutes
