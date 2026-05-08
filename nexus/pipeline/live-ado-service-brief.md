# NEXUS LiveAdoService — Build Brief

## Objective
Replace `StubAdoService` with a fully live `AdoCreationService` that posts work items to Azure DevOps via the ADO REST API. Add per-user PAT storage (encrypted) and a project selector UI so each admin posts to their own ADO projects.

---

## Context

- **Project root:** `/home/fredw/projects/fip/nexus/src/FortressNexus.Web/`
- **ECS cluster:** `fortress-tools-cluster`, service: `nexus-web`
- **ECR repo:** `742932328420.dkr.ecr.us-east-1.amazonaws.com/nexus-web`
- **DB:** Aurora MySQL, schema `nexus`, tunnel: `ssh -i ~/.ssh/fortress-bastion.pem ec2-user@13.217.202.98 -L 3307:fortress-ai-cluster.cluster-c89acukue4d5.us-east-1.rds.amazonaws.com:3306 -N`
- **DB password:** `aws secretsmanager get-secret-value --secret-id arn:aws:secretsmanager:us-east-1:742932328420:secret:fortress-tools/dev-db-password-9ZKFmr --query SecretString --output text` (requires deployer env vars)
- **ADO org:** `FortressAffinityGroup` (fixed, from appsettings `Nexus:Ado:Organization`)
- **ADO PAT for testing:** will be provided by Fred via the UI after deploy — do NOT hardcode

---

## Mandatory Rules

- `MySqlConnectionStringBuilder` MUST include `GuidFormat = MySqlGuidFormat.None` — already set in Program.cs, do not remove
- No Cognito. No Azure Key Vault (VaultUri is blank). No new AWS SDK packages beyond what's already referenced.
- DataProtection is already wired in Program.cs with `SetApplicationName("FortressAI")` and MySQL key ring. Use it for PAT encryption — see §3.
- `IAdoService` is registered as `StubAdoService` in Program.cs. Replace with `AdoCreationService` at the end.
- Dockerfile is at `nexus/Dockerfile` — build context is the `nexus/` directory (parent of `src/`).
- ECS deploy: use `source ~/projects/ai/projects/fortress_tools/.env.deployer && export AWS_ACCESS_KEY_ID AWS_SECRET_ACCESS_KEY AWS_REGION=us-east-1` for deployer creds. Use `scripts/ecs-register-task-def.sh` if it exists; otherwise register task def manually and force-redeploy the service.

---

## 1. New Entity: `UserAdoCredential`

File: `Models/Entities/UserAdoCredential.cs`

```csharp
public class UserAdoCredential
{
    public int Id { get; set; }
    public string UserUpn { get; set; } = "";         // e.g. fwhite@fortressaffinitygroup.com
    public string EncryptedPat { get; set; } = "";    // AES encrypted via DataProtection
    public string PatHint { get; set; } = "";         // last 4 chars of raw PAT, for display
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
}
```

---

## 2. DB Migration

Migration name: `AddUserAdoCredentials`

```sql
CREATE TABLE user_ado_credentials (
    id INT NOT NULL AUTO_INCREMENT PRIMARY KEY,
    user_upn VARCHAR(200) NOT NULL,
    encrypted_pat TEXT NOT NULL,
    pat_hint VARCHAR(10) NOT NULL DEFAULT '',
    created_at DATETIME NOT NULL,
    updated_at DATETIME NOT NULL,
    UNIQUE KEY uq_user_upn (user_upn)
);

ALTER TABLE artifact_sets
    ADD COLUMN ado_project_id_selected VARCHAR(100) NULL
        COMMENT 'ADO project ID selected by user at decomp time';
```

Add `EF Core` migration via `dotnet ef migrations add AddUserAdoCredentials` then apply with `dotnet ef database update` (via tunnel — set env vars: `NEXUS_DB_HOST=127.0.0.1`, `NEXUS_DB_USER=fortress_mysql`, `NEXUS_DB_PASSWORD=<from secrets manager>`, `FIP_DB_NAME=nexus`).

---

## 3. PAT Encryption: `IAdoCredentialService` + `AdoCredentialService`

Use `IDataProtectionProvider` (already in DI) — **do not add any new packages**.

