## Review Report: ADO#1344

### Verdict: PASS

---

### CC Invocation

```bash
cd /home/fredw/projects/fip && cat /tmp/ado1344-review-brief.md | claude --model sonnet --print --dangerously-skip-permissions
```

---

### Spec Compliance

**Spec:** `graph-token-acquisition.md`

| Criterion | Status | Evidence |
|---|---|---|
| Use delegated token (not client_credentials) | ✅ | `grant_type = "refresh_token"` — line 87 of FirmMicrosoftTokenService.cs |
| Token lookup key = FAIT user GUID | ✅ | CalendarService passes `firmUser.FaitUserId` as `faitGuid` to `GetValidAccessTokenAsync` |
| `GetValidAccessTokenAsync` returns auto-refreshed token | ✅ | ExpiresAt + 5min buffer check + refresh_token grant |
| No MSAL / ITokenAcquisition | ✅ | Not present |
| No client credentials in calendar flow | ✅ | Confirmed |
| FIRM config keys: `Firm:GraphClientId`, `Firm:GraphTenantId`, `Firm:GraphClientSecret` | ✅ | Lines 42–44 of FirmMicrosoftTokenService.cs |

**Spec compliance verdict: ✅ COMPLIANT**

---

### Issues Found

| # | Severity | File | Issue | Fix Required |
|---|----------|------|-------|--------------|
| 1 | Important | `Program.cs:135–136` | Comment says "fait_dev" but DB is actually `FIP_KEYRING_DB_NAME` (currently `fred_dev`). Comment is misleading until ADO#1244 ships. | Update comment to: `// FaitSharedDbContext — reads/updates user_microsoft_tokens from the FAIT DB (controlled by FIP_KEYRING_DB_NAME)` |
| 2 | Important | `Program.cs:137–146` | `FaitSharedDbContext` database is coupled to `FIP_KEYRING_DB_NAME` — same env var as the DataProtection keyring. No independent `FIP_FAIT_DB_NAME` env var. If these DBs ever diverge post-ADO#1244, the coupling silently redirects token lookups. | Cosmetic concern only for now — after ADO#1244 ships both contexts will correctly point to `fait_dev`. Track as follow-on: add dedicated `FIP_FAIT_DB_NAME` when prod/dev split requires it. |
| 3 | Important | `FirmMicrosoftTokenService.cs:97–99, 118–119` | On ANY refresh failure (including transient network errors), FIRM **deletes** the token from FAIT's canonical DB. A flaky call to `login.microsoftonline.com` could wipe a still-valid token that FAIT is actively using, forcing the user to re-consent. Pre-existing behavior, not a regression. | Track as follow-on WI. Fix: distinguish between non-retryable OAuth errors (invalid_grant, revoked) vs. transient errors (5xx, timeout). Only delete on non-retryable errors. |
| 4 | Nitpick | `FaitSharedDbContext.cs:24–30` | `HasColumnName(...)` calls are redundant — EF Core defaults already produce these PascalCase names (they match the property names). Harmless defensive code. | No action required. |

**No Critical issues found.**

---

### Consistency Audit

**Cross-DB access pattern:**

| Check | Result |
|---|---|
| `FaitSharedDbContext` column mappings vs. FAIT `AppDbContext` / migration snapshot | ✅ Match — all 7 columns PascalCase, consistent with FAIT's schema |
| `FirmDbContext` — `UserMicrosoftTokens` fully removed | ✅ No DbSet, no OnModelCreating block, no reference |
| `FirmMicrosoftTokenService` — all token ops use `FaitSharedDbContext` | ✅ Read (FindAsync), write (SaveChangesAsync after refresh), delete (Remove) all use `FaitSharedDbContext` |
| `CalendarService` — calls `GetValidAccessTokenAsync(faitGuid)` | ✅ Unchanged; uses `firmUser.FaitUserId` as key |
| `Program.cs` — `FaitSharedDbContext` registered as `IDbContextFactory<>` | ✅ Correct registration type |
| `GuidFormat = MySqlGuidFormat.None` present in `faitSharedCsb` | ✅ Line 145 |

**DB piggybacking analysis:**  
`FaitSharedDbContext` targets the DB identified by `FIP_KEYRING_DB_NAME` (currently `fred_dev`). This is intentional — `fred_dev` currently contains both `DataProtectionKeys` AND `user_microsoft_tokens`. After ADO#1244 ships, `FIP_KEYRING_DB_NAME` will be updated to `fait_dev` for the dev service, and both contexts will correctly follow. The coupling is by design, not an oversight.

---

### Positive Observations

- **Clean separation:** `FirmDbContext` is now clean — no cross-DB entity leaking into it.
- **GuidFormat=None correctly added:** The CHAR(36) UserId case is handled, consistent with prior ADO#1329 fix in the firm connection string.
- **No scope creep:** Only 4 files modified, CalendarService untouched.
- **Correct config key usage:** `Firm:GraphClientId/TenantId/ClientSecret` correctly follow the spec's per-module config key mapping.
- **Stub auth preserved:** `_useStubAuth` path intact for local dev.

---

### Follow-on Items (Separate WIs Recommended)

1. **ADO#1344 follow-on:** Add `FIP_FAIT_DB_NAME` env var to decouple `FaitSharedDbContext` from keyring once ADO#1244 prod/dev split is complete.
2. **Token delete on transient error:** `FirmMicrosoftTokenService` should only delete tokens on non-retryable OAuth errors, not transient network failures.
3. **ResolveFaitUserIdAsync:** Fred's `firm_users.fait_user_id` needs manual correction OR `ResolveFaitUserIdAsync` needs OID-based matching (noted in Build Report; separate WI required to unblock Fred's calendar).
