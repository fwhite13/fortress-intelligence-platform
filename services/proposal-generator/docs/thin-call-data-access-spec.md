# Proposal Generator — Thin-Call Data Access Layer Spec
**Version:** 1.0  
**Date:** 2026-05-04  
**Status:** Draft — pending Caleb Terry Azure DB access confirmation  

---

## 1. Overview

The Proposal Generator currently accepts a "fat payload" JSON request containing all data needed to render a proposal. The thin-call architecture replaces this with a minimal API request (`opportunityId` + `quoteIds[]`) and a data access layer that fetches the required data directly from the FAM OS Aurora MySQL database.

This spec defines:
- The new thin-call API request shape
- The data access queries (what to fetch from which tables)
- The mapping from FAM OS data model → proposal template data
- The error handling contract
- The stub interface to build against now (before Azure DB access is confirmed)

---

## 2. New API Request Shape

### Endpoint
```
POST /proposals/generate
```

### Thin-Call Body
```json
{
  "templateId": "nbais-wc",
  "opportunityId": "opp_01ABC123",
  "quoteIds": ["quote_01XYZ789"],
  "outputFormat": "docx"
}
```

### Backward Compatibility
The fat payload shape (current) must remain supported for local dev and testing. Detection: if `insured` key is present in the body → fat payload path. If `opportunityId` key is present → thin-call path.

---

## 3. FAM OS Data Sources

### 3.1 Tables Required

| Table | Schema | Purpose |
|-------|--------|---------|
| `opportunities` | `famos_dev` | Named insured, address, entity type, effective/expiration dates, AM name |
| `quotes` | `famos_dev` | Quote premium, carrier, line of business, quote date |
| `quote_attributes` | `famos_dev` | WC-specific attributes (policy number, EMR, etc.) — EAV or JSON column |
| `quote_schedule_items` | `famos_dev` | Employee classification schedule rows |
| `schedule_item_attributes` | `famos_dev` | Per-class attributes (class code, payroll, rate, est premium, state) |
| `opportunity_contacts` | `famos_dev` | Primary contact (name, title, email, phone) |
| `opportunity_exclusions` | `famos_dev` | Excluded persons (WC-specific) |
| `team_members` | `famos_dev` | Account manager / producer team for proposal footer |

> **Note:** Exact table/column names TBD pending Caleb Terry confirming Azure schema. Use the Aurora MySQL baseline from `memory/ops/db-access-reference.md` as the current reference; adjust when Azure is live.

### 3.2 Opportunity Query
```sql
SELECT
  o.Id                    AS opportunityId,
  o.InsuredName           AS insuredName,
  o.DBA                   AS insuredDba,
  o.EntityType            AS insuredEntityType,
  o.FEIN                  AS insuredFein,
  o.AddressStreet1        AS street1,
  o.AddressCity           AS city,
  o.AddressState          AS state,
  o.AddressZip            AS zip,
  o.EffectiveDate         AS effectiveDate,
  o.ExpirationDate        AS expirationDate,
  o.AccountManagerName    AS amName,
  o.AccountManagerEmail   AS amEmail
FROM famos_dev.opportunities o
WHERE o.Id = :opportunityId
```

### 3.3 Primary Contact Query
```sql
SELECT
  c.Name   AS contactName,
  c.Title  AS contactTitle,
  c.Email  AS contactEmail,
  c.Phone  AS contactPhone
FROM famos_dev.opportunity_contacts c
WHERE c.OpportunityId = :opportunityId
  AND c.IsPrimary = 1
LIMIT 1
```

### 3.4 Quote Query
```sql
SELECT
  q.Id              AS quoteId,
  q.LineOfBusiness  AS lineOfBusiness,
  q.CarrierName     AS carrierName,
  q.Premium         AS premium,
  q.QuoteDate       AS quoteDate,
  q.PolicyNumber    AS policyNumber,
  q.Attributes      AS attributesJson
FROM famos_dev.quotes q
WHERE q.Id IN (:quoteIds)
  AND q.OpportunityId = :opportunityId
```