```csharp
public interface IAdoCredentialService
{
    Task<bool> HasCredentialAsync(string userUpn);
    Task SaveCredentialAsync(string userUpn, string rawPat);
    Task<string?> GetDecryptedPatAsync(string userUpn);
    Task<string?> GetPatHintAsync(string userUpn);
    Task DeleteCredentialAsync(string userUpn);
    Task<List<string>> GetProjectsAsync(string userUpn);  // calls ADO API with stored PAT
    Task<bool> ValidatePatAsync(string rawPat);            // calls GET /_apis/projects, returns true if 200
}
```

Implementation notes:
- Inject `IDataProtectionProvider`, call `CreateProtector("NexusAdoPat")` to get `IDataProtector`
- `SaveCredentialAsync`: encrypt with `protector.Protect(rawPat)`, store hint as `rawPat[^4..]`
- `GetDecryptedPatAsync`: `protector.Unprotect(encryptedPat)`
- `GetProjectsAsync`: `GET https://dev.azure.com/{org}/_apis/projects?api-version=7.1` with `Authorization: Basic {Base64(:pat)}`
- `ValidatePatAsync`: same call, return `true` if HTTP 200

---

## 4. `AdoCreationService` — Live ADO HTTP Calls

File: `Services/AdoCreationService.cs` (already exists as scaffold — fill in the TODOs)

### Auth pattern (ADO REST API)
All calls use HTTP Basic auth:
```
Authorization: Basic {Base64(":"  + pat)}
```
(Empty username, PAT as password — standard ADO PAT auth)

### `CreateWorkItemBatchAsync` implementation

The scaffold already has the right structure. Fill in the TODO blocks:

**Step 1 — Get caller's PAT**
```csharp
var pat = await _credentialService.GetDecryptedPatAsync(artifactSet.CreatedBy)
    ?? throw new InvalidOperationException($"No ADO credential found for {artifactSet.CreatedBy}");
var project = artifactSet.AdoProjectName;  // set at decomp time from user selection
var org = _config["Nexus:Ado:Organization"] ?? "FortressAffinityGroup";
```

**Step 2 — Create each WI**
```
POST https://dev.azure.com/{org}/{project}/_apis/wit/workitems/${type}?api-version=7.1
Content-Type: application/json-patch+json

[
  { "op": "add", "path": "/fields/System.Title", "value": "..." },
  { "op": "add", "path": "/fields/System.Description", "value": "..." },
  { "op": "add", "path": "/fields/Microsoft.VSTS.Common.AcceptanceCriteria", "value": "..." },
  { "op": "add", "path": "/fields/System.Tags", "value": "tag1; tag2" },
  // Story Points (User Story only):
  { "op": "add", "path": "/fields/Microsoft.VSTS.Scheduling.StoryPoints", "value": 3 },
  // Priority:
  { "op": "add", "path": "/fields/Microsoft.VSTS.Common.Priority", "value": 2 },
  // Activity (Task only):
  { "op": "add", "path": "/fields/Microsoft.VSTS.Common.Activity", "value": "Development" }
]
```

Work item type URL encoding: `User Story` → `User%20Story`, `Test Case` → `Test%20Case`

**Step 3 — Parent linking (immediately after create)**

For WIs that have a `ParentTitle`, look up the parent's ADO ID from `titleToAdoId` map and add:
```
PATCH https://dev.azure.com/{org}/{project}/_apis/wit/workitems/{childId}?api-version=7.1
Content-Type: application/json-patch+json

[{
  "op": "add",
  "path": "/relations/-",
  "value": {
    "rel": "System.LinkTypes.Hierarchy-Reverse",
    "url": "https://dev.azure.com/{org}/_apis/wit/workitems/{parentAdoId}"
  }
}]
```

**Step 4 — Predecessor linking**

For `PredecessorTitles`, after the WI is created:
```
[{
  "op": "add",
  "path": "/relations/-",
  "value": {
    "rel": "System.LinkTypes.Dependency-Reverse",
    "url": "https://dev.azure.com/{org}/_apis/wit/workitems/{predecessorAdoId}"
  }
}]
```
If a predecessor title can't be resolved: call `AddCommentAsync` on the created WI.

**Step 5 — "Tested By" linking (Test Cases)**

