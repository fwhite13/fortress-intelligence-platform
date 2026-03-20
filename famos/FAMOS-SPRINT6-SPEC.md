# FAM OS Sprint 6 Spec — Contacts, Documents, UW Completeness, Owner Picker, Search, Activity Log

**Author:** Reed Richards (Software Architect)  
**Date:** 2026-03-19  
**Sprint Goal:** Round out the OpportunityWorkspace with contacts, documents, a completeness gate, owner assignment, wired topbar search, and a full activity log panel  
**Prerequisite:** Sprint 5 deployed and verified  
**Design System:** ALL components must comply with `DESIGN-SYSTEM.md`. No inline `Variant=`, `Color=`, `Size=` on MudButton. No inline `Style="width:..."` on inputs. All icons via `FamosIcons.*`. Clint enforces.  
**Spec references:** `FAMOS-ARCHITECTURE-SPEC.md`, `FAMOS-SPRINT5-SPEC.md`, `DESIGN-SYSTEM.md`

---

## Sprint 6 Overview

Six deliverables. All additive — no breaking changes to the lifecycle engine or existing panels.

| Part | Feature | New Files | Modified Files |
|------|---------|-----------|----------------|
| A | Contacts Panel | 2 | 3 |
| B | Documents Panel (S3) | 2 | 3 |
| C | UW Completeness Checklist | 1 | 3 |
| D | Owner Assignment UI | 2 | 3 |
| E | Opportunity Search (topbar) | 1 | 2 |
| F | Activity Log Panel | 1 | 2 |

**Total: 9 new files, 16 modified files.** Single sequential CC session — Parts A–D all touch `FamOsDbContext.cs` and `Opportunity.cs`; Parts E and F touch `MainLayout.razor` and `OpportunityService.cs` respectively.

---

## Parallelization Map

**All sequential — single CC session.**

Execution order:
1. Part D — `AffinityConfig` extension + `Users` array (needed by Owner Picker and other panels)
2. Part A — `Contact` entity + DB migration + `ContactsPanel.razor` + `LifecycleCommandService` additions
3. Part B — `OpportunityDocument` entity + DB migration + `DocumentService.cs` + `DocumentsPanel.razor`
4. Part C — `UwCompletenessService.cs` + completeness meter on `OpportunityWorkspace.razor`
5. Part E — `OpportunitySearchService.cs` + `MainLayout.razor` topbar search wiring
6. Part F — `ActivityPanel.razor` + `LifecycleCommandService.AddNoteAsync` + `OpportunityWorkspace.razor` panel wiring

---

## DB Changes

**Aurora MySQL compat: use try/catch on error 1060. Do NOT use `IF NOT EXISTS` syntax.**

Add a `TryAddColumnAsync` helper if not already present from Sprint 5 (see Sprint 5 spec for the pattern). Add new table creation and column additions to the DB initialization block in `Program.cs` after existing `CreateTablesAsync()`:

```csharp
// Sprint 6 — new tables

// contacts
await _db.Database.ExecuteSqlRawAsync("""
    CREATE TABLE IF NOT EXISTS contacts (
        id            CHAR(36) NOT NULL PRIMARY KEY,
        opportunity_id CHAR(36) NOT NULL,
        first_name    VARCHAR(100) NOT NULL DEFAULT '',
        last_name     VARCHAR(100) NOT NULL DEFAULT '',
        title         VARCHAR(100) NULL,
        email         VARCHAR(200) NULL,
        phone         VARCHAR(50) NULL,
        contact_type  INT NOT NULL DEFAULT 0,
        notes         LONGTEXT NULL,
        created_at    DATETIME NOT NULL DEFAULT CURRENT_TIMESTAMP,
        updated_at    DATETIME NOT NULL DEFAULT CURRENT_TIMESTAMP ON UPDATE CURRENT_TIMESTAMP,
        INDEX idx_contacts_opp (opportunity_id),
        FOREIGN KEY (opportunity_id) REFERENCES opportunities(id) ON DELETE CASCADE
    ) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4;
""");

// opportunity_documents
await _db.Database.ExecuteSqlRawAsync("""
    CREATE TABLE IF NOT EXISTS opportunity_documents (
        id              CHAR(36) NOT NULL PRIMARY KEY,
        opportunity_id  CHAR(36) NOT NULL,
        file_name       VARCHAR(255) NOT NULL DEFAULT '',
        file_type       VARCHAR(100) NULL,
        s3_key          VARCHAR(500) NOT NULL DEFAULT '',
        document_category INT NOT NULL DEFAULT 6,
        uploaded_at     DATETIME NOT NULL DEFAULT CURRENT_TIMESTAMP,
        uploaded_by     VARCHAR(200) NULL,
        INDEX idx_docs_opp (opportunity_id),
        FOREIGN KEY (opportunity_id) REFERENCES opportunities(id) ON DELETE CASCADE
    ) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4;
""");

// Sprint 6 — new column on opportunities
await TryAddColumnAsync("ALTER TABLE opportunities ADD COLUMN primary_contact_id CHAR(36) NULL");
```

---

## Part A — Contacts Panel

### A1. `Data/Entities/Contact.cs` (new)

```csharp
namespace FamOs.Web.Data.Entities;

public class Contact
{
    public Guid    Id             { get; set; } = Guid.NewGuid();
    public Guid    OpportunityId  { get; set; }
    public string  FirstName      { get; set; } = "";
    public string  LastName       { get; set; } = "";
    public string? Title          { get; set; }
    public string? Email          { get; set; }
    public string? Phone          { get; set; }
    public ContactType ContactType { get; set; } = ContactType.Primary;
    public string? Notes          { get; set; }
    public DateTime CreatedAt     { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAt     { get; set; } = DateTime.UtcNow;

    public Opportunity Opportunity { get; set; } = default!;

    public string FullName => $"{FirstName} {LastName}".Trim();
}

public enum ContactType
{
    Primary          = 0,
    Billing          = 1,
    DecisionMaker    = 2,
    TechnicalContact = 3
}
```

### A2. `Domain/Enums.cs` — Add `ContactType`

The enum is defined on the entity file above. If Tony prefers domain enums in `Enums.cs`, move it there. Either location is acceptable as long as it is in the `FamOs.Web.Domain` or `FamOs.Web.Data.Entities` namespace and consistently referenced.

### A3. `Data/FamOsDbContext.cs` — Add Contact entity config + navigation

Add `DbSet`:
```csharp
public DbSet<Contact> Contacts => Set<Contact>();
```

Add entity configuration in `OnModelCreating`:
```csharp
m.Entity<Contact>(e => {
    e.ToTable("contacts");
    e.HasKey(x => x.Id);
    e.Property(x => x.Id).HasColumnType("char(36)");
    e.Property(x => x.OpportunityId).HasColumnType("char(36)");
    e.Property(x => x.ContactType).HasConversion<int>();
    e.Property(x => x.Notes).HasColumnType("longtext");
    e.HasOne(x => x.Opportunity)
        .WithMany(o => o.Contacts)
        .HasForeignKey(x => x.OpportunityId);
});
```

Add `PrimaryContactId` to Opportunity config:
```csharp
e.Property(x => x.PrimaryContactId).HasColumnType("char(36)");
```

### A4. `Data/Entities/Opportunity.cs` — Add Contact navigation + PrimaryContactId

```csharp
// After existing navigation properties:
public List<Contact>  Contacts          { get; set; } = new();
public Guid?          PrimaryContactId  { get; set; }
```

### A5. `Domain/LifecycleCommandService.cs` — Add Contact Commands

