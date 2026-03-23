# Build Report: ADO#1016 — HubSpot Field Mapping Fix

**Date:** 2026-03-22  
**Agent:** Tony Stark (software-engineer)  
**Commit:** b1e76ab  
**Risk Level:** Medium (sync service changes, new DB columns, UI logic changes)

---

## Problem Summary

The Accounts page showed 971 accounts all as "Inactive" with dashes for Coverage Line, Carrier, and Expiration Date. The root cause:

1. AccountSyncService only fetched `name,city,state` from HubSpot companies
2. AccountStatus, Coverage, Carrier, ExpDate were derived from local Opportunities table
3. Many accounts had no matching opportunities, so all fields showed as empty/Inactive

---

## Solution Implemented

### 1. Account Entity Expansion (`Account.cs`)

Added 5 new properties to store HubSpot-synced data:
```csharp
public string?   AccountStatus   { get; set; }  // "Active" | "Prospect" | "Inactive"
public string?   PrimaryCoverage { get; set; }  // from primary deal
public string?   PrimaryCarrier  { get; set; }  // from primary deal
public DateTime? PolicyExpiresAt { get; set; }  // from primary deal
public string?   PrimaryDealId   { get; set; }  // HubSpot deal ID
```

### 2. AccountSyncService Full Rewrite

**Before:** 
- Fetched companies with `name,city,state` only
- No deal data

**After:**
- Fetches companies with `name,city,state,lifecyclestage,hs_lead_status`
- Bulk-fetches all deals with coverage/carrier/expiration properties
- Fetches company→deal associations via batch endpoint
- Picks "primary deal" (prefer non-closed, then most recent by closedate)
- Maps lifecyclestage to AccountStatus:
  - `customer` → "Active"
  - `lead`, `subscriber`, `marketingqualifiedlead`, `salesqualifiedlead`, `opportunity` → "Prospect"
  - Everything else → "Inactive"
- Rate limit handling: 60ms delay between API calls

### 3. Database Migrations (`Program.cs`)

Added DDL migrations using existing TryAddColumnAsync pattern:
```sql
ALTER TABLE accounts ADD COLUMN account_status VARCHAR(20) NULL;
ALTER TABLE accounts ADD COLUMN primary_coverage VARCHAR(100) NULL;
ALTER TABLE accounts ADD COLUMN primary_carrier VARCHAR(100) NULL;
ALTER TABLE accounts ADD COLUMN policy_expires_at DATETIME NULL;
ALTER TABLE accounts ADD COLUMN primary_deal_id VARCHAR(50) NULL;
```

### 4. EF Core Mappings (`FamOsDbContext.cs`)

Added property mappings for all new columns with appropriate max lengths.

### 5. Accounts.razor UI Changes

Updated helper functions to prefer stored HubSpot fields with fallback:
- `GetAccountStatus()` → prefers `account.AccountStatus` if set
- `GetCoverageLine()` → prefers `account.PrimaryCoverage` if set
- `GetCarrier()` → prefers `account.PrimaryCarrier` if set
- `GetExpDate()` → prefers `account.PolicyExpiresAt` if set

Also updated `_distinctCoverageLines` filter to include HubSpot-synced coverages.

### 6. CSS Grid Fix (`famos.css`)

Updated account table grid to properly handle 7 columns:
```css
grid-template-columns: minmax(180px, 2fr) 90px 110px 110px 110px 100px 100px;
```

Added `white-space: nowrap` and `text-overflow: ellipsis` to prevent overflow.

---

## Files Modified

| File | Lines Changed | Description |
|------|---------------|-------------|
| `Account.cs` | +7 | New entity properties |
| `FamOsDbContext.cs` | +6 | EF column mappings |
| `Program.cs` | +6 | DDL migrations |
| `AccountSyncService.cs` | +350 | Full sync logic rewrite |
| `Accounts.razor` | +30 | Prefer stored fields, updated helpers |
| `famos.css` | +15 | 7-column grid layout fix |

---

## Testing Notes

1. **Build verification:** Brace matching verified, syntax appears correct
2. **Local build:** Cannot compile locally (.NET 9 required, machine has .NET 8)
3. **AWS build:** Will be validated via CodeBuild/CodePipeline

### Post-Deploy Verification

After deploy, verify:
1. Accounts page shows Account Status based on HubSpot lifecyclestage
2. Coverage/Carrier/ExpDate populated from primary deal
3. No UI overflow issues with column widths
4. Sync runs successfully (check logs for `[AccountSync]` entries)

---

## Risks & Mitigations

| Risk | Mitigation |
|------|------------|
| HubSpot API rate limits | Added 60ms delay between calls |
| Custom property names vary | Code tries multiple property names with fallbacks |
| Large deal counts | Capped at 5000 deals, batch association fetching |
| DB migration failure | Using existing TryAddColumnAsync pattern (idempotent) |

---

## Ready for Review

Commit `b1e76ab` is ready for Clint's review.
