# Review Report — ADO#3137

### Verdict: NEEDS-CHANGES

---

### Spec Compliance Check

**§2 Codebase Map:**
- `src/FortressAI.Web/Components/Pages/Settings.razor` — ✅ Full rewrite present
- `src/FortressAI.Web/Components/Layout/SidebarContent.razor` — ✅ `/assistant-settings` nav entry removed
- `src/FortressAI.Web/Components/Pages/AssistantSettings.razor` — ✅ Deleted (confirmed absent)

**§7 Acceptance Criteria:**
- [x] 4 MudTabPanel elements (Assistant, Integrations, Briefing, Meeting Intelligence) — ✅ Present
- [x] `_activeTab = 0` (default to Assistant) — ✅ Confirmed
- [x] All cards under correct tabs (Profile→Assistant, MCP/M365/DevOps→Integrations, Briefing→Briefing, FIRM→Meeting Intelligence) — ✅ Confirmed
- [x] AssistantSettings.razor deleted — ✅ Confirmed
- [x] Sidebar nav entry removed — ✅ Confirmed
- [x] All 6 assistant fields present (PreferredName, Role, Responsibilities, CommunicationStyle, ResponseFormat, ShowCitations) — ✅ Confirmed
- [x] CommunicationStyle/ResponseFormat pre-populated with `.ToLowerInvariant()` with null guard — ✅ Confirmed (`!string.IsNullOrWhiteSpace` guards prevent NullReferenceException)
- [x] ShowCitations — MudSwitch, pre-populated — ✅ Confirmed
- [x] File accept: jpg/jpeg/png, 5MB check — ✅ Extension check at lines 692-693, size check at line 691
- [x] S3 key format with trailing-slash guard — ✅ Lines 701-703 correct; uses `Guid.NewGuid()` instead of raw filename (actually safer than spec)
- [x] Old avatar deleted before upload — ✅ Lines 706-713 (with logging on delete failure, not fatal)
- [x] `_avatarUrl` (raw S3 URL) stored in DB — ✅ Line 733
- [x] `_avatarPreviewUrl` used in `<img>` src — ✅ Confirmed
- [x] Pre-signed URL generated on page load if AvatarUrl set — ✅ Lines 481-482
- [x] Extended fields saved via direct EF write — ✅ Lines 636-651
- [x] Base fields still saved via ConfigSvc — ✅ Line 610
- [x] No new migration added beyond `20260510014154_AddAvatarUrlToUserAssistantConfig` — ✅ Confirmed
- [x] dotnet build 0 errors — ✅ Per build report

**Spec compliance verdict:** ✅ COMPLIANT on all checklist items, with exceptions noted below.

---

### Consistency Audit

**Files Cross-Referenced:**
- `Settings.razor` ↔ `AppDbContext.cs` (`UserAssistantConfig` entity config, lines 115-137) — ⚠️ **See I1 below**
- `Settings.razor` ↔ `Migrations/20260510014154_AddAvatarUrlToUserAssistantConfig.cs` — ⚠️ **See I1 below**
- `Settings.razor` ↔ `SidebarContent.razor` — ✅ Nav entry removed
- `UserAssistantConfig` entity properties vs DB columns — All new fields (`preferred_name`, `role`, `responsibilities`, `communication_style`, `response_format`, `show_citations`) have explicit `HasColumnName` in `AppDbContext.OnModelCreating`. `AvatarUrl` does **not**.

**Undocumented Dependencies Found:**
- None

---

### Issues Found

| Severity | File | Line | Issue | Fix |
|----------|------|------|-------|-----|
| Important | `AppDbContext.cs` / Migration | 27 / 115-137 | `AvatarUrl` column PascalCase in migration, no `HasColumnName` — convention break | Add `HasColumnName("avatar_url")` to OnModelCreating; generate corrective migration |
| Important | `Settings.razor` | 604-651 | SaveSettings: 4 separate commits, no transaction wrapper | Wrap in transaction or consolidate DbContexts |
| Important | `Settings.razor` | 740-744 | AccessDenied S3 exception swallowed (spec says re-throw) | Either re-throw or update spec to document intentional swallow |
| Nitpick | `Settings.razor` | 664 | `GenerateAvatarPreviewUrlAsync` declared `async` with no `await` (CS1998 warning) | Remove `async`, return `string?` or `Task.FromResult(...)` |
| Nitpick | `Settings.razor` | 125 | MudFileUpload `Accept=".jpg,.jpeg,.png"` — extension format vs spec's MIME type | Align with spec if required; code-level validation at lines 692-693 covers it |
| Nitpick | `Settings.razor` | 341-347 | Meeting Intelligence card: duplicate title in `MudCardHeader` and inner `MudText` | Remove the inner `MudText` header |

---

### Spec Fidelity — Detailed Notes

#### I1: AvatarUrl Column Convention Break (Important)

**Migration** (`20260510014154_AddAvatarUrlToUserAssistantConfig.cs`, line 27):
```csharp
migrationBuilder.AddColumn<string>(
    name: "AvatarUrl",          // ← PascalCase
    table: "user_assistant_config", ...);
```