```csharp
/// <summary>Add a contact to an opportunity. Only one Primary allowed per opportunity.</summary>
public async Task<Guid> AddContactAsync(
    Guid opportunityId, string firstName, string lastName,
    string? title, string? email, string? phone,
    ContactType contactType, string? notes, string actorUserId)
{
    await using var tx = await _db.Database.BeginTransactionAsync();
    var opp = await LoadOpportunityAsync(opportunityId);
    Validate(!opp.IsClosed, "Cannot add contacts to a closed opportunity");

    // Enforce single Primary constraint
    if (contactType == ContactType.Primary
        && opp.Contacts.Any(c => c.ContactType == ContactType.Primary))
    {
        throw new LifecycleValidationException(
            "This opportunity already has a primary contact. " +
            "Update the existing primary contact or use a different contact type.");
    }

    var contact = new Contact
    {
        OpportunityId = opportunityId,
        FirstName     = firstName.Trim(),
        LastName      = lastName.Trim(),
        Title         = title?.Trim(),
        Email         = email?.Trim(),
        Phone         = phone?.Trim(),
        ContactType   = contactType,
        Notes         = notes?.Trim(),
    };
    _db.Contacts.Add(contact);

    // Auto-set PrimaryContactId when adding a Primary contact
    if (contactType == ContactType.Primary)
        opp.PrimaryContactId = contact.Id;

    opp.UpdatedAt = DateTime.UtcNow;
    await WriteActivityAsync(opp.Id, "contact_added",
        $"Contact added: {firstName} {lastName} ({contactType})", actorUserId);

    await _db.SaveChangesAsync();
    await tx.CommitAsync();
    return contact.Id;
}

/// <summary>Remove a contact from an opportunity.</summary>
public async Task RemoveContactAsync(Guid contactId, string actorUserId)
{
    await using var tx = await _db.Database.BeginTransactionAsync();
    var contact = await _db.Contacts
        .Include(c => c.Opportunity)
        .FirstOrDefaultAsync(c => c.Id == contactId)
        ?? throw new NotFoundException($"Contact {contactId} not found");

    var opp = contact.Opportunity;
    _db.Contacts.Remove(contact);

    // Clear PrimaryContactId if this was the primary
    if (opp.PrimaryContactId == contactId)
        opp.PrimaryContactId = null;

    opp.UpdatedAt = DateTime.UtcNow;
    await WriteActivityAsync(opp.Id, "contact_removed",
        $"Contact removed: {contact.FullName}", actorUserId);

    await _db.SaveChangesAsync();
    await tx.CommitAsync();
}
```

### A6. `Services/OpportunityService.cs` — Include Contacts in `GetByIdAsync`

In `GetByIdAsync`, add `.Include(o => o.Contacts)` to the query chain after the existing includes.

### A7. `Components/Pages/Opportunity/Panels/ContactsPanel.razor` (new)

```razor
@namespace FamOs.Web.Components.Panels
@inject LifecycleCommandService Lifecycle
@inject UserSessionService UserSession
@inject ISnackbar Snackbar
@inject IDialogService DialogService
@using FamOs.Web.Data.Entities
@using FamOs.Web.Theme

<div class="famos-panel">

    <div class="famos-panel-header">
        <span class="famos-panel-title">Contacts</span>
        @if (!Opportunity.IsClosed)
        {
            <MudButton Class="famos-btn-outline-sm"
                       StartIcon="@FamosIcons.Add"
                       OnClick="OpenAddDialog">
                Add Contact
            </MudButton>
        }
    </div>

    @if (!Opportunity.Contacts.Any())
    {
        <div class="famos-empty-state">
            <MudIcon Icon="@FamosIcons.Person" Class="famos-empty-icon" />
            <div>No contacts yet. Add a primary contact to complete intake.</div>
        </div>
    }
    else
    {
        <div class="famos-contact-list">
            @foreach (var contact in Opportunity.Contacts
                .OrderBy(c => c.ContactType == ContactType.Primary ? 0 : 1)
                .ThenBy(c => c.LastName))
            {
                <div class="famos-contact-row">
                    <div class="famos-contact-avatar">
                        @(contact.FirstName.Length > 0 ? contact.FirstName[0].ToString().ToUpper() : "?")
                    </div>
                    <div class="famos-contact-info">
                        <div class="famos-contact-name">
                            @contact.FullName
                            @if (contact.ContactType == ContactType.Primary)
                            {
                                <span class="famos-contact-badge-primary">Primary</span>
                            }
                        </div>
                        @if (!string.IsNullOrEmpty(contact.Title))
                        {
                            <div class="famos-contact-meta">@contact.Title</div>
                        }
                        <div class="famos-contact-meta">
                            @if (!string.IsNullOrEmpty(contact.Email))
                            {
                                <span>@contact.Email</span>
                            }
                            @if (!string.IsNullOrEmpty(contact.Phone))
                            {
                                <span> · @contact.Phone</span>
                            }
                        </div>
                    </div>
                    @if (!Opportunity.IsClosed)
                    {
                        <MudButton Class="famos-btn-icon-sm"
                                   StartIcon="@FamosIcons.Delete"
                                   OnClick="() => RemoveContact(contact.Id)">
                        </MudButton>
                    }
                </div>
            }
        </div>
    }

</div>

@code {
    [Parameter, EditorRequired] public Opportunity Opportunity { get; set; } = default!;
    [Parameter] public EventCallback OnUpdated { get; set; }

    private async Task OpenAddDialog()
    {
        var dialog = await DialogService.ShowAsync<AddContactDialog>(
            "Add Contact",
            new DialogParameters { ["OpportunityId"] = Opportunity.Id });
        var result = await dialog.Result;
        if (result is { Canceled: false })
            await OnUpdated.InvokeAsync();
    }

    private async Task RemoveContact(Guid contactId)
    {
        var userId = await UserSession.GetUserIdAsync();
        await Lifecycle.RemoveContactAsync(contactId, userId);
        Opportunity.Contacts.RemoveAll(c => c.Id == contactId);
        Snackbar.Add("Contact removed.", Severity.Info);
        StateHasChanged();
    }
}
```

### A8. `Components/Dialogs/AddContactDialog.razor` (new)

```razor
@inject LifecycleCommandService Lifecycle
@inject UserSessionService UserSession
@inject ISnackbar Snackbar
@using FamOs.Web.Data.Entities
@using FamOs.Web.Theme

<MudDialog>
    <TitleContent>Add Contact</TitleContent>
    <DialogContent>
        <MudGrid Spacing="2">
            <MudItem xs="12" sm="6">
                <MudTextField Class="famos-input" @bind-Value="_firstName"
                    Label="First Name *" />
            </MudItem>
            <MudItem xs="12" sm="6">
                <MudTextField Class="famos-input" @bind-Value="_lastName"
                    Label="Last Name *" />
            </MudItem>
            <MudItem xs="12" sm="6">
                <MudTextField Class="famos-input" @bind-Value="_title"
                    Label="Title / Role" />
            </MudItem>
            <MudItem xs="12" sm="6">
                <MudSelect Class="famos-select" @bind-Value="_contactType"
                    Label="Contact Type">
                    <MudSelectItem Value="ContactType.Primary">Primary</MudSelectItem>
                    <MudSelectItem Value="ContactType.Billing">Billing</MudSelectItem>
                    <MudSelectItem Value="ContactType.DecisionMaker">Decision Maker</MudSelectItem>
                    <MudSelectItem Value="ContactType.TechnicalContact">Technical Contact</MudSelectItem>
                </MudSelect>
            </MudItem>
            <MudItem xs="12" sm="6">
                <MudTextField Class="famos-input" @bind-Value="_email"
                    Label="Email" InputType="InputType.Email" />
            </MudItem>
            <MudItem xs="12" sm="6">
                <MudTextField Class="famos-input" @bind-Value="_phone"
                    Label="Phone" />
            </MudItem>
            <MudItem xs="12">
                <MudTextField Class="famos-input" @bind-Value="_notes"
                    Label="Notes" Lines="2" />
            </MudItem>
        </MudGrid>
        @if (_error != null)
        {
            <MudAlert Severity="Severity.Error" Class="mt-3">@_error</MudAlert>
        }
    </DialogContent>
    <DialogActions>
        <MudButton Class="famos-btn-outline" OnClick="Cancel">Cancel</MudButton>
        <MudButton Class="famos-btn-primary" OnClick="Submit"
                   Disabled="@(string.IsNullOrWhiteSpace(_firstName) || string.IsNullOrWhiteSpace(_lastName))">
            Add Contact
        </MudButton>
    </DialogActions>
</MudDialog>

@code {
    [CascadingParameter] IMudDialogInstance MudDialog { get; set; } = default!;
    [Parameter] public Guid OpportunityId { get; set; }

    private string _firstName  = "";
    private string _lastName   = "";
    private string _title      = "";
    private string _email      = "";
    private string _phone      = "";
    private string _notes      = "";
    private ContactType _contactType = ContactType.Primary;
    private string? _error;

    private void Cancel() => MudDialog.Cancel();

    private async Task Submit()
    {
        if (string.IsNullOrWhiteSpace(_firstName) || string.IsNullOrWhiteSpace(_lastName)) return;
        var userId = await (new UserSessionService(default!, default!)).GetUserIdAsync(); // injected via @inject — see note below
        try
        {
            // NOTE: Tony — inject UserSessionService via @inject UserSessionService UserSession at top
            // and use: var userId = await UserSession.GetUserIdAsync();
            await Lifecycle.AddContactAsync(
                OpportunityId, _firstName, _lastName, _title,
                _email, _phone, _contactType, _notes, userId);
            MudDialog.Close(DialogResult.Ok(true));
        }
        catch (LifecycleValidationException ex)
        {
            _error = ex.Message;
        }
    }
}
```

**Important:** The `Submit` method above shows a placeholder for `UserSession`. Tony must add `@inject UserSessionService UserSession` to the `@inject` block at the top and replace the placeholder with `var userId = await UserSession.GetUserIdAsync();`. The pattern is identical to every other dialog in this project.

