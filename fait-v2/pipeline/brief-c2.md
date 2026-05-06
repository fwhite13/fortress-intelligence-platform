# CC Brief — ADO#2843 Build Cycle 2 (I1 only)

## Task
Add `created_at` and `updated_at` columns to the `user_sessions` table to match the timestamp convention on all other tables.

**Scope:** I1 only. Do NOT touch CHAR vs varchar (I2 pending design call). Do NOT touch any other files.

---

## File 1: `Data/Models/UserSession.cs`

Full path: `/home/fredw/projects/fip/fait-v2/src/FortressAI.V2.Web/Data/Models/UserSession.cs`

Add two properties after the `UserAgent` property and before the Navigation comment:

```csharp
[Column("created_at")]
public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

[Column("updated_at")]
public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
```

---

## File 2: `Data/FaitV2DbContext.cs`

Full path: `/home/fredw/projects/fip/fait-v2/src/FortressAI.V2.Web/Data/FaitV2DbContext.cs`

In the `user_sessions` entity config (inside the `modelBuilder.Entity<UserSession>` block), add after the `UserAgent` property config and before the `HasIndex` lines:

```csharp
entity.Property(e => e.CreatedAt).HasColumnName("created_at").HasColumnType("datetime(6)");
entity.Property(e => e.UpdatedAt).HasColumnName("updated_at").HasColumnType("datetime(6)");
```

---

## After editing files, run this migration command:

```bash
cd /home/fredw/projects/fip/fait-v2/src/FortressAI.V2.Web && dotnet ef migrations add AddUserSessionTimestamps --output-dir Data/Migrations
```

After running the migration, verify the generated migration file's `Up()` method contains `AddColumn` calls for `created_at` and `updated_at` on the `user_sessions` table.

---

## Constraints
- Do NOT modify any other files
- Do NOT change any existing `.HasColumnType()` calls on PKs/FKs
- Do NOT rename any existing columns
- Do NOT touch any other entity configurations
- The migration must be additive only (no dropping/modifying existing columns)