Every other column added in the same migration uses snake_case (`communication_style`, `preferred_name`, `response_format`, `responsibilities`, `role`, `show_citations`). `AvatarUrl` is the outlier.

**AppDbContext.cs** (lines 115-137) has explicit `HasColumnName(...)` for every new field except `AvatarUrl`:
```csharp
entity.Property(e => e.Role).HasColumnName("role")...
entity.Property(e => e.CommunicationStyle).HasColumnName("communication_style")...
entity.Property(e => e.PreferredName).HasColumnName("preferred_name")...
// AvatarUrl — no HasColumnName; EF resolves as "AvatarUrl" → matches migration by accident
```

**Runtime impact on MySQL:** MySQL column names are case-insensitive by default. EF resolves to `AvatarUrl`, migration created `AvatarUrl`, query works. This is NOT currently causing a `MySqlException`.

**Risk:** If a naming convention plugin is added later (e.g., `UseSnakeCaseNamingConvention()`), EF would expect `avatar_url` but find `AvatarUrl` — causing an immediate runtime failure. The inconsistency will also generate a spurious corrective migration in any future `dotnet ef migrations add`. Fix before it becomes a real problem.

**Fix:**
```csharp
// AppDbContext.cs — add to UserAssistantConfig entity configuration block
entity.Property(e => e.AvatarUrl).HasColumnName("avatar_url").HasMaxLength(512);
```
Then run `dotnet ef migrations add RenameAvatarUrlColumn` to generate a corrective DB rename.

---

#### I2: SaveSettings Non-Atomic (Important)

Tony flagged this himself. `SaveSettings` makes 4 separate writes: ConfigSvc (base fields), ConfigSvc (briefing schedule), DbContext for display name, separate DbContext for extended fields. If the display name save (op 3) succeeds but extended fields (op 4) fail, the user's display name changes but their new CommunicationStyle/Role/etc. do not persist. The UI shows a generic error without indicating which fields were saved.

Acceptable for v1 given the low-risk nature of the data. Not blocking by itself, but combined with the column naming issue above, this goes to **NEEDS-CHANGES** overall.

**Fix options:**
1. Merge ops 3 and 4 into a single `await using` DbContext (same user table) — reduces blast radius
2. Wrap all 4 ops in a `TransactionScope` — atomic but more complex
3. Keep as-is with improved error messaging that says which save failed — minimum acceptable

---

#### I3: AccessDenied Exception Handling (Important)

Spec checklist item 16 requires: *"AccessDenied S3 exception caught separately and re-thrown."*

Code at line 740:
```csharp
catch (Amazon.S3.AmazonS3Exception ex) when (ex.ErrorCode == "AccessDenied")
{
    Logger.LogError(ex, "[Avatar] AccessDenied");
    _avatarError = "Upload failed: access denied.";
    // No throw — exception absorbed
}
```

The behavior is actually better UX than re-throwing (which would crash the circuit). But it diverges from the spec. Either:
- Add `throw;` after logging (spec-compliant but circuit-crashing)
- Update the spec to document the intentional swallow as an architectural decision

My recommendation: **update the spec** — swallowing here is the right call for Blazor Server. Escalate this as a spec correction request.

---

### What to Fix (Required for PASS)

**Tony — two changes needed:**

**1. Add `HasColumnName("avatar_url")` for AvatarUrl in AppDbContext.cs:**
```csharp
// In modelBuilder.Entity<UserAssistantConfig>(entity => ...) block
// After line 136 (PreferredName mapping)
entity.Property(e => e.AvatarUrl).HasColumnName("avatar_url").HasMaxLength(512);
```
Then generate a corrective migration:
```bash
dotnet ef migrations add RenameAvatarUrlColumn --project src/FortressAI.Web
```
This renames the DB column from `AvatarUrl` to `avatar_url`.

**2. Pick one of the following for SaveSettings atomicity (your call which level):**
- Option A (minimal): Merge the display name save and extended fields save into a single DbContext block (one `await using var db = ...`, one `SaveChangesAsync()`)
- Option B (belt-and-suspenders): Wrap both ConfigSvc calls AND EF saves in a try/catch that surfaces which specific operation failed
- Option C (accept as-is): If Fred explicitly says non-atomic saves are acceptable given the low-stakes data, I'll accept it with a code comment documenting the behavior

**3. For the `async` warning (Nitpick-level but clean it up):**
Remove `async` from `GenerateAvatarPreviewUrlAsync` and return `string?` directly — this method is synchronous.

---

### CC Review Summary

CC (Sonnet) ran full analysis on Settings.razor (752 lines) and AppDbContext.cs. Findings:
- 1 critical flagged by CC (AvatarUrl column) → downgraded to **Important** (runtime safe on MySQL but convention violation)
- 3 important confirmed → I2 (non-atomic save, Tony-flagged), I3 (AccessDenied spec divergence), CS1998 async warning
- 2 nitpicks confirmed
- All 29 checklist items verified; items 1-10, 12-15, 17-19, 21-25, 27-29 PASS

No false positives dismissed.

---

_Hawkeye_  
_ADO#3137 — 2026-05-09_