### A9. `famos.css` — Add Contact Panel Classes

```css
/* Contacts Panel */
.famos-panel { margin-bottom: 16px; }
.famos-panel-header {
    display: flex;
    justify-content: space-between;
    align-items: center;
    margin-bottom: 12px;
}
.famos-panel-title {
    font-size: 12.5px;
    font-weight: 700;
    color: var(--navy);
    letter-spacing: 0.3px;
    text-transform: uppercase;
}
.famos-empty-state {
    padding: 24px 16px;
    text-align: center;
    color: var(--muted);
    font-size: 13px;
    border: 1px dashed var(--border);
    border-radius: 8px;
}
.famos-empty-icon {
    font-size: 32px;
    color: var(--border);
    display: block;
    margin: 0 auto 8px;
}
.famos-contact-list { display: flex; flex-direction: column; gap: 6px; }
.famos-contact-row {
    display: flex;
    align-items: center;
    gap: 10px;
    padding: 10px 12px;
    border: 1px solid var(--border);
    border-radius: 8px;
    background: var(--white);
}
.famos-contact-avatar {
    width: 32px; height: 32px; border-radius: 50%;
    background: var(--sky); color: #fff;
    font-size: 13px; font-weight: 700;
    display: flex; align-items: center; justify-content: center;
    flex-shrink: 0;
}
.famos-contact-info { flex: 1; min-width: 0; }
.famos-contact-name { font-size: 13px; font-weight: 600; color: var(--navy); }
.famos-contact-meta { font-size: 11.5px; color: var(--muted); margin-top: 1px; }
.famos-contact-badge-primary {
    display: inline-block;
    margin-left: 6px;
    padding: 1px 7px;
    border-radius: 20px;
    background: rgba(0, 144, 208, 0.12);
    color: var(--sky);
    font-size: 10px;
    font-weight: 700;
    letter-spacing: 0.4px;
    text-transform: uppercase;
    vertical-align: middle;
}
.famos-btn-icon-sm {
    min-width: 28px; width: 28px; height: 28px;
    padding: 0;
    border-radius: 6px;
    color: var(--muted);
    background: transparent;
    border: none;
}
.famos-btn-icon-sm:hover { color: var(--red); background: rgba(220,38,38,0.07); }
```

---

## Part B — Documents Panel (S3)

### B1. NuGet Package Addition

**Add to `FamOs.Web.csproj`:**
```xml
<PackageReference Include="AWSSDK.S3" Version="3.7.*" />
```

This is the only new NuGet package in Sprint 6.

### B2. `Data/Entities/OpportunityDocument.cs` (new)

```csharp
namespace FamOs.Web.Data.Entities;

public class OpportunityDocument
{
    public Guid     Id                 { get; set; } = Guid.NewGuid();
    public Guid     OpportunityId      { get; set; }
    public string   FileName           { get; set; } = "";
    public string?  FileType           { get; set; }   // MIME type
    public string   S3Key              { get; set; } = "";
    public DocumentCategory DocumentCategory { get; set; } = DocumentCategory.Other;
    public DateTime UploadedAt         { get; set; } = DateTime.UtcNow;
    public string?  UploadedBy         { get; set; }

    public Opportunity Opportunity     { get; set; } = default!;
}

public enum DocumentCategory
{
    Application   = 0,
    Quote         = 1,
    Proposal      = 2,
    BindRequest   = 3,
    Policy        = 4,
    Correspondence = 5,
    Other         = 6
}
```

### B3. `Data/FamOsDbContext.cs` — Add Document entity config

```csharp
public DbSet<OpportunityDocument> Documents => Set<OpportunityDocument>();
```

In `OnModelCreating`:
```csharp
m.Entity<OpportunityDocument>(e => {
    e.ToTable("opportunity_documents");
    e.HasKey(x => x.Id);
    e.Property(x => x.Id).HasColumnType("char(36)");
    e.Property(x => x.OpportunityId).HasColumnType("char(36)");
    e.Property(x => x.DocumentCategory).HasConversion<int>();
    e.HasOne(x => x.Opportunity)
        .WithMany(o => o.Documents)
        .HasForeignKey(x => x.OpportunityId);
});
```

Add navigation to `Opportunity.cs`:
```csharp
public List<OpportunityDocument> Documents { get; set; } = new();
```

### B4. `Services/DocumentService.cs` (new)

S3 key pattern: `famos/documents/{opportunityId}/{filename}`  
Bucket: `fip-cowork-workspaces`  
Presigned URL expiry: 15 minutes for upload, 60 minutes for download.

```csharp
using Amazon.S3;
using Amazon.S3.Model;
using FamOs.Web.Data;
using FamOs.Web.Data.Entities;

namespace FamOs.Web.Services;

public interface IDocumentService
{
    /// <summary>Get a presigned PUT URL for direct browser→S3 upload.</summary>
    Task<PresignedUploadResult> GetUploadUrlAsync(Guid opportunityId, string fileName, string contentType);

    /// <summary>Record the document in the DB after a successful S3 upload.</summary>
    Task<Guid> RecordUploadAsync(Guid opportunityId, string fileName, string contentType,
        string s3Key, DocumentCategory category, string uploadedBy);

    /// <summary>Get a presigned GET URL for download.</summary>
    Task<string> GetDownloadUrlAsync(string s3Key);

    /// <summary>Delete a document record + S3 object.</summary>
    Task DeleteAsync(Guid documentId, string actorUserId);
}

public class DocumentService : IDocumentService
{
    private readonly IAmazonS3 _s3;
    private readonly IDbContextFactory<FamOsDbContext> _dbFactory;
    private readonly ILogger<DocumentService> _logger;

    private const string BucketName    = "fip-cowork-workspaces";
    private const string KeyPrefix     = "famos/documents";

    public DocumentService(IAmazonS3 s3,
        IDbContextFactory<FamOsDbContext> dbFactory,
        ILogger<DocumentService> logger)
    {
        _s3       = s3;
        _dbFactory = dbFactory;
        _logger   = logger;
    }

    public Task<PresignedUploadResult> GetUploadUrlAsync(
        Guid opportunityId, string fileName, string contentType)
    {
        var safeFileName = Path.GetFileName(fileName); // strip any path traversal
        var s3Key        = $"{KeyPrefix}/{opportunityId}/{safeFileName}";

        var request = new GetPreSignedUrlRequest
        {
            BucketName  = BucketName,
            Key         = s3Key,
            Verb        = HttpVerb.PUT,
            ContentType = contentType,
            Expires     = DateTime.UtcNow.AddMinutes(15)
        };

        var url = _s3.GetPreSignedURL(request);
        return Task.FromResult(new PresignedUploadResult(url, s3Key));
    }

    public async Task<Guid> RecordUploadAsync(
        Guid opportunityId, string fileName, string contentType,
        string s3Key, DocumentCategory category, string uploadedBy)
    {
        await using var db = await _dbFactory.CreateDbContextAsync();
        var doc = new OpportunityDocument
        {
            OpportunityId    = opportunityId,
            FileName         = fileName,
            FileType         = contentType,
            S3Key            = s3Key,
            DocumentCategory = category,
            UploadedBy       = uploadedBy,
        };
        db.Documents.Add(doc);
        await db.SaveChangesAsync();
        _logger.LogInformation("[Docs] Recorded {File} for opportunity {Opp}", fileName, opportunityId);
        return doc.Id;
    }

    public Task<string> GetDownloadUrlAsync(string s3Key)
    {
        var request = new GetPreSignedUrlRequest
        {
            BucketName = BucketName,
            Key        = s3Key,
            Verb       = HttpVerb.GET,
            Expires    = DateTime.UtcNow.AddMinutes(60)
        };
        return Task.FromResult(_s3.GetPreSignedURL(request));
    }

    public async Task DeleteAsync(Guid documentId, string actorUserId)
    {
        await using var db = await _dbFactory.CreateDbContextAsync();
        var doc = await db.Documents.FindAsync(documentId)
            ?? throw new NotFoundException($"Document {documentId} not found");

        try
        {
            await _s3.DeleteObjectAsync(BucketName, doc.S3Key);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "[Docs] S3 delete failed for {Key} — removing DB record anyway", doc.S3Key);
        }

        db.Documents.Remove(doc);
        await db.SaveChangesAsync();
    }
}

public record PresignedUploadResult(string UploadUrl, string S3Key);
```

**Add to `Program.cs`:**
```csharp
// AWS S3 — uses deployer IAM role (no explicit credentials needed in ECS with task role)
builder.Services.AddDefaultAWSOptions(builder.Configuration.GetAWSOptions());
builder.Services.AddAWSService<IAmazonS3>();
builder.Services.AddScoped<IDocumentService, DocumentService>();
```

