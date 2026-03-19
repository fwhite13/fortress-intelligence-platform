# Research: HubSpot MCP Server for FAM OS CRM Integration

**Date:** 2026-03-19  
**Author:** Bruce (Researcher)  
**Question:** How do we wire FAM OS lifecycle events into HubSpot via MCP without building a full HubSpot API client?  
**Status:** Complete  
**Confidence:** High

---

## Executive Summary

HubSpot has two distinct MCP servers — a **local npm package** (`@hubspot/mcp-server`) and a **remote hosted server** (`https://mcp.hubspot.com`). **They are fundamentally different products.** For FAM OS's integration use case (programmatic read/write of contacts, deals, and lifecycle stages from code), **the npm package with a Private App token is what you want** — not the remote server.

**Critical finding:** The remote MCP server is **read-only**. It cannot update lifecycle stages, create deals, or write any CRM data. Use it for human-facing AI queries only. The npm package supports full read/write.

**For FAM OS:** Use `@hubspot/mcp-server` configured in Claude Code with a Private App access token. Scopes required: `crm.objects.contacts.read/write`, `crm.objects.deals.read/write`, `crm.objects.companies.read/write`.

---

## Section 1: Remote MCP vs Local npm Package — Critical Distinction

### Two Products, Different Use Cases

| | Remote MCP Server (`mcp.hubspot.com`) | Local npm Package (`@hubspot/mcp-server`) |
|--|--|--|
| **Auth** | OAuth 2.1 with PKCE | Private App Access Token |
| **Access** | Read-only | Full read/write |
| **Transport** | Streamable HTTP (hosted) | stdio (local process) |
| **Setup** | Create MCP Auth App in HubSpot Developer Portal | `npx @hubspot/mcp-server` + env var |
| **Write support** | ❌ None | ✅ Create/update contacts, deals, companies |
| **Use case** | Conversational CRM queries for human users | Programmatic agent integration |
| **Best for** | "Summarize my pipeline" queries | FAM OS lifecycle event hooks |
| **Self-service** | January 2026 public beta | May 2025 public beta |

**The remote MCP server** (`mcp.hubspot.com`) went into public beta September 1, 2025. A self-service UI for creating OAuth connectors launched January 13, 2026. Per HubSpot's official docs: *"currently supports read-only access to the following CRM objects"* — contacts, companies, deals, tickets, etc. Write capability has not been announced.

**The npm package** (`@hubspot/mcp-server`) launched May 6, 2025. It runs as a local process, authenticates via a Private App Access Token, and supports full CRUD on contacts, deals, companies, tickets, notes, engagements, and associations. This is the one to use for FAM OS.

---

## Section 2: What the npm MCP Server Supports

### Available Tools (9 at launch, expanding)

| Tool | Operations | Required Scopes |
|------|-----------|-----------------|
| **Contact Management** | Search, read, create, update contacts | `crm.objects.contacts.read`, `crm.objects.contacts.write` |
| **Company Records** | Search, read, create, update companies | `crm.objects.companies.read`, `crm.objects.companies.write` |
| **Deal Pipeline** | Search, read, create, update deals (including stage) | `crm.objects.deals.read`, `crm.objects.deals.write` |
| **Ticket Handling** | Search, read, create, update tickets | `tickets` |
| **Note Creation** | Create and attach notes to CRM objects | `crm.objects.contacts.read` |
| **Engagement Management** | Log calls, emails, meetings | `crm.objects.contacts.read` |
| **CRM Search** | Full-text search across all CRM objects | `crm.objects.*.read` |
| **Association Management** | Link contacts to companies, deals, tickets | `crm.objects.*.read` |
| **Property Access** | Read custom properties and field definitions | `crm.schemas.*.read` |

### FAM OS Use Case Coverage

