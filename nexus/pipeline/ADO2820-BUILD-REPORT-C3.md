# Build Report — ADO#2820 — Cycle 3

**Agent:** Tony Stark (BUILD)
**Date:** 2026-05-06
**Commit:** `a6a2abb`

---

## What was built

Surgical fix to the migration `Down()` method — added a `UpdateData` null back-fill for `ado_work_item_id` (NULL → 0) immediately before the `AlterColumn` that restores it to NOT NULL. This mirrors the existing correct pattern already in place for `ado_work_item_url`.

---

## Files changed

- `src/FortressNexus.Web/Migrations/20260506143015_MakeAdoWorkItemFieldsNullable.cs`
  — Added `migrationBuilder.UpdateData(...)` call for `ado_work_item_id` in `Down()` (7 lines inserted)

---

## The fix

```csharp
// Added immediately before the AlterColumn for ado_work_item_id in Down():
migrationBuilder.UpdateData(
    table: "work_item_records",
    keyColumn: "ado_work_item_id",
    keyValue: null,
    column: "ado_work_item_id",
    value: 0);
```

---

## Why this was needed

MySQL with `STRICT_TRANS_TABLES` rejects attempts to ALTER a nullable column to NOT NULL when existing rows contain NULL values. Without this back-fill, rolling back this migration on any database with NULL `ado_work_item_id` rows would throw and leave the DB in a broken state.

---

## Build result

```
Build succeeded.
0 Error(s)
1 Warning(s)  ← pre-existing, unrelated to this change
```

---

## Acceptance criteria verification

- [x] `UpdateData` for `ado_work_item_id` added in `Down()` before `AlterColumn` — ✅
- [x] Pattern mirrors `ado_work_item_url` handling in same method — ✅
- [x] `dotnet build` → 0 errors — ✅
- [x] No other files modified — ✅

---

## Notes

No `.Designer.cs` changes required — EF snapshot files only reflect model state, not `Down()` rollback logic.

---

**Status: COMPLETE — awaiting Clint review**