**Note on IAM:** The ECS task role needs `s3:GetObject`, `s3:PutObject`, `s3:DeleteObject` on `arn:aws:s3:::fip-cowork-workspaces/famos/*`. This is an infra change — Rhodey adds it to the ECS task role policy. No new env vars needed (SDK auto-discovers role credentials in ECS).

### B5. `Services/OpportunityService.cs` — Include Documents in `GetByIdAsync`

Add `.Include(o => o.Documents)` to the `GetByIdAsync` query chain.

### B6. `Components/Pages/Opportunity/Panels/DocumentsPanel.razor` (new)

Documents upload uses a two-step flow: the component gets a presigned PUT URL, the browser uploads the file directly to S3 via JavaScript interop (or `HttpClient` from the server — spec uses server-side upload via `IDocumentService`). Since Blazor Server can't do browser-direct S3 PUT, the file bytes go Server → S3 server-side.

```razor
@namespace FamOs.Web.Components.Panels
@inject IDocumentService DocSvc
@inject UserSessionService UserSession
@inject ISnackbar Snackbar
@using FamOs.Web.Data.Entities
@using FamOs.Web.Theme
@using Amazon.S3

<div class="famos-panel">

    <div class="famos-panel-header">
        <span class="famos-panel-title">Documents</span>
        @if (!Opportunity.IsClosed)
        {
            <MudButton Class="famos-btn-outline-sm"
                       StartIcon="@FamosIcons.Upload"
                       OnClick="OpenFilePicker">
                Upload
            </MudButton>
        }
    </div>

    <InputFile id="doc-file-input" OnChange="OnFileSelected"
               style="display:none;" />

    @if (_uploading)
    {
        <MudProgressLinear Indeterminate="true" Color="Color.Primary" Class="mb-2" />
        <div class="famos-meta-text">Uploading @_uploadingName...</div>
    }

    @if (!Opportunity.Documents.Any() && !_uploading)
    {
        <div class="famos-empty-state">
            <MudIcon Icon="@FamosIcons.Document" Class="famos-empty-icon" />
            <div>No documents uploaded yet.</div>
        </div>
    }
    else
    {
        @* Category picker for next upload *@
        @if (!Opportunity.IsClosed)
        {
            <div style="margin-bottom: 8px; display: flex; align-items: center; gap: 8px;">
                <span class="famos-meta-text">Category:</span>
                <MudSelect Class="famos-select" @bind-Value="_uploadCategory"
                           Style="max-width: 180px;">
                    @foreach (var cat in Enum.GetValues<DocumentCategory>())
                    {
                        <MudSelectItem Value="cat">@cat</MudSelectItem>
                    }
                </MudSelect>
            </div>
        }

        <div class="famos-doc-list">
            @foreach (var doc in Opportunity.Documents.OrderByDescending(d => d.UploadedAt))
            {
                <div class="famos-doc-row">
                    <MudIcon Icon="@FamosIcons.Document"
                             Style="font-size:16px; color:var(--muted); flex-shrink:0;" />
                    <div class="famos-contact-info">
                        <div class="famos-contact-name">@doc.FileName</div>
                        <div class="famos-contact-meta">
                            @doc.DocumentCategory · @doc.UploadedAt.ToLocalTime().ToString("MMM d, yyyy")
                            @if (!string.IsNullOrEmpty(doc.UploadedBy))
                            {
                                <span> · @doc.UploadedBy</span>
                            }
                        </div>
                    </div>
                    <div style="display:flex; gap:4px;">
                        <MudButton Class="famos-btn-icon-sm"
                                   StartIcon="@FamosIcons.Download"
                                   OnClick="() => DownloadDoc(doc.S3Key)">
                        </MudButton>
                        @if (!Opportunity.IsClosed)
                        {
                            <MudButton Class="famos-btn-icon-sm"
                                       StartIcon="@FamosIcons.Delete"
                                       OnClick="() => DeleteDoc(doc.Id)">
                            </MudButton>
                        }
                    </div>
                </div>
            }
        </div>
    }

    @if (_uploadError != null)
    {
        <MudAlert Severity="Severity.Error" Class="mt-2">@_uploadError</MudAlert>
    }

</div>

@code {
    [Parameter, EditorRequired] public Opportunity Opportunity { get; set; } = default!;
    [Parameter] public EventCallback OnUpdated { get; set; }

    private bool _uploading;
    private string _uploadingName = "";
    private string? _uploadError;
    private DocumentCategory _uploadCategory = DocumentCategory.Other;

    private void OpenFilePicker()
    {
        // Tony: wire this via JSInterop document.getElementById('doc-file-input').click()
        // or use MudFileUpload component if available in MudBlazor v7
    }

    private async Task OnFileSelected(InputFileChangeEventArgs e)
    {
        _uploadError = null;
        _uploading   = true;
        _uploadingName = e.File.Name;
        StateHasChanged();

        try
        {
            const long maxSize = 25 * 1024 * 1024; // 25MB
            using var stream   = e.File.OpenReadStream(maxSize);
            var bytes          = new byte[e.File.Size];
            _ = await stream.ReadAsync(bytes);

            var userId  = await UserSession.GetUserIdAsync();
            var s3Key   = $"famos/documents/{Opportunity.Id}/{e.File.Name}";

            // Server-side upload: Blazor server sends to S3 directly
            using var s3Stream = new MemoryStream(bytes);
            await (DocSvc as DocumentService)!.UploadRawAsync(
                s3Key, s3Stream, e.File.ContentType);

            var docId = await DocSvc.RecordUploadAsync(
                Opportunity.Id, e.File.Name, e.File.ContentType,
                s3Key, _uploadCategory, userId);

            // Add to local list so UI updates without reload
            Opportunity.Documents.Add(new OpportunityDocument
            {
                Id               = docId,
                OpportunityId    = Opportunity.Id,
                FileName         = e.File.Name,
                FileType         = e.File.ContentType,
                S3Key            = s3Key,
                DocumentCategory = _uploadCategory,
                UploadedAt       = DateTime.UtcNow,
                UploadedBy       = userId,
            });
            Snackbar.Add($"{e.File.Name} uploaded.", Severity.Success);
            await OnUpdated.InvokeAsync();
        }
        catch (Exception ex)
        {
            _uploadError = $"Upload failed: {ex.Message}";
        }
        finally
        {
            _uploading = false;
            StateHasChanged();
        }
    }

    private async Task DownloadDoc(string s3Key)
    {
        var url = await DocSvc.GetDownloadUrlAsync(s3Key);
        // Tony: open in new tab via JSInterop
        // JS: window.open(url, '_blank')
        // Inject IJSRuntime and call: await JS.InvokeVoidAsync("open", url, "_blank");
        Snackbar.Add("Download link opened.", Severity.Info);
    }

    private async Task DeleteDoc(Guid docId)
    {
        var userId = await UserSession.GetUserIdAsync();
        await DocSvc.DeleteAsync(docId, userId);
        Opportunity.Documents.RemoveAll(d => d.Id == docId);
        Snackbar.Add("Document deleted.", Severity.Info);
        StateHasChanged();
    }
}
```

**Add `UploadRawAsync` to `DocumentService.cs`** (used by `DocumentsPanel` for server-side upload):

```csharp
/// <summary>Upload raw bytes to S3 directly (server-side Blazor).</summary>
public async Task UploadRawAsync(string s3Key, Stream data, string contentType)
{
    var request = new PutObjectRequest
    {
        BucketName  = BucketName,
        Key         = s3Key,
        InputStream = data,
        ContentType = contentType,
    };
    await _s3.PutObjectAsync(request);
    _logger.LogInformation("[Docs] S3 upload complete: {Key}", s3Key);
}
```

**Add to `IDocumentService`:**
```csharp
Task UploadRawAsync(string s3Key, Stream data, string contentType);
```

**Add to `famos.css`:**
```css
.famos-doc-list { display: flex; flex-direction: column; gap: 6px; }
.famos-doc-row {
    display: flex;
    align-items: center;
    gap: 10px;
    padding: 8px 12px;
    border: 1px solid var(--border);
    border-radius: 8px;
    background: var(--white);
}
.famos-meta-text { font-size: 11.5px; color: var(--muted); }
```

---

## Part C — UW Completeness Checklist

### C1. `Services/UwCompletenessService.cs` (new)

Pure evaluation — no DB writes. Returns a score and list of unmet items.