### 3.5 Classification Schedule Query (WC-specific)
```sql
SELECT
  si.Id             AS itemId,
  si.ItemType       AS itemType,
  si.Description    AS description,
  si.Attributes     AS attributesJson
FROM famos_dev.quote_schedule_items si
WHERE si.QuoteId = :quoteId
  AND si.ItemType = 'employee_class'
ORDER BY si.SortOrder, si.Id
```

From each row's `attributesJson`, extract:
- `state` → state abbreviation
- `class_code` → NCCI class code
- `class_description` → description (fall back to `description` column)
- `payroll` → estimated annual payroll (decimal)
- `rate_per_hundred` → rate per $100 of payroll (decimal)
- `estimated_premium` → class-level estimated premium (decimal)

### 3.6 Excluded Persons Query (WC-specific)
```sql
SELECT
  ep.PersonName AS name
FROM famos_dev.opportunity_exclusions ep
WHERE ep.OpportunityId = :opportunityId
  AND ep.ExclusionType = 'wc_owner_exclusion'
ORDER BY ep.SortOrder, ep.Id
```

> **Alternative:** If excluded persons live in the quote `Attributes` JSON column rather than a separate table, extract from `JSON_EXTRACT(q.Attributes, '$.excludedPersons')`.

---

## 4. Field Mapping: FAM OS → assembleNbaisWcTemplateData

| Template Field | Source | Notes |
|----------------|--------|-------|
| `memberName` | `opportunities.InsuredName` | |
| `memberLegalName` | `opportunities.InsuredName` | Same unless legal name field exists separately |
| `memberAddress` | `street1`, `city`, `state`, `zip` | Formatted: "123 Main St, City, ST 00000" |
| `policyPeriod` | `effectiveDate` – `expirationDate` | Formatted MM/DD/YYYY – MM/DD/YYYY |
| `quoteDate` | `quotes.QuoteDate` | Falls back to today |
| `amName` | `opportunities.AccountManagerName` | |
| `amEmail` | `opportunities.AccountManagerEmail` | |
| `contactName` | `opportunity_contacts.Name` | |
| `contactTitle` | `opportunity_contacts.Title` | |
| `contactEmail` | `opportunity_contacts.Email` | |
| `contactPhone` | `opportunity_contacts.Phone` | |
| `basePremium` | `quotes.Premium` | Formatted currency |
| `surplusContribution` | `basePremium * 0.08` | Computed, rounded to cents |
| `employersLiabilityFee` | `$120.00` | Program constant |
| `totalEstimatedPremium` | `basePremium + surplusContribution + 120` | Computed |
| `downPayment` | `totalEstimatedPremium * 0.25` | Computed, rounded to cents |
| `classSchedule[]` | `quote_schedule_items` (type=employee_class) | See §3.5 |
| `excludedPersons[]` | `opportunity_exclusions` (type=wc_owner_exclusion) | See §3.6 |
| `hasExcludedPersons` | `excludedPersons.length > 0` | Computed boolean |

---

## 5. Data Access Layer Interface (Stub)

Build the following module now with mock implementations. Tony swaps mock → real DB calls when Azure access is confirmed.

### File: `src/services/dataAccess/FamOsDataClient.js`