| FAM OS Need | MCP Supported? | Notes |
|-------------|---------------|-------|
| Read contact by email/ID | ✅ Yes | Contact Management + CRM Search |
| Create new contact | ✅ Yes | Contact Management |
| Update contact `lifecyclestage` | ✅ Yes | Update contact properties |
| Read deal by company name | ✅ Yes | Deal Pipeline + CRM Search |
| Create new deal | ✅ Yes | Deal Pipeline |
| Update deal pipeline stage (`dealstage`) | ✅ Yes | Update deal properties |
| Associate contact ↔ company ↔ deal | ✅ Yes | Association Management |
| Read/set custom properties | ✅ Yes | Property Access + CRUD tools |
| Webhook/event triggers | ❌ No | Direct HubSpot API required |
| Bulk import/export | ❌ No | Direct HubSpot API required |
| Workflow automation triggers | ❌ No | Direct HubSpot API required |
| Custom objects | ❌ Not yet | In roadmap |

---

## Section 3: Auth Model

### Option A: Private App Token (Recommended for FAM OS)

**For the npm local MCP server.** This is the simpler, more appropriate approach for a server-to-server integration.

1. In HubSpot: **Settings → Integrations → Private Apps → Create a private app**
2. Name it (e.g., "FAM OS MCP Integration")
3. Select scopes (see below)
4. Copy the generated access token — treat like a password
5. Set as `PRIVATE_APP_ACCESS_TOKEN` env var

**Required scopes for FAM OS:**
```
crm.objects.contacts.read
crm.objects.contacts.write
crm.objects.companies.read
crm.objects.companies.write
crm.objects.deals.read
crm.objects.deals.write
crm.schemas.contacts.read       (for custom property definitions)
crm.schemas.deals.read
```

**Tokens do not expire** (unlike OAuth access tokens) but should be rotated periodically. HubSpot supports multiple active tokens per app for zero-downtime rotation.

### Option B: OAuth 2.1 with PKCE (Remote MCP server only)

**Only applicable for the remote `mcp.hubspot.com` server** (human-facing, read-only). Requires:
1. Create an MCP Auth App in HubSpot Developer Portal
2. Configure redirect URLs
3. Run OAuth 2.1 flow with PKCE
4. Scopes are auto-determined by MCP server's available tools + user-granted permissions

Since the remote server is read-only, **this is not useful for FAM OS write operations.**

---

## Section 4: Configuration in Claude Code

### Claude Code `~/.claude/settings.json` (or project-level `mcp.json`)

**Global config** (`~/.claude/settings.json`):
```json
{
  "mcpServers": {
    "hubspot": {
      "command": "npx",
      "args": ["-y", "@hubspot/mcp-server"],
      "env": {
        "PRIVATE_APP_ACCESS_TOKEN": "pat-na1-xxxxxxxx-xxxx-xxxx-xxxx-xxxxxxxxxxxx"
      }
    }
  }
}
```

**Project-level** (`.claude/mcp.json` in FAM OS repo root):
```json
{
  "mcpServers": {
    "hubspot": {
      "command": "npx",
      "args": ["-y", "@hubspot/mcp-server"],
      "env": {
        "PRIVATE_APP_ACCESS_TOKEN": "${HUBSPOT_PRIVATE_APP_TOKEN}"
      }
    }
  }
}
```

**Verify installation in Claude Code:**
```
/mcp
```
You should see `hubspot` listed as an active MCP server.

**Alternative — install globally first:**
```bash
npm install -g @hubspot/mcp-server
```
Then replace `"command": "npx", "args": ["-y", "@hubspot/mcp-server"]` with `"command": "hubspot-mcp-server"`.

### Environment Variable Setup (Recommended for FAM OS)

Don't hardcode the token. Use environment variable injection:
```bash
# .env (gitignored)
HUBSPOT_PRIVATE_APP_TOKEN=pat-na1-xxxxxxxx-xxxx-xxxx-xxxx-xxxxxxxxxxxx
```

For production, inject via your secret manager (AWS Secrets Manager, Doppler, etc.).

---

## Section 5: Rate Limits and Known Limitations

### Rate Limits

HubSpot enforces the **same API rate limits for MCP as direct API calls**. The MCP server is a wrapper over HubSpot's REST API, not a separate tier.

| Account Tier | Burst (per 10s per app) | Daily (per account) |
|-------------|------------------------|---------------------|
| Free / Starter | 100 req/app | 250,000 |
| Professional | 190 req/app | 625,000 |
| Enterprise | 190 req/app + API Limit Increase pack available | Higher with add-on |