For `TestedByTitles` on a User Story, after all WIs are created:
```
[{
  "op": "add",
  "path": "/relations/-",
  "value": {
    "rel": "Microsoft.VSTS.Common.TestedBy-Forward",
    "url": "https://dev.azure.com/{org}/_apis/wit/workitems/{tcAdoId}"
  }
}]
```

**`AddCommentAsync`:**
```
POST https://dev.azure.com/{org}/{project}/_apis/wit/workitems/{id}/comments?api-version=7.1-preview.3
Content-Type: application/json

{ "text": "..." }
```

**Constructor additions needed:**
```csharp
private readonly IAdoCredentialService _credentialService;
private readonly IConfiguration _config;
private readonly IHttpClientFactory _httpClientFactory;
// Register HttpClient in Program.cs: builder.Services.AddHttpClient();
```

### `GetProcessTemplatesAsync` and `GetProjectsAsync`
Delegate to `IAdoCredentialService.GetProjectsAsync` — pass caller UPN (extract from `IHttpContextAccessor` or pass as parameter).

For `GetProcessTemplatesAsync`:
```
GET https://dev.azure.com/{org}/_apis/process/processes?api-version=7.1
```

---

## 5. Project Selector UI: `AdoProjectSelector.razor`

New component at `Components/Shared/AdoProjectSelector.razor`.

This is an inline component embedded in `SubmissionDetail.razor` at the "Decompose" button section. It handles:

1. **PAT check** — on init, call `IAdoCredentialService.HasCredentialAsync(userUpn)`
2. **If no PAT** — show a PAT entry form:
   - Password input for PAT
   - "Save & Validate" button — calls `ValidatePatAsync`, shows ✅ or ❌
   - On success: save and load projects
3. **If PAT exists** — show hint (`••••{hint}`), "Change PAT" link, and load project list
4. **Project dropdown** — `MudSelect` populated from `GetProjectsAsync`
5. **"Decompose" button** — disabled until project is selected; calls parent callback with selected project name

Parameters:
```csharp
[Parameter] public string UserUpn { get; set; } = "";
[Parameter] public bool IsAdmin { get; set; }
[Parameter] public EventCallback<string> OnProjectSelected { get; set; }  // fires with project name when ready
[Parameter] public EventCallback<string> OnDecomposeClicked { get; set; } // fires with project name on button click
```

---

## 6. Wire into `SubmissionDetail.razor`

The existing "Decompose" button in `SubmissionDetail.razor` is gated on `_isAdmin && submission.Status == SubmissionStatus.Approved`. Replace that button with the `<AdoProjectSelector>` component (shown only to admins on approved submissions).

When `OnDecomposeClicked` fires:
1. Set `artifactSet.AdoProjectName` to the selected project before calling `DecomposeAndPersistAsync`
2. The existing `HandleDecomposeAsync` method passes `submissionId`, `specDocumentId`, `callerUpn` — update `ArtifactGenerationService.DecomposeAndPersistAsync` signature to also accept `projectName` and store it on `ArtifactSet`

Update `ArtifactGenerationService.DecomposeAndPersistAsync`:
```csharp
public async Task<ArtifactSet> DecomposeAndPersistAsync(int submissionId, int specDocumentId, string callerUpn, string adoProjectName)
```
Set `artifactSet.AdoProjectName = adoProjectName` (was hardcoded to `"Fortress"`).

---

## 7. `NexusArtifacts.razor` — "Post to ADO" flow

The existing "Post to ADO" button already calls `IAdoService.CreateWorkItemBatchAsync`. That call now goes to `AdoCreationService`. No UI changes needed here — the existing dialog/confirmation flow is fine.

The only change: if `AdoCreationService` throws (e.g. PAT expired, 401), catch it and show a `MudSnackbar` error: "ADO posting failed: {ex.Message}. Check your PAT in Account Settings."

---

## 8. Program.cs changes

```csharp
// Replace:
builder.Services.AddScoped<IAdoService, StubAdoService>();
// With:
builder.Services.AddScoped<IAdoService, AdoCreationService>();
builder.Services.AddScoped<IAdoCredentialService, AdoCredentialService>();
builder.Services.AddHttpClient();  // for IHttpClientFactory

// Add to appsettings.Production.json (and appsettings.json for local dev):
// "Nexus": { "Ado": { "Organization": "FortressAffinityGroup" } }
```