```js
/**
 * FamOsDataClient — FAM OS data access for thin-call proposal generation.
 *
 * In stub mode: returns mock data matching test-payloads/nbais-wc-test.json shape.
 * In live mode: executes SQL against FAM OS Aurora MySQL (via Azure connection).
 *
 * Toggle via env var: FAM_OS_DATA_MODE=stub|live
 */
export class FamOsDataClient {
  constructor(dbPool, mode = process.env.FAM_OS_DATA_MODE ?? 'stub') {
    this._db = dbPool
    this._mode = mode
  }

  /** Fetch opportunity + primary contact */
  async getOpportunity(opportunityId) { ... }

  /** Fetch one or more quotes by ID, scoped to opportunity */
  async getQuotes(opportunityId, quoteIds) { ... }

  /** Fetch WC employee classification schedule for a quote */
  async getClassSchedule(quoteId) { ... }

  /** Fetch WC excluded persons for an opportunity */
  async getExcludedPersons(opportunityId) { ... }

  /** Convenience: fetch all data needed for NBAIS WC proposal in one call */
  async getNbaisWcProposalData(opportunityId, quoteIds) {
    const [opportunity, quotes, classSchedule, excludedPersons] = await Promise.all([
      this.getOpportunity(opportunityId),
      this.getQuotes(opportunityId, quoteIds),
      this.getClassSchedule(quoteIds[0]),
      this.getExcludedPersons(opportunityId),
    ])
    return { opportunity, quotes, classSchedule, excludedPersons }
  }
}
```

### File: `src/services/dataAccess/stubs/nbaisWcStub.js`

Returns a hardcoded object matching the shape of `getNbaisWcProposalData()` return value, using the Carson Valley Excavation test data. This is what runs in `FAM_OS_DATA_MODE=stub`.

### File: `src/services/proposalAssembler.js` (new)

Thin-call request handler: detects `opportunityId` in request body, calls `FamOsDataClient.getNbaisWcProposalData()`, maps result to fat payload shape, passes to existing `assembleNbaisWcTemplateData()`. Keeps fat payload path intact.

---

## 6. Error Handling Contract

| Condition | HTTP Status | Error Code |
|-----------|-------------|------------|
| `opportunityId` not found | 404 | `OPPORTUNITY_NOT_FOUND` |
| One or more `quoteIds` not found | 404 | `QUOTE_NOT_FOUND` |
| Quote does not belong to opportunity | 400 | `QUOTE_OPPORTUNITY_MISMATCH` |
| DB connection failure | 503 | `DATA_SOURCE_UNAVAILABLE` |
| Required field missing after fetch (e.g. no insured name) | 422 | `INCOMPLETE_OPPORTUNITY_DATA` |
| Stub mode but unknown opportunityId | 404 | `STUB_OPPORTUNITY_NOT_FOUND` |

---

## 7. Connection Config

```js
// src/config.js additions
FAM_OS_DB_HOST:     process.env.FAM_OS_DB_HOST        // Azure MySQL hostname
FAM_OS_DB_PORT:     process.env.FAM_OS_DB_PORT ?? 3306
FAM_OS_DB_NAME:     process.env.FAM_OS_DB_NAME ?? 'famos_dev'
FAM_OS_DB_USER:     process.env.FAM_OS_DB_USER
FAM_OS_DB_PASSWORD: process.env.FAM_OS_DB_PASSWORD    // from Azure Key Vault / Secrets Manager
FAM_OS_DATA_MODE:   process.env.FAM_OS_DATA_MODE ?? 'stub'
```

Caleb Terry will provide `FAM_OS_DB_HOST`, `FAM_OS_DB_USER`, `FAM_OS_DB_PASSWORD` once Azure infra is ready.

---

## 8. Out of Scope (This Spec)

- Multi-LOB thin-call (IAAPA, GL, etc.) — NBAIS WC first
- `GET /proposals/:id` endpoint (separate WI)
- Proposal status webhooks / callbacks
- Caching of FAM OS data (not needed at current scale)

---

## 9. Open Questions

1. **Exact table names in Azure MySQL** — confirm with Caleb Terry. Current spec uses Aurora MySQL naming conventions; may differ.
2. **Excluded persons storage** — separate table vs. JSON in `quotes.Attributes`? Caleb to confirm.
3. **Auth between Proposal Generator and FAM OS DB** — service account credentials, connection pooling config, SSL requirement.
4. **`memberLegalName`** — is there a distinct legal name field on the opportunity, or always same as `InsuredName`?
