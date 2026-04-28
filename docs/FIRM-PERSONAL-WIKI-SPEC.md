# FIRM Personal Wiki Spec

> **Prereq for RISE/Refuge Notetaker deployment.**
> Ships to Fortress first, comes free in the Refuge deploy.

## Overview

Add a personal-level wiki alongside the existing org-level wiki in FIRM. Individual users can create their own context entries (names, jargon, project details, preferred terminology) that get fed into transcription and summarization prompts alongside the org wiki.

**Why prereq for RISE:** Refuge users won't share the Fortress org wiki. The org wiki is tenant-scoped, so it naturally isolates. But personal wikis make the tool immediately more useful for each Refuge user from day one — they can teach the AI their vocabulary without waiting for a Refuge admin to populate the org wiki.

## Current State

### Org Wiki

- **Model:** `FirmOrgContext` — single row per tenant (`EntraTenantId` + `WikiContent` JSON blob)
- **Service:** `OrgContextService` — CRUD for org-level entries
- **UI:** `OrgContext.razor` — admin-only edit, read-only for regular users
- **Consumption:** `BatchTranscriptionService.SubmitTranscriptionJobAsync()` serializes org entries as `ORG_WIKI_JSON` env var → passed to the AWS Batch transcription container
- **Format:** JSON array of `OrgContextEntry { Term, Description }`

### How Org Wiki Reaches AI

```
OrgContextService.GetContextAsync(tenantId)
  → serialize to JSON
    → env var ORG_WIKI_JSON on Batch job
      → transcription container reads it
        → injected into Bedrock prompt as context
```

## Design

### Data Model

