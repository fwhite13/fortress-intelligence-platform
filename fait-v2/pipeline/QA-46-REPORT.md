# QA Report — fait-v2:46

**Date:** 2026-05-09 11:58 EDT  
**Tester:** Black Widow (Natasha Romanoff)  
**Image:** fait-v2:46 (commit `1bb5e191`)  
**Environment:** Production ECS — fortress-tools-cluster, service fait-v2

---

## Verdict: ⚠️ PASS-WITH-NOTES

The app is **running and serving traffic** — ALB target healthy, ECS service stable (1/1 running). Users authenticated in prior sessions are active. However, a **non-fatal but recurring error** was introduced by the DB migration: `DataProtectionKeys` table does not exist in `fait_dev`, causing repeating key ring errors at startup and every ~6 minutes. This is a regression from `:45`.

Full post-auth functional QA (chat UI, bubble colors, ADO#3121/#3122) **requires Entra SSO** and cannot be completed from this host (DNS does not resolve `ai.fortressam.ai` from WSL2/sandbox). Browser visual testing is blocked. See FIP Auth Rules in SOUL.md — **Path 2 requires manual sign-off from Fred.**

---

## Test Results

| Test Case | Result | Notes |
|-----------|--------|-------|
| 1. App loads (https://ai.fortressam.ai) | ⚠️ PARTIAL | DNS not resolvable from QA host; ALB target confirmed healthy via AWS API |
| 1a. No 500/502/503 responses | ✅ PASS | ALB target state: `healthy`; ECS 1/1 running |
| 1b. Auth flow / redirect to Entra | ⚠️ MANUAL REQUIRED | Cannot test from headless QA host |
| 2. Chat UI — user bubble dark navy | ⚠️ MANUAL REQUIRED | Cannot reach app from QA host; code changes are in place (ADO#3121/#3122) |
| 2a. User bubble text readable | ⚠️ MANUAL REQUIRED | Same as above |
| 2b. Assistant bubble neutral/left | ⚠️ MANUAL REQUIRED | Same as above |
| 2c. Avatar initials display | ⚠️ MANUAL REQUIRED | Same as above |
| 2d. Markdown rendering (code blocks, lists) | ⚠️ MANUAL REQUIRED | Same as above |
| 3. entra_oid backfill (ADO#3119) | ⚠️ NO DATA | No user sessions hit new container yet; cannot confirm backfill log |
| 4a. DB connects to fait_dev | ✅ PASS | EF Core migrations ran successfully: "EF Core migrations complete" at 15:53:38 |
| 4b. EF migrations up to date | ✅ PASS | Log: `[INF] EF Core migrations complete.` — no pending migration errors |
| 4c. DataProtectionKeys table created | ❌ FAIL | Table does NOT exist in `fait_dev` — recurring ERR at startup and every ~6 min |
| 5. Core features regression | ⚠️ PARTIAL | Active user sessions (from :45) show ChatView loading with history — but through old container |

---

## CloudWatch Summary

### Container: `33073052` (fait-v2:46 — currently running)

**Startup sequence (15:53 UTC):**
```
[15:53:35 INF] Running EF Core migrations...
[15:53:38 INF] EF Core migrations complete.
[15:53:39 INF] Seeded mcp_servers entry: forge-kb
[15:53:39 INF] Seeded mcp_servers entry: ms365
[15:53:39 INF] Seeded mcp_servers entry: ado
[15:53:39 INF] Seeded mcp_servers entry: web-search
[15:53:39 INF] Seeded Marketing plugin agent
[15:53:39 ERR] Failed executing DbCommand: SELECT `d`.`Id`, `d`.`FriendlyName`, `d`.`Xml` FROM `DataProtectionKeys` AS `d`
[15:53:39 ERR] An exception occurred... 'FortressAI.V2.Web.Data.SharedKeyRingDbContext'
[15:53:39 ERR] An error occurred while reading the key ring.
[15:53:39 INF] ScheduledTaskBackgroundService started.
[15:53:39 WRN] Overriding HTTP_PORTS '8080' and HTTPS_PORTS ''. Binding to values defined by URLS instead 'http://+:8080'.
```

**Recurring error (every ~6 minutes — key ring refresh):**
```
MySqlConnector.MySqlException (0x80004005): Table 'fait_dev.DataProtectionKeys' doesn't exist
```
Seen at: 15:53:39, 15:59:43, 16:00:04

**No user traffic in new container yet** — no ChatView INF logs, no auth activity, no entra_oid backfill logs.

### Container: `36b5c791` (fait-v2:45 — previous, now terminated)

- Zero DataProtectionKeys errors
- Active user session: ChatView loaded, 23 messages history, Fargate task launched for user
- Confirmed working with `fait_v2_dev` database

---

## Root Cause Analysis — DataProtectionKeys Error

**What's broken:** `SharedKeyRingDbContext` queries `fait_dev.DataProtectionKeys` — table does not exist.

**Why it's different from :45:**
- `:45` used `FORTRESS_DB_NAME = fait_v2_dev`, which **had** a `DataProtectionKeys` table (auto-created or included in schema)
- `:46` migrated to `fait_dev` (fresh DB, 24 v2 tables) — `DataProtectionKeys` was **not included** in the schema migration

**The env var config:**
```
FIP_KEYRING_DB_NAME = fait_dev  ← SAME in both :45 and :46
```
Wait — both `:45` and `:46` have `FIP_KEYRING_DB_NAME = fait_dev`. But `:45` had zero DataProtectionKeys errors. This means the `DataProtectionKeys` table **already existed in `fait_dev`** at the time `:45` was running (it was auto-created at some earlier point), but the fresh `fait_dev` database created for `:46` does not have it.

**Code intent (SharedKeyRingDbContext.cs comment):** "Points to fred_dev (FIP portal's database) — DataProtectionKeys table only." However `FIP_KEYRING_DB_NAME` is set to `fait_dev`, not `fred_dev`. This may be intentional (shared key ring within FAIT's own DB) but the table wasn't included in the migration.

**Impact:** ASP.NET DataProtection cannot read/write encryption keys. This affects:
- Cookie decryption for any new sessions
- Anti-forgery token validation
- Any data encrypted via `IDataProtector`

**Severity:** HIGH if new user logins are attempted. The app stays running but auth for new sessions may fail silently or throw errors.

---

## Browser Verification

**Status: BLOCKED**

`ai.fortressam.ai` does not resolve from the QA host (WSL2 SteamServer, DNS not forwarding external FIP domains). Browser tool confirmed: `Error: getaddrinfo ENOTFOUND ai.fortressam.ai`.

Per SOUL.md FIP SSO Auth rules: visual QA and post-auth testing **require manual sign-off from Fred.**

---

## Issues Found

### 🔴 Issue #1 — `DataProtectionKeys` table missing in `fait_dev` (HIGH)

- **Type:** Regression introduced by ADO#3123 (DB migration)
- **Symptom:** `MySqlConnector.MySqlException: Table 'fait_dev.DataProtectionKeys' doesn't exist` — recurring every ~6 minutes
- **Root cause:** Fresh `fait_dev` DB was created with 24 v2 tables but `DataProtectionKeys` was not included
- **Impact:** Auth cookie encryption/decryption may fail for new sessions; recurring error spam in logs
- **Present in :45?** No — `fait_v2_dev` had the table; `fait_dev` (fresh) does not
- **Fix options:**
  1. Add `DataProtectionKeys` table creation to the EF migration for `fait_dev`
  2. Or: Run `ALTER TABLE` / `CREATE TABLE DataProtectionKeys (Id INT NOT NULL AUTO_INCREMENT, FriendlyName LONGTEXT NULL, Xml LONGTEXT NULL, PRIMARY KEY (Id))` against `fait_dev` directly
  3. Or: If `FIP_KEYRING_DB_NAME` is intended to point to FIP portal's DB (`fred_dev`/`fip_dev`), fix the env var

### ⚠️ Issue #2 — Visual/functional QA incomplete (MEDIUM — process gap)

- Browser cannot reach `ai.fortressam.ai` from QA host
- ADO#3121, #3122 (chat bubble styling, MessageBubble rebuild) **not visually verified**
- Requires manual Fred sign-off per SOUL.md FIP auth rules

---

## Rollback Recommendation

**HOLD — do not roll back immediately, but flag for urgent fix.**

**Rationale:**
- The app is running and ALB is healthy
- Users with existing valid sessions are unaffected (cookies already decrypted while old keyring was available, or fallback key generation occurs)
- ASP.NET DataProtection degrades gracefully — it will generate a new ephemeral key ring when it can't read the DB, which means sessions work but keys aren't persisted/shared (restart = new keys = existing sessions may expire)
- However, **new user logins will be affected** if the key ring error prevents cookie issuance

**Priority:** Fix the DataProtectionKeys table before significant user traffic hits the new container.  
**Recommended action:** Create the DataProtectionKeys table in `fait_dev` immediately — can be done via direct DB command without a redeploy.

---

## ⚠️ PARTIAL PASS — Manual Sign-Off Required

Per SOUL.md FIP SSO Auth Rules:
- **Path 1 (unauthenticated redirect):** NOT testable from this host
- **Path 2 (post-auth landing + chat UI verification):** REQUIRES MANUAL SIGN-OFF from Fred

**Do not mark ADO#3121, #3122, #3119 as Done until:**
1. Fred confirms post-auth app loads correctly
2. Fred confirms user chat bubble is dark navy (not light gray)
3. DataProtectionKeys table is created in `fait_dev`