```csharp
using FamOs.Web.Data.Entities;
using FamOs.Web.Domain;

namespace FamOs.Web.Services;

public class UwCompletenessResult
{
    public int   Score           { get; init; }   // 0–100
    public bool  CanRouteToMarket { get; init; }  // Score >= 60
    public List<string> UnmetItems { get; init; } = new();
    public List<string> MetItems   { get; init; } = new();
}

/// <summary>
/// Computes the underwriting completeness score for an opportunity.
/// Pure function — no DB calls. All data must be loaded (Contacts, Submissions, Quotes).
/// </summary>
public class UwCompletenessService
{
    private record CheckItem(string Description, Func<Opportunity, bool> IsMet, int Weight);

    private static readonly List<CheckItem> Items = new()
    {
        new("Intake questionnaire filled",
            o => !string.IsNullOrEmpty(o.IntakeResponsesJson), 20),

        new("At least one carrier submission created",
            o => o.Submissions.Any(), 15),

        new("All submissions have carrier name and coverage types",
            o => o.Submissions.Any()
                && o.Submissions.All(s =>
                    !string.IsNullOrEmpty(s.CarrierName)
                    && !string.IsNullOrEmpty(s.CoverageTypes)), 10),

        new("At least one quote received",
            o => o.Submissions.Any(s => s.Status == SubmissionStatus.QuoteReceived)
              || o.Quotes.Any(), 20),

        new("Primary contact assigned",
            o => o.Contacts.Any(c => c.ContactType == ContactType.Primary), 15),

        new("Target effective date set",
            o => o.EffectiveDateTarget.HasValue, 10),

        new("Estimated premium set",
            o => o.EstimatedPremium.HasValue, 10),
    };

    public UwCompletenessResult Evaluate(Opportunity opp)
    {
        var metItems   = Items.Where(i => i.IsMet(opp)).ToList();
        var unmetItems = Items.Where(i => !i.IsMet(opp)).ToList();

        var score = metItems.Sum(i => i.Weight);

        return new UwCompletenessResult
        {
            Score            = Math.Min(score, 100),
            CanRouteToMarket = score >= 60,
            MetItems         = metItems.Select(i => i.Description).ToList(),
            UnmetItems       = unmetItems.Select(i => i.Description).ToList(),
        };
    }
}
```

**Add to `Program.cs`:**
```csharp
builder.Services.AddScoped<UwCompletenessService>();
```

### C2. `Domain/LifecycleCommandService.cs` — Stage Gate for RouteToMarket

In `RouteToMarketAsync`, after the existing stage check and submission check, add:

```csharp
// UW completeness gate: requires >= 60%
var completer = new UwCompletenessService();
var result    = completer.Evaluate(opp);
if (!result.CanRouteToMarket)
{
    var missing = string.Join("; ", result.UnmetItems);
    throw new LifecycleValidationException(
        $"UW completeness is {result.Score}% — must reach 60% before routing to market. " +
        $"Incomplete: {missing}");
}
```

**Note:** `LifecycleCommandService` must include `Contacts` and `Quotes` when loading the opportunity, in addition to the existing includes. Update `LoadOpportunityAsync`:

```csharp
private async Task<Opportunity> LoadOpportunityAsync(Guid id)
{
    return await _db.Opportunities
        .Include(o => o.Submissions)
        .Include(o => o.Quotes)
        .Include(o => o.Contacts)      // ← add Sprint 6
        .Include(o => o.Proposals)
        .Include(o => o.Tasks.Where(t => t.Status == "open"))
        .Include(o => o.Flags)
        .FirstOrDefaultAsync(o => o.Id == id)
        ?? throw new NotFoundException($"Opportunity {id} not found");
}
```

### C3. `Components/Pages/Opportunity/OpportunityWorkspace.razor` — Add Completeness Meter

Add to the top of the workspace `@inject` block:
```razor
@inject UwCompletenessService UwCompleteness
```

After the existing page header div (opportunity name + stage pills), add the completeness meter — **only shown for UnderwritingPrep stage**:

```razor
@if (_opp.LifecycleStage == LifecycleStage.UnderwritingPrep)
{
    var completeness = UwCompleteness.Evaluate(_opp);
    var meterColor   = completeness.Score >= 80 ? "var(--green)"
                     : completeness.Score >= 50 ? "var(--amber)"
                     : "var(--red)";
    <div class="famos-completeness-bar mb-4">
        <div style="display:flex; justify-content:space-between; align-items:center; margin-bottom:5px;">
            <span class="famos-meta-text">UW Completeness</span>
            <span style="font-size:13px; font-weight:700; color:@meterColor;">
                @completeness.Score%
            </span>
        </div>
        <div style="height:6px; background:var(--border); border-radius:3px;">
            <div style="@($"height:6px; width:{completeness.Score}%; background:{meterColor}; border-radius:3px; transition:width 0.4s;")">
            </div>
        </div>
        @if (completeness.UnmetItems.Any())
        {
            <div class="famos-meta-text mt-1">
                Missing: @string.Join(" · ", completeness.UnmetItems)
            </div>
        }
    </div>
}
```

**Add to `famos.css`:**
```css
.famos-completeness-bar {
    padding: 10px 14px;
    border: 1px solid var(--border);
    border-radius: 8px;
    background: var(--cream);
}
```

---

## Part D — Owner Assignment UI

### D1. `AffinityConfig.cs` — Add Users Array

```csharp
public class AffinityConfig
{
    public string  AffinityId    { get; set; } = "famos";
    public string  DisplayName   { get; set; } = "Fortress Affinity Management OS";
    public string  PortalName    { get; set; } = "FAM OS";
    public string  LogoPath      { get; set; } = "";
    public string? PrimaryColor  { get; set; }
    public string? AccentColor   { get; set; }

    /// <summary>
    /// Known users for this affinity program.
    /// Populated via appsettings. Phase 1: manual list. Phase 2: pulled from identity provider.
    /// </summary>
    public List<AffinityUser> Users { get; set; } = new();
}

public class AffinityUser
{
    public string UserId      { get; set; } = "";
    public string DisplayName { get; set; } = "";
    public string Initials    { get; set; } = "";
}
```

**Update `appsettings.json`:**
```json
"AffinityConfig": {
    "AffinityId": "tig",
    "DisplayName": "Titan Insurance Group",
    "PortalName": "Titan Dashboard",
    "LogoPath": "/images/affinity/tig-logo.svg",
    "Users": [
        {
            "UserId": "lauren.tig@titaninsurancegroup.com",
            "DisplayName": "Lauren",
            "Initials": "LL"
        },
        {
            "UserId": "fred.white@fortressam.ai",
            "DisplayName": "Fred",
            "Initials": "FW"
        }
    ]
}
```

### D2. `Domain/LifecycleCommandService.cs` — Add `AssignOwnerAsync`

```csharp
/// <summary>Assign a new owner to an opportunity.</summary>
public async Task AssignOwnerAsync(Guid opportunityId, string newOwnerUserId, string actorUserId)
{
    await using var tx = await _db.Database.BeginTransactionAsync();
    var opp = await LoadOpportunityAsync(opportunityId);
    Validate(!opp.IsClosed, "Cannot reassign owner of a closed opportunity");

    var previous      = opp.OwnerUserId;
    opp.OwnerUserId   = newOwnerUserId;
    opp.UpdatedAt     = DateTime.UtcNow;

    await WriteActivityAsync(opp.Id, "owner_assigned",
        $"Owner changed from {previous} to {newOwnerUserId}", actorUserId);

    await _db.SaveChangesAsync();
    await tx.CommitAsync();
}
```

### D3. `Components/Dialogs/OwnerPickerDialog.razor` (new)

```razor
@inject IOptions<AffinityConfig> AffinityOptions
@using FamOs.Web.Theme
@using Microsoft.Extensions.Options

<MudDialog>
    <TitleContent>Assign Owner</TitleContent>
    <DialogContent>
        <MudText Typo="Typo.body2" Class="mb-3" Color="Color.Secondary">
            Select the ER responsible for this opportunity.
        </MudText>
        <div class="famos-owner-list">
            @foreach (var user in _users)
            {
                var isSelected = _selectedId == user.UserId;
                <div class="@($"famos-owner-row{(isSelected ? " famos-owner-row--selected" : "")}")"
                     @onclick="() => _selectedId = user.UserId">
                    <div class="famos-contact-avatar" style="@(isSelected ? "background:var(--sky);" : "background:var(--navy-mid);")">
                        @user.Initials
                    </div>
                    <div class="famos-contact-info">
                        <div class="famos-contact-name">@user.DisplayName</div>
                        <div class="famos-contact-meta">@user.UserId</div>
                    </div>
                    @if (isSelected)
                    {
                        <MudIcon Icon="@FamosIcons.Check" Style="color:var(--sky); font-size:18px;" />
                    }
                </div>
            }
        </div>
    </DialogContent>
    <DialogActions>
        <MudButton Class="famos-btn-outline" OnClick="Cancel">Cancel</MudButton>
        <MudButton Class="famos-btn-primary" OnClick="Submit"
                   Disabled="@string.IsNullOrEmpty(_selectedId)">
            Assign
        </MudButton>
    </DialogActions>
</MudDialog>

@code {
    [CascadingParameter] IMudDialogInstance MudDialog { get; set; } = default!;
    [Parameter] public string CurrentOwnerId { get; set; } = "";

    private List<AffinityUser> _users = new();
    private string _selectedId = "";

    protected override void OnInitialized()
    {
        _users      = AffinityOptions.Value.Users;
        _selectedId = CurrentOwnerId;
    }

    private void Cancel() => MudDialog.Cancel();

    private void Submit()
    {
        if (string.IsNullOrEmpty(_selectedId)) return;
        MudDialog.Close(DialogResult.Ok(_selectedId));
    }
}
```

