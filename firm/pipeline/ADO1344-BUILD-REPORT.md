## Build Report: ADO#1344

### Investigation Findings

- **fait_user_id for firm_users id=9bdd8169:** `b25e0de9-614b-4e71-ba4a-f1a261eeeaa9` (confirmed via DB query)
- **Token storage key:** `fait_user_id` (stored in `fait_dev.user_microsoft_tokens` by FAIT's `/auth/ms-callback`)
- **Token lookup key:** `fait_user_id` (CalendarService already calls `GetValidAccessTokenAsync(faitGuid)` correctly)
- **Root cause scenario: Scenario C — wrong database**

**Detailed root cause:**

FIRM has two MySQL databases:
- `firm_dev` — FIRM's own tables (firm_users, firm_meetings, etc.)
- `fait_dev` — FAIT's shared database (user_microsoft_tokens, DataProtectionKeys)

`FirmDbContext` connects to `firm_dev`. It had a `UserMicrosoftTokens` DbSet mapped to `user_microsoft_tokens`, but **`firm_dev.user_microsoft_tokens` is completely empty** (0 rows). All real tokens are stored in `fait_dev.user_microsoft_tokens` by FAIT's OAuth consent flow (`/auth/ms-callback` → `MicrosoftTokenService.ExchangeCodeAsync`).

The token lookup key (`fait_user_id`) was already correct — CalendarService properly resolves `firmUser.FaitUserId` and passes it to `GetValidAccessTokenAsync`. The only problem was querying the wrong database.

Additional finding: `firm_users.fait_user_id` for Fred (`9bdd8169`) is `b25e0de9`, but Fred's actual FAIT user (`fwhite@fortressinsurance.com`) is `1f89fc34`. The `b25e0de9` FAIT user ID belongs to `LMitchell@fortressinsurance.com`. This means `ResolveFaitUserIdAsync` is returning the wrong user (it returns "most recently created Entra user" as a single-tenant workaround — ADO#TODO to fix). However, this is a separate issue — Fred needs to re-link his FAIT user ID. The DB fix in this WI unblocks the token lookup when the mapping IS correct.

### CC Invocation

```bash
cd /home/fredw/projects/fip && cat /tmp/ado1344-brief.md | claude --model sonnet --print --dangerously-skip-permissions
```

### Changes Made

| File | Change |
|---|---|
| `Data/FaitSharedDbContext.cs` | **Created** — new DbContext connecting to `fait_dev` (via `FIP_KEYRING_DB_NAME`). Has single `UserMicrosoftTokens` DbSet with PascalCase column mappings matching the actual FAIT table schema. GuidFormat=None for CHAR(36) UserId. |
| `Program.cs` | **Modified** — adds `faitSharedCsb` (clone of keyRingCsb + GuidFormat=None) and registers `FaitSharedDbContext` factory after the keyring context. |
| `Services/FirmMicrosoftTokenService.cs` | **Modified** — constructor injects `IDbContextFactory<FaitSharedDbContext>` instead of `IDbContextFactory<FirmDbContext>`. All token reads and refreshes now use `FaitSharedDbContext`. |
| `Data/FirmDbContext.cs` | **Modified** — removed `UserMicrosoftTokens` DbSet and its `OnModelCreating` block. Prevents accidental queries against the empty `firm_dev.user_microsoft_tokens`. |

### Commit

```
b205f2e fix(ADO#1344): point FirmMicrosoftTokenService to fait_dev for token lookup
```

### Self-Review Checklist

- [x] token storage key = FaitUserId — CalendarService already uses `firmUser.FaitUserId` (unchanged)
- [x] token lookup key = FaitUserId — `FirmMicrosoftTokenService.GetValidAccessTokenAsync(userId)` where userId is `faitGuid`
- [x] Both paths use the SAME key — storage (FAIT) and lookup (FIRM) both use the FAIT user GUID
- [x] GuidFormat=None still present in FIRM connection string (firm_dev, line ~30 of Program.cs)
- [x] GuidFormat=None added to FaitSharedDbContext connection string (fait_dev UserId is CHAR(36))
- [x] No client-credentials token used in calendar flow — CalendarService uses delegated token from FirmMicrosoftTokenService
- [x] Build: 0 errors, 0 warnings

### ⚠️ Follow-on Issue (separate WI recommended)

`ResolveFaitUserIdAsync` in `MeetingService` uses a single-tenant workaround — returns "most recently created Entra user" regardless of OID. For Fred (`fwhite@fortressinsurance.com`), `firm_users.fait_user_id` was populated with `LMitchell@fortressinsurance.com`'s FAIT user ID. This means after this fix deploys, the calendar feature will still fail for Fred until:
1. A new WI is raised to fix `ResolveFaitUserIdAsync` to use `EntraOid` for user matching (requires FAIT to store EntraOid)
2. **OR** Fred's `firm_users.fait_user_id` is manually corrected to `1f89fc34-9b8c-42fc-b674-aa4562a4f57d`

The DB fix in this WI is still necessary and correct — it's a prerequisite for the above.

### How to Test

After deploy:
1. Ensure Fred's `firm_users.fait_user_id` = `1f89fc34-9b8c-42fc-b674-aa4562a4f57d` (may need manual fix or re-login after ResolveFaitUserId fix)
2. Navigate to FIRM Meetings page
3. Verify calendar events load without "No Microsoft token" warning in CloudWatch
4. Token refresh should also work (FIRM writes updated token back to fait_dev)
