# CC Brief — ADO#1352 Cycle 2 Fixes

You are fixing review findings in the FIRM Blazor app (ASP.NET 8). All changes are surgical. Build must be 0 errors, 0 warnings introduced.

Working directory: `/home/fredw/projects/fip`

---

## Fix I1 — FipDbContext null guard in FIRM Program.cs

**File:** `firm/src/FortressIntelligenceRM.Web/Program.cs`

Around line 56, inside the FipDbContext `MySqlConnectionStringBuilder` block, change:

```csharp
Server = dbHost,
```

To:

```csharp
Server = dbHost ?? "localhost",
```

This is the only change in this file. Do not touch the FirmDbContext block above it (which already has a null guard).

---

## Fix N1 — Remove dead `_calendarPendingMsg` field in Meetings.razor

**File:** `firm/src/FortressIntelligenceRM.Web/Components/Pages/Meetings.razor`

The field `_calendarPendingMsg` (private string?) is declared at line ~178 but never assigned. The `else if` block at line ~79 renders based on it but can never show anything.

Remove BOTH:
1. The field declaration: `private string? _calendarPendingMsg;`
2. The `else if (!_calendarLoading && !string.IsNullOrEmpty(_calendarPendingMsg))` block and its content (the entire else-if branch, including the MudText/HTML inside it)

---

## Fix N2 — Remove unused `using` directives in CalendarService.cs

**File:** `firm/src/FortressIntelligenceRM.Web/Services/CalendarService.cs`

Lines 3-4 have unused `using` statements that survived the refactor:
```csharp
using Microsoft.EntityFrameworkCore;
using FortressIntelligenceRM.Web.Data;
```

Remove both. The file already has `using System.Text.Json;`, `using System.Text.RegularExpressions;`, and the other necessary usings. These two are dead.

---

## Fix N3 — Remove legacy UserMicrosoftToken from FirmDbContext + delete model file

### Step 1: Edit FirmDbContext.cs

**File:** `firm/src/FortressIntelligenceRM.Web/Data/FirmDbContext.cs`

Remove the DbSet property:
```csharp
public DbSet<UserMicrosoftToken> UserMicrosoftTokens => Set<UserMicrosoftToken>();
```

Remove the entire `modelBuilder.Entity<UserMicrosoftToken>` block from `OnModelCreating`. It looks like:
```csharp
modelBuilder.Entity<UserMicrosoftToken>(entity =>
{
    entity.ToTable("user_microsoft_tokens");
    // ... all config lines inside
    entity.HasOne(e => e.User).WithOne().HasForeignKey<UserMicrosoftToken>(e => e.UserId)
        .HasConstraintName("fk_user_microsoft_tokens_user_id");
});
```
Remove it entirely.

Also remove the `using` for `UserMicrosoftToken` model if there is a specific using for its namespace that becomes unused after removing the DbSet. Check the top of the file for any using statements that only exist to support `UserMicrosoftToken`.

### Step 2: Delete Models/UserMicrosoftToken.cs

**File to delete:** `firm/src/FortressIntelligenceRM.Web/Models/UserMicrosoftToken.cs`

Before deleting, confirm no other .cs or .razor file references `UserMicrosoftToken` besides `FirmDbContext.cs` (which we're already cleaning). You can grep:
```
grep -rn "UserMicrosoftToken" firm/src/ --include="*.cs" --include="*.razor"
```

If the only reference is in `FirmDbContext.cs` (which we're cleaning in Step 1), then delete the file.

---

## Build Verification

After all changes, run:
```bash
cd /home/fredw/projects/fip
dotnet build firm/src/FortressIntelligenceRM.Web/FortressIntelligenceRM.Web.csproj 2>&1 | tail -5
```

The build must show `Build succeeded` with 0 errors and 0 warnings introduced by these changes.

If build fails, fix any issues before completing.

---

## Output

Print a summary of every change made (file, what was changed). Print the final build result line.
