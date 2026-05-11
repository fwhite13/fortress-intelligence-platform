# Review Report — ADO#3236

### Verdict: NEEDS-CHANGES

**Cycle:** 1 of 2  
**Reviewer:** Hawkeye (Clint Barton)  
**Commit:** `92db9340`

---

### CC Review Summary

CC ran against all six files plus the DashboardHub and AppDbContextModelSnapshot. CC found three Criticals and four Importants. I agree with CC's Critical #1 (schema/OnModelCreating mismatch), Critical #2 (HttpClient auth), and Critical #3 (SignalR group not joined). Critical #1 is the most severe — every DB operation on `FeedbackSubmission` will fail at runtime. The Criticals compound: Critical #2 means submission never reaches the endpoint, so even if #1 and #3 were fixed, the feature still wouldn't work.

I downgraded CC's framing of Critical #3 slightly — Tony explicitly flagged the hub group join as a "v1 known limitation." That context matters. But the framing in the Build Report is misleading: Tony says the callback "only reaches users on Dashboard/Tasks pages" — which implies *those* users get it. That's also wrong. The *callback* hits `DashboardHub` via `IHubContext<DashboardHub>`, so users on Dashboard/Tasks who have joined via their own connection DO receive it. The FeedbackModal's own hub connection is the dead one. Net result: if you're on Dashboard and open the modal, you'd get the notification. If you're only in Chat, you wouldn't. This is a real gap but not a showstopper for v1 if Tony acknowledges it — I'm leaving it as Important, not Critical, for the re-read.

CC's Important findings (#4–#8) are all confirmed. The hardcoded production domain is a real violation. The internal token in plaintext to Jarvis is a security concern worth flagging.

**False positives dismissed:** None. All CC findings check out.

---

### Spec Compliance Check