**CRM Search API** has stricter limits (separate from above). For searching contacts/deals by field values (which is how MCP reads work), expect tighter constraints.

**429 handling:** The MCP server does not automatically retry on 429. Your calling code should implement exponential backoff. At 100 req/10s, FAM OS lifecycle hooks (point-in-time writes per user action) will not hit limits under normal load.

### Known Limitations

1. **No webhook support** — MCP cannot register HubSpot webhooks. Use direct API for inbound HubSpot → FAM OS events.
2. **No bulk operations** — MCP makes individual API calls. For batch imports, use the HubSpot Batch API directly.
3. **No custom objects** (yet) — Custom object CRUD is not in the current tool set. HubSpot roadmap item.
4. **No workflow triggers** — Cannot trigger HubSpot automation workflows via MCP.
5. **No marketing tools** — Emails, forms, campaigns not in current tool set (on roadmap).
6. **CRM Search is not vector search** — Based on HubSpot's CRM Search API (structured filters), not semantic/vector search.
7. **Public beta** — MCP server is still beta. Breaking changes possible; pin your npm version in `package.json`.
8. **LLM hallucination risk** — HubSpot's own docs warn: "LLMs are prone to hallucination, always review when prompted for permission to use an MCP tool that makes changes to your account."

---

## Section 6: HubSpot API Gaps — What MCP Doesn't Cover

For FAM OS, these operations require **direct HubSpot REST API calls**:

| Operation | HubSpot API | Why MCP Can't Do It |
|-----------|------------|---------------------|
| **Inbound webhooks** (HubSpot → FAM OS) | Webhooks API | MCP is outbound only |
| **Bulk contact import** | Imports API | MCP is single-record |
| **Trigger enrollment in workflow** | Workflows API | Not in MCP tool set |
| **Custom objects** | CRM Custom Objects API | Not yet in MCP |
| **HubSpot Forms submission** | Forms API | Not in MCP |
| **Marketing email send** | Marketing Emails API | Not in MCP |
| **Deal pipeline definition** (create new stage) | Pipelines API | MCP reads stages, doesn't create them |
| **List membership** (add to static list) | Lists API | Not in MCP |
| **HubSpot to FAM OS sync** (poll for changes) | CRM Search + `hs_lastmodifieddate` | Possible via MCP but REST more efficient for polling |

**FAM OS integration recommendation:**
- Use MCP for: **write lifecycle events back to HubSpot** (contact updates, deal creation/updates)
- Use direct HubSpot REST API for: **any webhook-driven or bulk operations**

---

## Section 7: HubSpot CRM Concepts — Quick Reference

### Object Model

```
Contact  ←→  Company  ←→  Deal
   ↑              ↑           ↑
  (person)    (org/firm)   (opportunity)
```

- **Contact**: A person. Has `email`, `firstname`, `lastname`, `phone`, `lifecyclestage`, `hs_lead_status`, and any custom properties.
- **Company**: An organization. Has `name`, `domain`, `industry`, `numberofemployees`, `annualrevenue`.
- **Deal**: A sales opportunity. Has `dealname`, `amount`, `dealstage`, `closedate`, `pipeline`. Belongs to a pipeline. Associated to contacts and/or companies.

Associations are many-to-many: a contact can be associated with multiple companies and multiple deals.

### Lifecycle Stages (`lifecyclestage` property on Contact/Company)

Standard values (internal API names):

| Stage | API Value | Meaning |
|-------|-----------|---------|
| Subscriber | `subscriber` | Opted into marketing content |
| Lead | `lead` | Showed interest, not yet qualified |
| Marketing Qualified Lead | `marketingqualifiedlead` | Marketing has determined this is a real prospect |
| Sales Qualified Lead | `salesqualifiedlead` | Sales accepted this lead |
| Opportunity | `opportunity` | Active deal being worked |
| Customer | `customer` | Won/closed deal, paying |
| Evangelist | `evangelist` | Active promoter/referrer |
| Other | `other` | Doesn't fit other stages |

**For FAM OS:**
- On `prospect_created`: set `lifecyclestage = "lead"` or `"marketingqualifiedlead"` depending on your qualification logic
- On `opportunity_bound`: set `lifecyclestage = "opportunity"` or `"customer"` depending on bind definition