---

## 9. NexusDbContext changes

Add to `NexusDbContext.cs`:
```csharp
public DbSet<UserAdoCredential> UserAdoCredentials { get; set; }
```

Configure in `OnModelCreating`:
```csharp
modelBuilder.Entity<UserAdoCredential>(entity => {
    entity.ToTable("user_ado_credentials");
    entity.HasKey(e => e.Id);
    entity.Property(e => e.UserUpn).HasColumnName("user_upn").HasMaxLength(200).IsRequired();
    entity.Property(e => e.EncryptedPat).HasColumnName("encrypted_pat").HasColumnType("TEXT").IsRequired();
    entity.Property(e => e.PatHint).HasColumnName("pat_hint").HasMaxLength(10);
    entity.Property(e => e.CreatedAt).HasColumnName("created_at");
    entity.Property(e => e.UpdatedAt).HasColumnName("updated_at");
    entity.HasIndex(e => e.UserUpn).IsUnique();
});
```

---

## 10. Build & Deploy

```bash
cd /home/fredw/projects/fip/nexus

# Build
docker build -t nexus-web:live-ado . -f Dockerfile

# Tag & push
docker tag nexus-web:live-ado 742932328420.dkr.ecr.us-east-1.amazonaws.com/nexus-web:live-ado
docker tag nexus-web:live-ado 742932328420.dkr.ecr.us-east-1.amazonaws.com/nexus-web:latest
aws ecr get-login-password --region us-east-1 | docker login --username AWS --password-stdin 742932328420.dkr.ecr.us-east-1.amazonaws.com
docker push 742932328420.dkr.ecr.us-east-1.amazonaws.com/nexus-web:live-ado
docker push 742932328420.dkr.ecr.us-east-1.amazonaws.com/nexus-web:latest

# Register new task def and force-redeploy
# Check if script exists:
ls scripts/ecs-register-task-def.sh 2>/dev/null && bash scripts/ecs-register-task-def.sh || {
  # Manual: get current task def, update image, register, force deploy
  CURRENT_TD=$(aws ecs describe-services --cluster fortress-tools-cluster --services nexus-web --query 'services[0].taskDefinition' --output text)
  aws ecs describe-task-definition --task-definition $CURRENT_TD --query 'taskDefinition' > /tmp/td.json
  # Update image in td.json then register
  aws ecs register-task-definition --cli-input-json file:///tmp/td.json
  NEW_TD=$(aws ecs describe-task-definitions --family-prefix nexus-web --query 'taskDefinitionArns[-1]' --output text)
  aws ecs update-service --cluster fortress-tools-cluster --service nexus-web --task-definition $NEW_TD --force-new-deployment
}
```

After deploy, verify:
```bash
curl -s https://nexus.fortressam.ai/health  # should return "OK"
```

---

## 11. Acceptance Criteria

- [ ] Admin user can enter and save an ADO PAT via the project selector UI
- [ ] PAT is validated against ADO before saving (401 → error shown, not saved)
- [ ] Project dropdown shows only projects accessible with the saved PAT
- [ ] Decompose button is disabled until a project is selected
- [ ] Decomposition stores the selected project name on `ArtifactSet.AdoProjectName`
- [ ] "Post to ADO" creates real work items in the selected ADO project
- [ ] Parent hierarchy links are set (Epics → Features → Stories → Tasks)
- [ ] Predecessor links are set where `PredecessorTitles` resolves
- [ ] Unresolvable predecessors get an ADO comment
- [ ] Error handling: expired/invalid PAT shows snackbar, does not crash
- [ ] Health endpoint still returns 200 after deploy

---

## Notes

- `AdoCreationService` already exists at `Services/AdoCreationService.cs` with the right structure — fill in the TODO blocks, add constructor params
- `StubAdoService` can remain in the codebase but must not be registered in Program.cs
- Use `IHttpClientFactory` (not `new HttpClient()`) for all ADO HTTP calls
- The `ArtifactSet.AdoProjectName` field already exists in the DB schema — no migration needed for it
- `ArtifactSet.AdoProjectId` also exists — optionally populate with the ADO project GUID if returned by the projects API
- Do not change the existing decomposition logic in `ArtifactGenerationService` other than the `adoProjectName` parameter addition
