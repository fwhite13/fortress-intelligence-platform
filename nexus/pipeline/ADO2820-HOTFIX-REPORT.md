# ADO#2820 — Hotfix Report: MakeAdoWorkItemFieldsNullable Migration

**Date:** 2026-05-06  
**Commit:** `bcd3208`  
**Branch:** main  
**Status:** SHIPPED ✅

---

## Problem

Migration `20260506143015_MakeAdoWorkItemFieldsNullable.cs` accidentally included:

- `AddColumn<string>` for `acceptance_criteria` in `Up()`
- `DropColumn` for `acceptance_criteria` in `Down()`

This column was already added by the prior migration `20260506000001_AddAcceptanceCriteriaToWorkItemRecord`. On every ECS task startup, EF Core attempted to add the column a second time, resulting in:

```
Duplicate column name 'acceptance_criteria'
```

The app continued running (health check passed) but logged errors on every container replacement.

---

## Fix

**File edited:** `src/FortressNexus.Web/Migrations/20260506143015_MakeAdoWorkItemFieldsNullable.cs`

**Removed from `Up()`:**
```csharp
migrationBuilder.AddColumn<string>(
    name: "acceptance_criteria",
    table: "work_item_records",
    type: "text",
    nullable: true)
    .Annotation("MySql:CharSet", "utf8mb4");
```

**Removed from `Down()`:**
```csharp
migrationBuilder.DropColumn(
    name: "acceptance_criteria",
    table: "work_item_records");
```

The migration now only contains the two `AlterColumn` operations it was originally intended to perform:
- `ado_work_item_url` → nullable
- `ado_work_item_id` → nullable

---

## Verification

**Build:** `dotnet build` → 0 errors, 1 pre-existing warning (unrelated null assignment in FileStorageService.cs)

**Migration script check:** `dotnet ef migrations script --idempotent --context NexusDbContext`
- `acceptance_criteria` ADD appears under `20260506000001_AddAcceptanceCriteriaToWorkItemRecord` only ✅
- `20260506143015_MakeAdoWorkItemFieldsNullable` contains only the two AlterColumn operations ✅

---

## Impact

- No schema changes — `acceptance_criteria` column is unchanged in the database
- ECS task replacements will no longer log the duplicate column error
- Designer.cs snapshot was not modified (it correctly reflected `acceptance_criteria` from the prior migration already)

---

## Notes

The Designer.cs was intentionally left untouched. The snapshot inside it already correctly includes `acceptance_criteria` as established by `20260506000001`. Only the migration `.cs` file's `Up()`/`Down()` operations were modified.
