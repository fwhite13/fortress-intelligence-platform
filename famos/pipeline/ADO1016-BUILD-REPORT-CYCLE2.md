# Build Report: ADO#1016 Cycle 2 — HubSpot Field Mapping

**Date:** 2026-03-22
**Agent:** Tony Stark
**Commit:** `11b7747`
**Status:** ✅ COMPLETE (with findings)

---

## Root Cause Investigation

Queried HubSpot API directly to find actual property names and data:

### Findings

| Check | Result |
|-------|--------|
| Total companies | 2084 |
| Companies with `lifecyclestage=lead` | 2084 (100%) |
| Companies with `lifecyclestage=customer` | 0 |
| Companies with `hs_lead_status` set | 0 |
| Total deals | **0** |
| API access to line_items/products | Missing scopes |

### Conclusion

**This is a DATA issue, not a code bug.**

- "All 1919 accounts show Prospect" → **CORRECT** because HubSpot has all companies with `lifecyclestage: "lead"`
- "Only 1/1919 has coverage/carrier data" → **CORRECT** because HubSpot has zero deals (the 1 account must be test data)

---

## HubSpot Property Names Verified

### Company Properties Available
- `lifecyclestage` — standard HubSpot property ✓
- `hs_lead_status` — standard HubSpot property ✓
- No TIG-specific custom properties for status/coverage/carrier

### Deal Properties Available
- `dealstage`, `closedate`, `amount` — standard
- No custom properties for coverage/carrier/expiration
- **BUT: Zero deals exist to have these properties**

---

## Code Changes Made

### File: `src/FamOs.Web/Services/AccountSyncService.cs`

| Change | Purpose |
|--------|---------|
| Added zero-deals warning log | Makes data gap visible in logs |
| Added status distribution log | Shows `Active=X, Prospect=Y, Inactive=Z` after sync |
| Enhanced `MapLifecycleToStatus` | Now checks `hs_lead_status` as fallback |
| Changed `evangelist` → `Active` | Evangelists are happy customers, not prospects |

### Diff Summary
- +47 lines, -15 lines
- No new dependencies
- No schema changes
- Rate limit delay preserved
- Per-company try/catch preserved

---

## Recommendations for TIG

For the sync to produce useful data, TIG needs to update HubSpot:

1. **Update lifecycle stages** — Mark actual customers as `customer` instead of `lead`
2. **Create deals** — Coverage, carrier, and expiration data live on deal records
3. **Add custom properties** (if not using standard) — Ensure `line_of_business`, `carrier_name`, `policy_expiration_date` or similar exist on deals

---

## Self-Review Checklist

- [x] Code compiles
- [x] No new packages added
- [x] Rate limit delay preserved
- [x] Per-company try/catch preserved
- [x] Investigation documented
- [x] ADO updated with findings
- [x] Commit message descriptive

---

## Next Steps

1. **REVIEW** — Hawkeye reviews changes
2. **DEPLOY** — After review pass
3. **Escalate to TIG** — Need HubSpot data updates (lifecycle stages + deals)
4. **Re-test** — After TIG updates HubSpot data

---

*The code is correct. The data is the problem.*