**Add to `famos.css`:**
```css
.famos-owner-list { display: flex; flex-direction: column; gap: 4px; }
.famos-owner-row {
    display: flex;
    align-items: center;
    gap: 10px;
    padding: 10px 12px;
    border: 1px solid var(--border);
    border-radius: 8px;
    cursor: pointer;
    transition: border-color 0.15s;
}
.famos-owner-row:hover { border-color: var(--sky); }
.famos-owner-row--selected { border-color: var(--sky); background: rgba(0,144,208,0.05); }
```

### D4. `Components/Pages/Opportunity/OpportunityWorkspace.razor` — Add "Assign Owner" button

In the workspace header button row (where "Park" and "Close" buttons are), add:

```razor
<MudButton Class="famos-btn-outline-sm" OnClick="OpenOwnerPicker">
    @if (!string.IsNullOrEmpty(_opp.OwnerUserId))
    {
        <span>Owner: @GetOwnerDisplay(_opp.OwnerUserId)</span>
    }
    else
    {
        <span>Assign Owner</span>
    }
</MudButton>
```

In `@code`:
```csharp
[Inject] private IOptions<AffinityConfig> AffinityOptions { get; set; } = default!;

private async Task OpenOwnerPicker()
{
    var dialog = await DialogService.ShowAsync<OwnerPickerDialog>(
        "Assign Owner",
        new DialogParameters { ["CurrentOwnerId"] = _opp?.OwnerUserId ?? "" });
    var result = await dialog.Result;
    if (result is { Canceled: false } && result.Data is string newOwner)
    {
        var userId = await UserSession.GetUserIdAsync();
        await Lifecycle.AssignOwnerAsync(_opp!.Id, newOwner, userId);
        Snackbar.Add("Owner assigned.", Severity.Success);
        await Reload();
    }
}

private string GetOwnerDisplay(string ownerUserId)
{
    var user = AffinityOptions.Value.Users.FirstOrDefault(u => u.UserId == ownerUserId);
    return user?.DisplayName ?? ownerUserId.Split('@')[0];
}
```

---

## Part E — Opportunity Search (Topbar)

### E1. `Services/OpportunitySearchService.cs` (new)

```csharp
using Microsoft.EntityFrameworkCore;
using FamOs.Web.Data;
using FamOs.Web.Data.Entities;

namespace FamOs.Web.Services;

public class OpportunitySearchService
{
    private readonly IDbContextFactory<FamOsDbContext> _dbFactory;

    public OpportunitySearchService(IDbContextFactory<FamOsDbContext> dbFactory)
        => _dbFactory = dbFactory;

    /// <summary>
    /// Returns up to 8 opportunities whose name contains the query (case-insensitive).
    /// Excludes permanently closed opportunities.
    /// </summary>
    public async Task<List<OpportunitySearchResult>> SearchAsync(string query)
    {
        if (string.IsNullOrWhiteSpace(query) || query.Length < 2)
            return new();

        await using var db = await _dbFactory.CreateDbContextAsync();
        return await db.Opportunities
            .Where(o => !o.IsClosed
                && EF.Functions.Like(o.Name, $"%{query}%"))
            .OrderBy(o => o.Name)
            .Take(8)
            .Select(o => new OpportunitySearchResult
            {
                Id           = o.Id,
                Name         = o.Name,
                Stage        = o.LifecycleStage,
                Signal       = o.DominantSignal,
                Premium      = o.EstimatedPremium,
            })
            .ToListAsync();
    }
}

public class OpportunitySearchResult
{
    public Guid            Id      { get; set; }
    public string          Name    { get; set; } = "";
    public LifecycleStage  Stage   { get; set; }
    public DominantSignal  Signal  { get; set; }
    public decimal?        Premium { get; set; }
}
```

**Add to `Program.cs`:**
```csharp
builder.Services.AddScoped<OpportunitySearchService>();
```

### E2. `Components/Layout/MainLayout.razor` — Wire the Topbar Search

Replace the current static `<input type="text" placeholder="Search opportunities..." />` block with a Blazor component. The topbar search must:
- Debounce input (300ms)
- Show a dropdown panel below the input with max 8 results
- Dismiss the dropdown on outside click or ESC
- Navigate on result click

Replace the entire `famos-topbar-search` div:

```razor
@inject OpportunitySearchService SearchSvc
@inject NavigationManager Nav

@* ── inside MudMainContent, replace the famos-topbar-search div ── *@
<div class="famos-topbar-search famos-topbar-search--interactive"
     @onfocusout="HandleFocusOut">
    <span class="famos-topbar-search-icon">
        <MudIcon Icon="@FamosIcons.Search" Style="font-size:13px; color:#9ca3af;" />
    </span>
    <input type="text"
           placeholder="Search opportunities..."
           @bind="_searchText"
           @bind:event="oninput"
           @oninput="OnSearchInput"
           @onkeydown="OnKeyDown"
           @onfocus="() => _searchFocused = true" />

    @if (_searchResults.Any() && _searchFocused)
    {
        <div class="famos-search-dropdown">
            @foreach (var result in _searchResults)
            {
                <div class="famos-search-result"
                     @onmousedown:preventDefault="true"
                     @onclick="() => NavigateToResult(result)">
                    <div class="famos-contact-name">@result.Name</div>
                    <div class="famos-contact-meta">
                        @GetStageLabel(result.Stage)
                        @(result.Premium.HasValue ? $" · ${result.Premium:N0}" : "")
                    </div>
                </div>
            }
        </div>
    }
</div>
```

In the `@code` block (add to existing `MainLayout.razor` `@code`):

```csharp
private string _searchText     = "";
private bool   _searchFocused  = false;
private List<OpportunitySearchResult> _searchResults = new();
private CancellationTokenSource? _searchCts;

private async Task OnSearchInput(ChangeEventArgs e)
{
    _searchText = e.Value?.ToString() ?? "";
    _searchCts?.Cancel();
    _searchCts = new CancellationTokenSource();
    var ct = _searchCts.Token;

    try
    {
        await Task.Delay(300, ct);  // 300ms debounce
        _searchResults = await SearchSvc.SearchAsync(_searchText);
        StateHasChanged();
    }
    catch (OperationCanceledException) { /* debounced */ }
}

private void OnKeyDown(KeyboardEventArgs e)
{
    if (e.Key == "Escape")
    {
        _searchText    = "";
        _searchResults = new();
        _searchFocused = false;
    }
}

private void HandleFocusOut()
{
    // Small delay to allow click to register before dismissing
    Task.Delay(150).ContinueWith(_ =>
    {
        _searchFocused = false;
        InvokeAsync(StateHasChanged);
    });
}

private void NavigateToResult(OpportunitySearchResult result)
{
    _searchText    = "";
    _searchResults = new();
    _searchFocused = false;
    Nav.NavigateTo($"/opportunity/{result.Id}");
}

private static string GetStageLabel(LifecycleStage stage) => stage switch
{
    LifecycleStage.Intake           => "Intake",
    LifecycleStage.UnderwritingPrep => "App Review",
    LifecycleStage.Marketed         => "Submitted",
    LifecycleStage.QuotesReceived   => "Quotes In",
    LifecycleStage.ClientDecision   => "Proposal",
    LifecycleStage.Binding          => "Binding",
    LifecycleStage.Bound            => "Bound",
    _                               => stage.ToString()
};
```

**Add to `famos.css`:**
```css
.famos-topbar-search--interactive { position: relative; }

.famos-search-dropdown {
    position: absolute;
    top: calc(100% + 4px);
    left: 0;
    right: 0;
    background: var(--white);
    border: 1px solid var(--border);
    border-radius: 8px;
    box-shadow: 0 4px 16px rgba(0,0,0,0.12);
    z-index: 9999;
    overflow: hidden;
    min-width: 280px;
}

.famos-search-result {
    padding: 9px 14px;
    cursor: pointer;
    border-bottom: 1px solid var(--border);
}
.famos-search-result:last-child { border-bottom: none; }
.famos-search-result:hover { background: var(--cream); }
```