New table `firm_user_wiki` (created by `DatabaseInitializationService` raw SQL, consistent with FIRM's no-EF-migrations pattern):

```sql
CREATE TABLE IF NOT EXISTS firm_user_wiki (
    id BIGINT AUTO_INCREMENT PRIMARY KEY,
    entra_oid VARCHAR(128) NOT NULL,
    entra_tenant_id VARCHAR(36) NOT NULL,
    term VARCHAR(256) NOT NULL,
    description TEXT NOT NULL,
    created_at DATETIME NOT NULL DEFAULT CURRENT_TIMESTAMP,
    updated_at DATETIME NOT NULL DEFAULT CURRENT_TIMESTAMP ON UPDATE CURRENT_TIMESTAMP,
    INDEX idx_user_wiki_user (entra_oid, entra_tenant_id)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4;
```

**Key design decisions:**
- **Per-entry rows** (not a single JSON blob like org wiki). Cleaner for CRUD, pagination, and future search.
- **Keyed by `entra_oid` + `entra_tenant_id`**. Works across tenants (Fortress and Refuge) without collision.
- **No foreign key to `firm_users`**. Keeps it simple — the Entra OID from the auth cookie is the identity.

### EF Model

```csharp
public class FirmUserWikiEntry
{
    public long Id { get; set; }
    [MaxLength(128)]
    public string EntraOid { get; set; } = "";
    [MaxLength(36)]
    public string EntraTenantId { get; set; } = "";
    [MaxLength(256)]
    public string Term { get; set; } = "";
    public string Description { get; set; } = "";
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
}
```

Add to `FirmDbContext`:

```csharp
public DbSet<FirmUserWikiEntry> UserWikiEntries => Set<FirmUserWikiEntry>();

// In OnModelCreating:
modelBuilder.Entity<FirmUserWikiEntry>(entity =>
{
    entity.ToTable("firm_user_wiki");
    entity.HasKey(e => e.Id);
    entity.Property(e => e.Id).HasColumnName("id").ValueGeneratedOnAdd();
    entity.Property(e => e.EntraOid).HasColumnName("entra_oid");
    entity.Property(e => e.EntraTenantId).HasColumnName("entra_tenant_id");
    entity.Property(e => e.Term).HasColumnName("term");
    entity.Property(e => e.Description).HasColumnName("description");
    entity.Property(e => e.CreatedAt).HasColumnName("created_at");
    entity.Property(e => e.UpdatedAt).HasColumnName("updated_at");
});
```

### Service

New `IUserWikiService` / `UserWikiService`:

```csharp
public interface IUserWikiService
{
    Task<List<FirmUserWikiEntry>> GetEntriesAsync(string entraOid, string tenantId);
    Task<FirmUserWikiEntry> AddEntryAsync(string entraOid, string tenantId, string term, string description);
    Task<FirmUserWikiEntry?> UpdateEntryAsync(long id, string entraOid, string term, string description);
    Task<bool> DeleteEntryAsync(long id, string entraOid);
    Task<int> GetEntryCountAsync(string entraOid, string tenantId);
}
```

### API Controller

New `UserWikiController` (REST endpoints for the Blazor UI):

```
GET    /api/user-wiki                    → list current user's entries
POST   /api/user-wiki                    → add entry { term, description }
PUT    /api/user-wiki/{id}               → update entry { term, description }
DELETE /api/user-wiki/{id}               → delete entry
```

All endpoints extract `entra_oid` from the auth cookie claims. Users can only see/edit their own entries.

### UI

#### Option A: Tab on Existing Org Context Page

Add a "Personal Wiki" tab alongside "Organization Wiki" on the `OrgContext.razor` page. Both admins and regular users see and edit their personal entries. Rename the page title to "Context Wiki" or "Knowledge Base."

#### Option B: Separate Page (Recommended)

New `/my-wiki` page accessible from the sidebar nav. Clean separation:
- `/org-context` — org wiki (admin-editable, everyone reads)
- `/my-wiki` — personal wiki (each user owns theirs)

**Recommended: Option B.** Cleaner mental model for users. The org wiki is "company knowledge," personal wiki is "my knowledge."

#### Page: `MyWiki.razor`

- Header: "My Wiki" with subtitle "Personal context entries that improve AI accuracy for your meetings."
- Add Entry button → dialog with Term + Description fields
- Table: Term | Description | Actions (Edit, Delete)
- Entry count indicator (e.g., "12 entries")
- Import/export as CSV (nice-to-have, not required for v1)

#### Sidebar Nav

Add "My Wiki" icon + link in `MainLayout.razor` nav, between Meetings and Org Context (or wherever makes sense).

### Prompt Injection — How Personal Wiki Reaches AI

Currently, only org wiki is passed to the transcription job. Personal wiki needs to be merged in.

**Change in `BatchTranscriptionService.SubmitTranscriptionJobAsync()`:**

```csharp
// Existing: org wiki
var orgEntries = await _orgContextService.GetContextAsync(tenantId);

// New: personal wiki for the meeting creator
var userWikiEntries = await _userWikiService.GetEntriesAsync(creatorEntraOid, tenantId);

// Merge: org entries + personal entries (org first, personal after)
var allContext = new List<object>();
if (orgEntries.Count > 0)
    allContext.Add(new { source = "organization", entries = orgEntries });
if (userWikiEntries.Count > 0)
    allContext.Add(new { source = "personal", entries = userWikiEntries.Select(e => new { e.Term, e.Description }) });

var wikiJson = allContext.Count > 0
    ? JsonSerializer.Serialize(allContext)
    : null;

// Pass as WIKI_JSON (replaces ORG_WIKI_JSON)
if (wikiJson != null)
    envVars.Add(new KeyValuePair { Name = "WIKI_JSON", Value = wikiJson });
```

**Transcription container update:** Read `WIKI_JSON` (fall back to `ORG_WIKI_JSON` for backward compat). Parse the `source` field to label context appropriately in the prompt:

```
Organization context:
- FAM: Fortress Affinity Management, a business unit within Fortress

Personal context (from meeting creator):
- TPS: The quarterly TPS report that goes to Tom
- Project Atlas: Internal codename for the new carrier integration
```

### Meeting Creator Identity

To look up the personal wiki for the right user, we need the meeting creator's `entra_oid` at transcription submission time. This is already available — `FirmMeeting` is created by an authenticated user, and the `BatchTranscriptionService` is called in that user's context.

**If the meeting was created by user A but transcription is triggered later (e.g., by Batch callback):** The meeting record should store `creator_entra_oid`. Check if this column exists; if not, add it.

### Shared Meeting Context (Future)

For meetings with multiple FIRM users, we could merge personal wikis from all participants. **Not in scope for v1** — use the meeting creator's wiki only. Flag for future iteration.

---

## Implementation Tasks

### Task 1: Database + Model
- Add `firm_user_wiki` table creation to `DatabaseInitializationService`
- Add `FirmUserWikiEntry` model
- Add DbSet to `FirmDbContext` + `OnModelCreating` mapping
- Store `creator_entra_oid` on `FirmMeeting` if not already present

### Task 2: Service Layer
- Implement `UserWikiService` with CRUD operations
- Register in DI (`Program.cs`)

### Task 3: API Controller
- `UserWikiController` with GET/POST/PUT/DELETE
- Auth: extract `entra_oid` from claims, scope all queries

### Task 4: UI — My Wiki Page
- New `MyWiki.razor` page with add/edit/delete dialog
- Sidebar nav entry
- Responsive layout (mobile-friendly — some Refuge users may access from phones)

### Task 5: Prompt Integration
- Update `BatchTranscriptionService` to merge personal wiki
- Update transcription container to read `WIKI_JSON`
- Backward compat: still read `ORG_WIKI_JSON` if `WIKI_JSON` absent
- Ensure `creator_entra_oid` is available at transcription time

### Task 6: Testing
- Verify personal wiki CRUD (add, edit, delete entries)
- Verify entries appear in transcription prompt
- Verify tenant isolation (Fortress user can't see Refuge user's entries)
- Verify user isolation (user A can't see user B's entries within same tenant)

---

## Entry Limits

- **Max entries per user:** 100 (soft limit, enforced in service layer)
- **Max term length:** 256 chars
- **Max description length:** 2000 chars
- **Max total wiki size per user for prompt injection:** ~4000 tokens (~16KB). If a user's wiki exceeds this, truncate oldest entries with a note in the prompt: "Personal wiki truncated to fit context window."

---

## UX Copy

**My Wiki page header:**
> Your personal knowledge base. Add terms, names, acronyms, and context that are specific to your work. These entries help the AI produce more accurate transcriptions and summaries for your meetings.

**Empty state:**
> No entries yet. Add terms and definitions that the AI should know about when processing your meetings — like team member names, project codenames, or industry jargon.

**Add Entry dialog:**
> **Term:** The word, name, or acronym (e.g., "TPS Report")
> **Description:** What it means or how it should be used (e.g., "Quarterly financial summary report sent to Tom McDonnell")

---

## Dependencies

- None external. This is a pure FIRM code change.
- Ships to Fortress FIRM first (dev → prod).
- Automatically available in Refuge Notetaker deployment since it's the same codebase.