**Important:** Lifecycle stages are **unidirectional by default** — HubSpot will not automatically move a contact backward in the lifecycle. Moving forward is fine; moving backward requires a force-update or a workflow. When updating via API, you can always set the value directly.

### Deal Pipeline Stages (`dealstage` property on Deal)

Deal stages live inside a **Pipeline**. Each HubSpot account has at least one pipeline (default: "Sales Pipeline"). Stage IDs are account-specific internal identifiers — you must query your account's pipeline to get the correct stage IDs.

**Typical default stages:**

| Stage Name | Notes |
|------------|-------|
| Appointment Scheduled | Early prospect engagement |
| Qualified to Buy | Basic qualification complete |
| Presentation Scheduled | Demo/pitch scheduled |
| Decision Maker Bought-In | Key stakeholder aligned |
| Contract Sent | Proposal delivered |
| Closed Won | Deal closed successfully |
| Closed Lost | Deal lost |

**For insurance sales (FAM OS-specific):** You'll likely customize the pipeline to match your workflow (Quote, Application, Underwriting, Bound, In-Force, Lapsed). Stage IDs are account-specific — retrieve them via MCP's property access tool or directly from the Pipelines API before building integration code.

### Custom Properties

Any standard HubSpot property can be augmented with custom properties. For FAM OS, you'll likely want:
- `famos_prospect_id` (text) — FAM OS internal ID for the prospect
- `famos_policy_number` (text) — Policy/bind number
- `famos_bind_date` (date) — Date opportunity was bound
- `famos_carrier` (enumeration) — Carrier/insurer

Custom properties are created in HubSpot Settings → Properties, then accessible in MCP via the Property Access tool and settable via Contact/Deal update tools.

### Deals vs Contacts vs Companies in Insurance Context

| HubSpot Object | FAM OS Equivalent |
|---------------|------------------|
| Contact | Individual prospect/policyholder |
| Company | Agency, employer group, or business prospect |
| Deal | Insurance opportunity/application/policy |
| Deal Stage | Workflow stage (Quote → Application → Bound) |
| Lifecycle Stage | Lead qualification (Lead → Opportunity → Customer) |

---

## Section 8: Sample MCP Tool Calls for FAM OS Use Cases

The MCP protocol passes natural language to the tool layer. The following are the Claude prompts that will invoke the correct MCP tools, plus the underlying API operations for reference.

### Use Case 1: Update Contact Lifecycle Stage on Bind

**Scenario:** FAM OS event `opportunity_bound` fires → update contact's lifecycle stage to `customer` in HubSpot.

**Claude prompt to MCP:**
```
Update the HubSpot contact with email "john.doe@example.com" and set their 
lifecyclestage to "customer" and add a custom property famos_bind_date to 
"2026-03-19" and famos_policy_number to "POL-2026-0042".
```

**Underlying API operation:**
```
PATCH /crm/v3/objects/contacts/{contactId}
{
  "properties": {
    "lifecyclestage": "customer",
    "famos_bind_date": "2026-03-19",
    "famos_policy_number": "POL-2026-0042"
  }
}
```

**For programmatic FAM OS hooks (not conversational):** Call the MCP tool directly:
```typescript
// In your FAM OS event handler
await mcpClient.callTool("hubspot", "update_contact", {
  identifier: { email: prospect.email },
  properties: {
    lifecyclestage: "customer",
    famos_bind_date: bindEvent.date,
    famos_policy_number: bindEvent.policyNumber
  }
});
```

### Use Case 2: Read Deal by Company Name

**Scenario:** Look up active deals for a given company to check deal status before creating a duplicate.

**Claude prompt to MCP:**
```
Search HubSpot deals associated with the company "Acme Insurance Group" and 
return their deal names, current stages, amounts, and close dates.
```

**Underlying API operation:**
```
POST /crm/v3/objects/deals/search
{
  "filterGroups": [{
    "filters": [{
      "propertyName": "associations.company",
      "operator": "EQ",
      "value": "{companyId}"
    }]
  }],
  "properties": ["dealname", "dealstage", "amount", "closedate", "pipeline"]
}
```