---

## Part F — Activity Log Panel

### F1. `Domain/LifecycleCommandService.cs` — Add `AddNoteAsync`

```csharp
/// <summary>
/// Adds a manual note to the opportunity activity log.
/// Does NOT change lifecycle stage.
/// </summary>
public async Task AddNoteAsync(Guid opportunityId, string noteText, string actorUserId)
{
    await using var tx = await _db.Database.BeginTransactionAsync();
    var opp = await LoadOpportunityAsync(opportunityId);
    Validate(!opp.IsClosed, "Cannot add notes to a closed opportunity");
    Validate(!string.IsNullOrWhiteSpace(noteText), "Note text cannot be empty");

    await WriteActivityAsync(opp.Id, "note", noteText.Trim(), actorUserId);
    opp.UpdatedAt = DateTime.UtcNow;

    await _db.SaveChangesAsync();
    await tx.CommitAsync();
}
```

### F2. `Services/OpportunityService.cs` — Include Activities in `GetByIdAsync`

Add `.Include(o => o.Activities.OrderByDescending(a => a.OccurredAt))` to the `GetByIdAsync` query chain.

**Note:** EF Core supports `Include` with ordering (`.Include(o => o.Activities.OrderByDescending(...))`) in EF Core 7+. This project uses EF Core 9. Confirmed safe.

### F3. `Components/Pages/Opportunity/Panels/ActivityPanel.razor` (new)

```razor
@namespace FamOs.Web.Components.Panels
@inject LifecycleCommandService Lifecycle
@inject UserSessionService UserSession
@inject ISnackbar Snackbar
@using FamOs.Web.Data.Entities
@using FamOs.Web.Theme

<div class="famos-panel">

    <div class="famos-panel-header">
        <span class="famos-panel-title">Activity</span>
    </div>

    @* Manual note entry *@
    @if (!Opportunity.IsClosed)
    {
        <div class="famos-note-entry mb-4">
            <MudTextField Class="famos-input"
                          @bind-Value="_noteText"
                          Placeholder="Add a note..."
                          Lines="2" />
            <div style="margin-top: 6px; text-align: right;">
                <MudButton Class="famos-btn-outline-sm"
                           OnClick="AddNote"
                           Disabled="@(string.IsNullOrWhiteSpace(_noteText) || _saving)">
                    Add Note
                </MudButton>
            </div>
        </div>
    }

    @* Activity log *@
    @if (!Opportunity.Activities.Any())
    {
        <div class="famos-empty-state">
            <MudIcon Icon="@FamosIcons.Note" Class="famos-empty-icon" />
            <div>No activity yet. Activity is recorded automatically as the opportunity progresses.</div>
        </div>
    }
    else
    {
        <div class="famos-activity-list">
            @foreach (var act in Opportunity.Activities)
            {
                <div class="famos-activity-row">
                    <div class="famos-activity-dot @GetDotClass(act.EventType)"></div>
                    <div class="famos-activity-body">
                        <div class="famos-activity-desc">@act.Description</div>
                        <div class="famos-activity-meta">
                            @act.OccurredAt.ToLocalTime().ToString("MMM d, yyyy 'at' h:mm tt")
                            @if (!string.IsNullOrEmpty(act.ActorUserId))
                            {
                                <span> · @GetActorDisplay(act.ActorUserId)</span>
                            }
                        </div>
                    </div>
                </div>
            }
        </div>
    }

</div>

@code {
    [Parameter, EditorRequired] public Opportunity Opportunity { get; set; } = default!;
    [Parameter] public EventCallback OnUpdated { get; set; }

    private string _noteText = "";
    private bool   _saving;

    private async Task AddNote()
    {
        if (string.IsNullOrWhiteSpace(_noteText)) return;
        _saving = true;
        try
        {
            var userId = await UserSession.GetUserIdAsync();
            await Lifecycle.AddNoteAsync(Opportunity.Id, _noteText, userId);

            // Add to local list for immediate display
            Opportunity.Activities.Insert(0, new Activity
            {
                EventType   = "note",
                Description = _noteText.Trim(),
                ActorUserId = userId,
                OccurredAt  = DateTime.UtcNow,
            });

            _noteText = "";
            Snackbar.Add("Note added.", Severity.Success);
            await OnUpdated.InvokeAsync();
        }
        catch (Exception ex)
        {
            Snackbar.Add($"Error: {ex.Message}", Severity.Error);
        }
        finally { _saving = false; }
    }

    private static string GetDotClass(string eventType) => eventType switch
    {
        "note"                  => "dot-note",
        "opportunity_closed"    => "dot-closed",
        "intake_saved"          => "dot-info",
        "submission_created"    => "dot-info",
        "quote_scraped"         => "dot-info",
        "contact_added"         => "dot-info",
        "owner_assigned"        => "dot-info",
        _                       => "dot-default"
    };

    private string GetActorDisplay(string userId)
    {
        // Simple initials extraction — same logic as OpportunityCard
        var atIdx = userId.IndexOf('@');
        var local = atIdx > 0 ? userId[..atIdx] : userId;
        var parts = local.Split(new[] { '.', '_', '-' }, StringSplitOptions.RemoveEmptyEntries);
        return parts.Length >= 2
            ? $"{char.ToUpper(parts[0][0])}{char.ToUpper(parts[1][0])}"
            : local.Length > 0 ? local[..Math.Min(local.Length, 2)].ToUpper() : "?";
    }
}
```

**Add to `famos.css`:**
```css
.famos-note-entry { /* container for note input + button */ }

.famos-activity-list { display: flex; flex-direction: column; gap: 0; }
.famos-activity-row {
    display: flex;
    gap: 12px;
    padding: 10px 0;
    border-bottom: 1px solid var(--border);
    align-items: flex-start;
}
.famos-activity-row:last-child { border-bottom: none; }

.famos-activity-dot {
    width: 8px; height: 8px; border-radius: 50%;
    flex-shrink: 0; margin-top: 5px;
}
.dot-default  { background: var(--sky); }
.dot-info     { background: var(--sky); }
.dot-note     { background: var(--amber); }
.dot-closed   { background: var(--red); }

.famos-activity-body { flex: 1; min-width: 0; }
.famos-activity-desc { font-size: 13px; color: var(--text); }
.famos-activity-meta { font-size: 11px; color: var(--muted); margin-top: 2px; }
```

### F4. `Components/Pages/Opportunity/OpportunityWorkspace.razor` — Wire New Panels

Replace the existing hardcoded activity timeline at the bottom with the new `ActivityPanel`. Also add `ContactsPanel`, `DocumentsPanel`, and `QuoteScraperPanel` (from Sprint 5) as always-visible secondary panels below the stage-specific panel.

In the workspace, after the `@switch` block, replace the existing activity timeline section with:

```razor
@* ── Always-visible secondary panels ─────────────────────── *@
<div class="famos-secondary-panels mt-4">
    @* Quote Scraper — only during and after submission stages *@
    @if (_opp.LifecycleStage is LifecycleStage.Marketed or LifecycleStage.QuotesReceived)
    {
        <QuoteScraperPanel Opportunity="_opp" OnUpdated="Reload" />
    }

    <ContactsPanel Opportunity="_opp" OnUpdated="Reload" />
    <DocumentsPanel Opportunity="_opp" OnUpdated="Reload" />
    <ActivityPanel  Opportunity="_opp" OnUpdated="Reload" />
</div>
```

**Add `@using` directives** for the new panel namespaces to `_Imports.razor` or top of workspace file:
```razor
@using FamOs.Web.Components.Panels
```

---

## FamosIcons.cs — Add Missing Icons

The `DESIGN-SYSTEM.md` requires all icons to use `FamosIcons.*`. Add any icons used in Sprint 6 that don't already exist:

```csharp
// Add to FamosIcons.cs in the Data section:
public const string Contacts   = Icons.Material.Outlined.Contacts;
public const string Attach     = Icons.Material.Outlined.AttachFile;
public const string NoteAlt    = Icons.Material.Outlined.NoteAlt;
public const string AssignUser = Icons.Material.Outlined.AssignmentInd;
```

Update any Sprint 6 component that used `FamosIcons.Person`, `FamosIcons.Document`, `FamosIcons.Note` to use the most semantically correct constant. All four are acceptable — just be consistent.

---

## File Summary