No formal developer brief spec was provided for this WI (it's a port from fait-v2, not a new spec). Reviewed against the Build Report's stated deliverables.

**Stated deliverables vs. actuals:**
- ✅ `feedback_submissions` table + migration created
- ✅ `FeedbackSubmission.cs` model created
- ✅ `FeedbackModal.razor` component created
- ✅ "Report a Bug" button in chat header (top-right via `justify-content: space-between`)
- ✅ `POST /api/feedback` endpoint with `.RequireAuthorization()`
- ✅ `POST /api/feedback/{id}/status` internal callback endpoint
- ✅ `FeedbackDispatcher` helper created
- ❌ Feature is non-functional end-to-end (Criticals #1 and #2)

**Spec compliance verdict:** ❌ NON-COMPLIANT — feature is built but broken

---

### Consistency Audit

**Files cross-referenced:**
- `FeedbackSubmission.cs` ↔ `AppDbContext.OnModelCreating` — ❌ **OnModelCreating has NO FeedbackSubmission block** (see Critical C1)
- `AppDbContextModelSnapshot.cs` FeedbackSubmission section ↔ migration ↔ `AppDbContext.cs` — ❌ **Snapshot has HasColumnName mappings with no backing code**
- `FeedbackDispatcher` callback URL ↔ `config["FIP:FaitBaseUrl"]` pattern — ❌ Hardcoded production domain (see Important I2)
- `hub.Clients.Group("user-{submission.UserId}")` ↔ `DashboardHub.JoinUserGroup` callers — ❌ FeedbackModal never joins group (see Important I3)
- `config["Feedback:InternalToken"]` null check in callback endpoint — ✅ Correctly rejects if not configured
- Migration column names ↔ snapshot column names — ✅ Match (but snapshot is ahead of AppDbContext.cs code)
- `POST /api/feedback/.RequireAuthorization()` ↔ `FeedbackModal` loopback HttpClient — ❌ Auth mismatch (see Critical C2)
- `FindAsync(new object[] { id }, ct)` — ✅ Valid EF Core 8 overload confirmed
- CSS in ChatView header and FeedbackModal — ✅ All CSS variables with fallbacks, no hardcoded values
- Header button placement (`chat-header-actions` with `flex-shrink: 0`, parent has `justify-content: space-between`) — ✅ Button IS in top-right position
- No new raw MySQL connections in this feature — ✅ GuidFormat rule not triggered

---

### Issues Found

| Severity | File | Line | Issue | Fix |
|----------|------|------|-------|-----|
| **Critical** | `AppDbContext.cs` | 48 | `OnModelCreating` has no `FeedbackSubmission` entity block — EF will use PascalCase column names but DB has snake_case | Add `modelBuilder.Entity<FeedbackSubmission>()` block with `HasColumnName` for all properties (see fix below) |
| **Critical** | `FeedbackModal.razor` | 116–127 | `IHttpClientFactory.CreateClient()` in Blazor Server has no auth credentials — `POST /api/feedback` requires auth, loopback call always 401 | Remove HTTP call; inject `IDbContextFactory<AppDbContext>` + `FeedbackDispatcher` directly into component |
| **Important** | `FeedbackModal.razor` | 72–88 | Connects to DashboardHub but never calls `JoinUserGroup(userId)` — `ReceiveFeedbackResult` never reaches FeedbackModal | Either add `JoinUserGroup` call (needs `UserSession` injected for userId), or document as v1 limitation and close the hub connection (it's wasted overhead) |
| **Important** | `Program.cs` | 726–727 | `Feedback:InternalToken` sent in plaintext in the Jarvis webhook message | Don't embed the token in the message; Jarvis should receive it via a per-submission signed value or the token should not be in the message body at all |
| **Important** | `Program.cs` | 726 | Callback URL hardcoded as `https://fait.fortressam.ai/api/feedback/...` — breaks dev/staging | Use `config["FIP:FaitBaseUrl"]` or equivalent, constructed at dispatch time |
| **Important** | `Program.cs` | 693, 698, 741 | `FeedbackDispatcher` is a `static class` — can't inject `ILogger`; errors go to `Console.Error` only | Convert to a non-static service registered with DI so `ILogger` can be injected |
| **Important** | Snapshot:519–525 | — | `Status` property marked `ValueGeneratedOnAdd()` in snapshot — EF ignores any non-default value set in C# on insert | Remove `ValueGeneratedOnAdd()` from the `Status` property in `OnModelCreating`; use only `HasDefaultValue("pending")` |
| **Important** | `Program.cs` | 734 | `new HttpClient()` in `FeedbackDispatcher` bypasses connection pooling | Use a named `IHttpClientFactory` client |
| Nitpick | `FeedbackModal.razor` | 138 | Bare `catch` swallows exceptions without logging | `catch (Exception ex)` + log it |

---

### Spec Fidelity

The AC from the Build Report are structurally met — the right files exist, the right endpoints exist. The feature is architecturally sound. But two implementation bugs make it non-functional in production:

1. **DB operations will throw** because EF can't find the snake_case columns without `OnModelCreating` config
2. **Submissions never reach the server** because the Blazor component's loopback HttpClient has no auth

These aren't edge cases — they're the core happy path. Feedback submission = 401. Even if you bypass that somehow, DB write = MySqlException.

---

### What to Fix (NEEDS-CHANGES)

Tony, here's what's broken and exactly how to fix it:

#### Fix C1 — Add FeedbackSubmission to OnModelCreating

In `AppDbContext.cs`, add this block inside `OnModelCreating` (before the closing `}}`):

```csharp
modelBuilder.Entity<FeedbackSubmission>(entity =>
{
    entity.ToTable("feedback_submissions");
    entity.HasKey(e => e.Id);
    entity.Property(e => e.Id).HasMaxLength(36).HasColumnType("varchar(36)").HasColumnName("id");
    entity.Property(e => e.UserId).HasMaxLength(255).HasColumnType("varchar(255)").HasColumnName("user_id").IsRequired();
    entity.Property(e => e.Type).HasMaxLength(50).HasColumnType("varchar(50)").HasColumnName("type").IsRequired();
    entity.Property(e => e.Description).HasColumnType("longtext").HasColumnName("description").IsRequired();
    entity.Property(e => e.PageUrl).HasMaxLength(500).HasColumnType("varchar(500)").HasColumnName("page_url");
    entity.Property(e => e.ScreenshotS3Key).HasMaxLength(500).HasColumnType("varchar(500)").HasColumnName("screenshot_s3_key");
    entity.Property(e => e.Status).HasMaxLength(50).HasColumnType("varchar(50)").HasColumnName("status").HasDefaultValue("pending").IsRequired();
    entity.Property(e => e.AdoWiId).HasColumnType("int").HasColumnName("ado_wi_id");
    entity.Property(e => e.TriageResult).HasColumnType("longtext").HasColumnName("triage_result");
    entity.Property(e => e.CreatedAt).HasColumnType("DATETIME(6)").HasColumnName("created_at").HasDefaultValueSql("CURRENT_TIMESTAMP(6)");
    entity.Property(e => e.TriagedAt).HasColumnType("DATETIME(6)").HasColumnName("triaged_at");
    entity.HasIndex(e => e.UserId).HasDatabaseName("idx_feedback_user_id");
    entity.HasIndex(e => e.Status).HasDatabaseName("idx_feedback_status");
});
```

Note: Do NOT use `ValueGeneratedOnAdd()` on Status. Just `HasDefaultValue("pending")` is correct.

#### Fix C2 — Remove HttpClient, use direct service injection

The `FeedbackModal` should not make a loopback HTTP call. Replace the Submit method:

```razor
@inject IDbContextFactory<AppDbContext> DbFactory
@inject IConfiguration Configuration

@code {
    // In Submit():
    var http = HttpClientFactory.CreateClient();  // DELETE this
    
    // Replace with:
    await using var db = await DbFactory.CreateDbContextAsync();
    var submission = new FeedbackSubmission
    {
        UserId = /* inject UserSession and use Session.UserId.ToString() */,
        Type = _type,
        Description = _description,
        PageUrl = _pageUrl,
        Status = "pending",
    };
    db.FeedbackSubmissions.Add(submission);
    await db.SaveChangesAsync();
    _ = FeedbackDispatcher.DispatchToJarvisAsync(submission, Configuration);
    _isOpen = false;
    Snackbar.Add("Feedback submitted! Jarvis is reviewing it now.", Severity.Success);
```

You'll need `@inject UserSession Session` (or however userId is obtained in this component's context).

#### Fix I2 (Important) — Callback URL from config

```csharp
// In FeedbackDispatcher.DispatchToJarvisAsync:
var baseUrl = config["FIP:FaitBaseUrl"]?.TrimEnd('/') ?? "https://fait.fortressam.ai";
// Then in the message:
- After triage, call back: POST {{baseUrl}}/api/feedback/{{submission.Id}}/status
```

#### Fix I3 (Important, v1 framing) — Either join the group or don't connect

If v1 doesn't support real-time notification in Chat, **remove the hub connection entirely** from FeedbackModal. It's wasted overhead that connects but never receives anything. Document in code: "v1: no real-time notification in Chat. Callback will reach Dashboard/Tasks users only."

If you want to fix it properly: inject `UserSession`, call `JoinUserGroup(Session.UserId.ToString())` after `_hubConnection.StartAsync()` succeeds.

---

### Notes on EF Schema — CRITICAL (don't skip this)

The snapshot already has the correct `HasColumnName` mappings — which means at some point the `OnModelCreating` block existed. It was dropped somewhere between when the migration was generated and the final commit. The migration is correct. The DB schema will be correct after migration runs. **Only the runtime EF model is wrong**, and only at query/insert time.

You can verify the fix worked by running `dotnet ef migrations add TestVerify --no-build` (then delete the migration immediately). If it produces an empty migration, the model matches the schema. If it produces column renames, the `HasColumnName` mappings aren't matching.

---

_Hawkeye out. Three Criticals and four Importants — send it back when fixed._