**Note:** For FAM OS's programmatic use, searching by company name requires either:
1. First looking up the company by name to get its ID, then searching deals by company association
2. Or storing the HubSpot company ID in FAM OS at company creation time (recommended for performance)

### Use Case 3: Create a Deal on Prospect Creation

**Scenario:** FAM OS event `prospect_created` → create a deal record in HubSpot and associate with contact and company.

**Claude prompt to MCP:**
```
Create a new HubSpot deal with:
- Deal name: "John Doe - Auto Policy Q1 2026"
- Pipeline: "Insurance Sales Pipeline" 
- Stage: "Quote"
- Amount: 2400
- Close date: 2026-06-30
- Associate with contact email: john.doe@example.com
- Associate with company: "Doe Household"
```

**Underlying API operations (3 calls):**
```
# 1. Create the deal
POST /crm/v3/objects/deals
{
  "properties": {
    "dealname": "John Doe - Auto Policy Q1 2026",
    "pipeline": "{pipelineId}",
    "dealstage": "{quoteStageId}",
    "amount": "2400",
    "closedate": "1751241600000"
  }
}

# 2. Associate deal → contact
PUT /crm/v4/objects/deals/{dealId}/associations/contacts/{contactId}/3

# 3. Associate deal → company
PUT /crm/v4/objects/deals/{dealId}/associations/companies/{companyId}/5
```

### Use Case 4: Update Deal Stage on Progression

**Scenario:** FAM OS event `application_submitted` → move deal from "Quote" to "Application" stage.

**Claude prompt to MCP:**
```
Update the HubSpot deal "John Doe - Auto Policy Q1 2026" and change its 
dealstage to "Application" and add a note: "Application submitted via FAM OS 
on 2026-03-19. Application ID: APP-2026-0089."
```

**Underlying API operations:**
```
# 1. Update deal stage
PATCH /crm/v3/objects/deals/{dealId}
{
  "properties": {
    "dealstage": "{applicationStageId}"
  }
}

# 2. Create note
POST /crm/v3/objects/notes
{
  "properties": {
    "hs_note_body": "Application submitted via FAM OS on 2026-03-19. Application ID: APP-2026-0089.",
    "hs_timestamp": "1742342400000"
  },
  "associations": [{
    "to": { "id": "{dealId}" },
    "types": [{ "associationCategory": "HUBSPOT_DEFINED", "associationTypeId": 214 }]
  }]
}
```

### Use Case 5: Look Up Contact and Check Current Lifecycle Stage

**Scenario:** Before writing, verify the contact exists and check current lifecycle stage.

**Claude prompt to MCP:**
```
Find the HubSpot contact with email "jane.smith@prospect.com" and return 
their contact ID, current lifecyclestage, associated companies, and 
any open deals.
```

**Programmatic check (idempotency pattern):**
```typescript
// Search contact by email
const contact = await mcpClient.callTool("hubspot", "search_contacts", {
  filters: [{ propertyName: "email", operator: "EQ", value: email }],
  properties: ["email", "lifecyclestage", "famos_prospect_id"]
});

if (!contact.results.length) {
  // Create new contact
} else if (contact.results[0].properties.lifecyclestage === "customer") {
  // Already bound — skip or update famos_policy_number only
} else {
  // Update lifecycle stage
}
```

---

## Section 9: Integration Architecture Recommendation for FAM OS

```
FAM OS Event Bus
     │
     ├─→ [prospect_created]
     │         └─→ HubSpot MCP: create_contact + create_deal
     │
     ├─→ [opportunity_quoted]
     │         └─→ HubSpot MCP: update_deal (stage=Quote)
     │
     ├─→ [application_submitted]
     │         └─→ HubSpot MCP: update_deal (stage=Application) + create_note
     │
     ├─→ [opportunity_bound]
     │         └─→ HubSpot MCP: update_contact (lifecyclestage=customer)
     │                           update_deal (stage=Closed Won, amount=final_premium)
     │                           set custom properties (policy_number, bind_date, carrier)
     │
     └─→ [policy_lapsed]
               └─→ HubSpot MCP: update_deal (stage=Closed Lost) + create_note
                                  update_contact (lifecyclestage=other or custom)
```