### New Files (9)
```
fip/famos/src/FamOs.Web/Data/Entities/Contact.cs
fip/famos/src/FamOs.Web/Data/Entities/OpportunityDocument.cs
fip/famos/src/FamOs.Web/Services/UwCompletenessService.cs
fip/famos/src/FamOs.Web/Services/DocumentService.cs
fip/famos/src/FamOs.Web/Services/OpportunitySearchService.cs
fip/famos/src/FamOs.Web/Components/Dialogs/AddContactDialog.razor
fip/famos/src/FamOs.Web/Components/Dialogs/OwnerPickerDialog.razor
fip/famos/src/FamOs.Web/Components/Pages/Opportunity/Panels/ContactsPanel.razor
fip/famos/src/FamOs.Web/Components/Pages/Opportunity/Panels/DocumentsPanel.razor
fip/famos/src/FamOs.Web/Components/Pages/Opportunity/Panels/ActivityPanel.razor
```

*(10 new files — includes ActivityPanel)*

### Modified Files (16)
```
fip/famos/src/FamOs.Web/FamOs.Web.csproj                          (add AWSSDK.S3 package)
fip/famos/src/FamOs.Web/AffinityConfig.cs                         (add Users array + AffinityUser class)
fip/famos/src/FamOs.Web/appsettings.json                          (add Users array seed data)
fip/famos/src/FamOs.Web/Data/Entities/Opportunity.cs              (Contacts, Documents nav props, PrimaryContactId)
fip/famos/src/FamOs.Web/Data/FamOsDbContext.cs                    (Contact, OpportunityDocument entity config + DbSets)
fip/famos/src/FamOs.Web/Domain/LifecycleCommandService.cs         (AddContact, RemoveContact, AssignOwner, AddNote, LoadOpportunity includes, RouteToMarket UW gate)
fip/famos/src/FamOs.Web/Services/OpportunityService.cs            (Include Contacts, Documents, Activities in GetByIdAsync)
fip/famos/src/FamOs.Web/Theme/FamosIcons.cs                       (add Contacts, Attach, NoteAlt, AssignUser)
fip/famos/src/FamOs.Web/Program.cs                                (register AWS, DocumentService, UwCompletenessService, OpportunitySearchService)
fip/famos/src/FamOs.Web/Components/Layout/MainLayout.razor        (wire topbar search)
fip/famos/src/FamOs.Web/Components/Pages/Opportunity/OpportunityWorkspace.razor  (completeness meter, Assign Owner button, secondary panels)
fip/famos/src/FamOs.Web/wwwroot/css/famos.css                     (contact, doc, activity, search, completeness, owner CSS classes)
```

**DO NOT touch:** FAIT, FIRM, FORMS, FipShared, Sprint 5 service files (QuoteScraperService, AgingService, HubSpotService).

---

## Acceptance Criteria

### Part A — Contacts
1. ContactsPanel shows on OpportunityWorkspace below the stage panel; empty state shown when no contacts exist
2. "Add Contact" opens the dialog; submitting a Primary contact creates a `contacts` row with `contact_type = 0` in DB
3. Attempting to add a second Primary contact shows the validation error "This opportunity already has a primary contact" and does NOT create a second row
4. "Remove" button (trash icon) on a contact deletes the row; `primary_contact_id` on `opportunities` is cleared if the primary was deleted
5. Contact type badge renders "Primary" in sky-blue pill styling on the primary contact row

### Part B — Documents
6. DocumentsPanel shows on OpportunityWorkspace; empty state shown when no documents uploaded
7. Clicking "Upload" opens the file picker; selecting a file uploads it to S3 at `famos/documents/{opportunityId}/{filename}`
8. After upload, the file appears in the document list with correct filename, category, and upload timestamp
9. "Download" icon generates a presigned GET URL and opens it in a new tab
10. "Delete" removes the document from both S3 and the DB
11. File size limit: files > 25MB show an error and do NOT upload

### Part C — UW Completeness
12. On an INTAKE or UnderwritingPrep opportunity with no intake form, no submissions, no contacts: completeness bar shows 0% in red
13. Adding all 7 checklist items brings the bar to 100% in green
14. Attempting to click "Route to Market" on an opportunity with completeness < 60% shows the validation error naming the missing items
15. The completeness bar is NOT shown outside the UnderwritingPrep stage

### Part D — Owner Assignment
16. "Assign Owner" button appears in the OpportunityWorkspace header
17. Clicking it opens the OwnerPickerDialog listing the 2 seeded TIG users
18. Selecting a user and clicking "Assign" updates `opportunities.owner_user_id` and shows the owner display name on the button
19. The pipeline card owner initials update on the next pipeline page load

### Part E — Search
20. Typing 2+ characters in the topbar search box shows a dropdown with up to 8 matching opportunities
21. Results appear within 400ms of stopping typing (300ms debounce + query time)
22. Clicking a result navigates to `/opportunity/{guid}`
23. Pressing ESC clears the search text and closes the dropdown
24. Searching for a closed opportunity returns no results

### Part F — Activity Log
25. ActivityPanel shows on OpportunityWorkspace; existing lifecycle events appear in reverse-chronological order
26. Entering text and clicking "Add Note" creates an `Activity` row with `event_type = 'note'`
27. The new note appears at the top of the activity list immediately after submission
28. Notes panel is read-only on closed opportunities (no input field shown)

---

## Clint Review Priorities

```
⚠️  HIGH: DESIGN SYSTEM COMPLIANCE — Every component in this sprint must pass
          the DESIGN-SYSTEM.md checklist. Primary violations to watch for:
          - AddContactDialog: MudTextField must use Class="famos-input" only.
            No Variant="Variant.Outlined" or Dense="true" inline.
          - DocumentsPanel: MudSelect must use Class="famos-select" only.
          - OwnerPickerDialog: MudButton must use Class="famos-btn-primary" /
            "famos-btn-outline" only.
          - If Tony uses Variant=, Color=, or Size= on ANY MudButton in Sprint 6,
            reject the PR immediately.

⚠️  HIGH: UwCompletenessService evaluates Submissions using SubmissionStatus enum
          added in Sprint 5. Confirm that Sprint 5 is deployed and the enum
          values match before Sprint 6 tests run. If Sprint 5 is not yet deployed,
          the completeness gate check for "QuoteReceived" status will always fail.

⚠️  HIGH: LoadOpportunityAsync now includes Contacts. This increases the payload
          size of every lifecycle operation. Verify the include does not cause
          N+1 queries — EF Core should JOIN, not loop. Check the EF Core query
          log for any SELECT inside a loop on Contact loading.

⚠️  HIGH: DocumentService.UploadRawAsync streams file bytes from the Blazor
          Server process to S3. For large files (e.g., 25MB), this holds the
          Blazor Server circuit open and blocks the render thread. Acceptable
          for Phase 1 MVP (small team, low volume). Flag for Phase 2 redesign
          to use presigned client-side upload instead.

⚠️  HIGH: Single Primary contact constraint is enforced in LifecycleCommandService.
          Verify it is NOT also enforced in a DB unique constraint — if a DB
          unique constraint on (opportunity_id, contact_type=0) is added, it
          would correctly enforce the rule but the Sprint 6 spec does not include
          it. Either approach is fine, but not both (double enforcement = confusing
          error messages). Choose one: app-layer validation only (spec approach).

⚠️  MEDIUM: MainLayout.razor HandleFocusOut uses Task.Delay(150ms) to let click
            events register before dismissing. This is a common Blazor workaround.
            Verify it works in the deployed browser — if the delay is too short,
            clicks on search results will dismiss the dropdown before firing the
            onClick. If the delay is too long, the dropdown stays open when the
            user clicks elsewhere on the page.

⚠️  MEDIUM: OpportunitySearchService uses EF.Functions.Like for case-insensitive
            search. On Aurora MySQL, LIKE is case-insensitive by default for
            utf8mb4_general_ci collation. Verify the `opportunities` table uses
            this collation (it should — set in Sprint 1 CREATE TABLE). If
            case-sensitivity is wrong in testing, check the column collation.

⚠️  MEDIUM: AWSSDK.S3 is a new NuGet package. Verify it resolves cleanly in the
            monorepo build. The FAMOS project does not have AWSSDK.Core listed
            separately — AWSSDK.S3 pulls it in transitively. Confirm no version
            conflict with other AWSSDK packages in the monorepo.

⚠️  LOW: affinityConfig.Users is read from appsettings.json at startup. Adding a
         new user requires a redeployment. This is the Phase 1 design — document
         it in the PR description so Lauren's team knows how to add users.

⚠️  LOW: ActivityPanel.AddNote inserts the new Activity into Opportunity.Activities
         locally (optimistic update) before the DB write completes. If the write
         fails, the local insertion is NOT rolled back — the user sees the note
         in the UI but it doesn't exist in the DB. This is acceptable for MVP.
         Tony must not call StateHasChanged before the await completes.
```

---

_Spec by Reed Richards | Sprint 6 = 10 new files, 16 modified. Contacts, documents (S3), UW completeness gate, owner picker, wired topbar search, full activity log panel. Design system compliance is mandatory on every component — Clint enforces._