**Before writing any event:**
1. Look up contact by `email` or `famos_prospect_id` custom property
2. Check for existing deal (avoid duplicates)
3. Write update/create idempotently

**Store HubSpot IDs in FAM OS:** When you create a contact or deal in HubSpot, store the returned HubSpot `id` (e.g., `hs_contact_id`, `hs_deal_id`) in your FAM OS prospect record. This eliminates lookup-by-email on every write and is much faster.

---

## Section 10: Developer MCP Server — What It Is and Why You Don't Need It

For completeness: the **HubSpot Developer MCP Server** is entirely separate and serves a different purpose.

- **What it is:** A local, CLI-based MCP server for HubSpot app/CMS development tasks
- **Installed via:** `hs mcp setup` (HubSpot CLI v8.0.0+)
- **What it does:** Helps Claude build HubSpot UI Extensions, upload projects, manage builds, search HubSpot developer docs, create test accounts
- **Auth:** HubSpot CLI authentication (for your developer account)
- **For FAM OS:** ❌ Not relevant. This is for building HubSpot plugins/extensions, not for CRM data integration.

**Quick comparison:**

| | Developer MCP Server | Remote MCP Server | npm MCP Package |
|--|--|--|--|
| **Purpose** | Build HubSpot apps/CMS | Conversational CRM queries | CRM data integration |
| **Auth** | HubSpot CLI login | OAuth 2.1 | Private App token |
| **Write CRM data** | ❌ | ❌ | ✅ |
| **For FAM OS** | ❌ | ❌ | ✅ |

---

## Open Questions

1. **Pipeline stage IDs** — You need to retrieve your account's actual `dealstage` internal values before writing deal stage updates. These are account-specific. Query once, store in FAM OS config.

2. **Custom property creation** — The MCP tool can *read* custom properties but you'll need to *create* them in HubSpot Settings UI or via Properties API first (a one-time setup step).

3. **Concurrent event handling** — If multiple FAM OS events fire in rapid succession for the same contact, consider implementing a brief deduplication window (e.g., last-write-wins within 5s) to avoid race conditions.

4. **MCP write maturity** — The npm package's write tools are in public beta. Worth testing in a HubSpot sandbox account before production. HubSpot developer accounts get free sandbox access.

5. **Claude Code as the runtime?** — If FAM OS's event handlers run through Claude Code with the MCP configured, this works cleanly. If FAM OS is a standalone Node.js/Python service, you'd call the MCP server via stdio subprocess or use the HubSpot REST API directly (MCP adds overhead for programmatic server-to-server calls).

---

## Sources

1. HubSpot Developer Changelog: "The HubSpot MCP Server - available in Public Beta" — May 6, 2025 — https://developers.hubspot.com/changelog/mcp-server-beta
2. HubSpot Developer Docs: "Integrate AI tools with the HubSpot MCP server (BETA)" — https://developers.hubspot.com/docs/apps/developer-platform/build-apps/integrate-with-the-remote-hubspot-mcp-server
3. HubSpot Developer Docs: "Creating apps and CMS content with HubSpot's developer MCP server" — https://developers.hubspot.com/docs/developer-tooling/local-development/mcp-server
4. HubSpot Developer Changelog: "Public Beta: Self-Service MCP Auth Apps for the HubSpot Remote MCP Server" — January 13, 2026 — https://developers.hubspot.com/changelog/public-beta-self-service-mcp-auth-apps-for-the-hubspot-remote-mcp-server
5. HubSpot Developer Docs: "API usage guidelines and limits" — https://developers.hubspot.com/docs/developer-tooling/platform/usage-guidelines
6. HubSpot Developer Docs: "Using Object APIs" — https://developers.hubspot.com/docs/guides/crm/using-object-apis
7. npm: `@hubspot/mcp-server` — https://www.npmjs.com/package/@hubspot/mcp-server
8. Digital Applied: "HubSpot MCP Server: AI Agent Integration Guide" — February 11, 2026 — https://www.digitalapplied.com/blog/hubspot-mcp-server-ai-agent-integration-guide
9. HubSpot MCP landing page — https://developers.hubspot.com/mcp

---

*Report compiled 2026-03-19 by Bruce (Researcher agent). For technical implementation questions, route to Jarvis → Tony.*
