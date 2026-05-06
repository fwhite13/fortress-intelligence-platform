# Review Report — ADO#2820 (Cycle 2)

**Commit:** `c933e3b`
**Verdict:** NEEDS-CHANGES

---

## Spec Compliance Check

This is a targeted cycle 2 re-review. Both cycle 1 issues were addressed. One fix is incomplete.

---

## Consistency Audit

**Files Cross-Referenced:**
- `WorkItemRecord.cs` ↔ `NexusDbContext.cs` — ✅ nullable types match config
- `NexusDbContext.cs` ↔ `20260506143015_MakeAdoWorkItemFieldsNullable.cs` — ⚠️ see C1
- `ArtifactGenerationService.cs` ↔ `NexusArtifacts.razor` write-back — ✅ type-compatible
- `StubAdoService.cs` ↔ `WorkItemRecord.cs` — ✅ `GetValueOrDefault()` handles `int?` correctly
- `NexusDbContextModelSnapshot.cs` — ✅ reflects nullable int for `AdoWorkItemId`

---

## Critical Issues — 1

### C1: `Down()` missing `UpdateData` for `ado_work_item_id` before NOT NULL constraint restore

- **File:** `Migrations/20260506143015_MakeAdoWorkItemFieldsNullable.cs`
- **Location:** `Down()` method, line ~68
- **Category:** Correctness (migration data integrity)
- **Issue:** The `Down()` method correctly handles `ado_work_item_url` — it runs `UpdateData` to replace NULL → `""` before `AlterColumn` restores the NOT NULL constraint. But `ado_work_item_id` goes straight to `AlterColumn<int>(..., nullable: false, defaultValue: 0)` with no prior `UpdateData`. On MySQL with `STRICT_TRANS_TABLES` (standard for this stack), any `work_item_records` row with a NULL `ado_work_item_id` (i.e., Pending items not yet posted to ADO — the entire reason for this nullable fix) will cause the rollback to throw. The `defaultValue: 0` only sets the column's default for future inserts; it does not back-fill existing NULLs.

**Evidence:**
```csharp
// ✅ URL: correctly back-fills nulls before AlterColumn
migrationBuilder.UpdateData(
    table: "work_item_records",
    keyColumn: "ado_work_item_url",
    keyValue: null,
    column: "ado_work_item_url",
    value: "");

migrationBuilder.AlterColumn<string>(
    name: "ado_work_item_url",
    ...
    nullable: false,
    ...);

// ❌ ID: no UpdateData — goes straight to AlterColumn
migrationBuilder.AlterColumn<int>(
    name: "ado_work_item_id",
    ...
    nullable: false,
    defaultValue: 0,   // ← does NOT back-fill existing NULLs
    ...);
```

**Impact:** `dotnet ef database update <previous-migration>` will fail on any database that has Pending WorkItemRecords (i.e., any environment where this feature has been exercised). Rollback is completely broken for the primary use case this migration enables.

**Fix:**
```diff
+ migrationBuilder.UpdateData(
+     table: "work_item_records",
+     keyColumn: "ado_work_item_id",
+     keyValue: null,
+     column: "ado_work_item_id",
+     value: 0);
+
  migrationBuilder.AlterColumn<int>(
      name: "ado_work_item_id",
      table: "work_item_records",
      type: "int",
      nullable: false,
      defaultValue: 0,
      ...);
```

Insert the `UpdateData` call in `Down()` immediately before the `AlterColumn` for `ado_work_item_id`.

---

## Passing Criteria — 7/8

| # | Criterion | Result |
|---|-----------|--------|
| 1 | `WorkItemRecord.AdoWorkItemId` is `int?` | ✅ PASS |
| 2 | `WorkItemRecord.AdoWorkItemUrl` is `string?` | ✅ PASS |
| 3 | `.IsRequired()` removed from both in `NexusDbContext` | ✅ PASS |
| 4 | Migration `Up()` sets both columns nullable | ✅ PASS |
| 4b | Migration `Down()` fully restores both columns | ❌ **FAIL** (C1) |
| 5 | No `AdoWorkItemId > 0` guards remain anywhere | ✅ PASS |
| 6 | `StubAdoService.GetValueOrDefault()` fix is sound | ✅ PASS |
| 7 | No `AdoWorkItemId = 0` / `AdoWorkItemUrl = ""` in `DecomposeAndPersistAsync` mapping | ✅ PASS |
| 8 | Empty-DTO guard throws BEFORE `ArtifactSet` creation | ✅ PASS |

**Spot-check: NexusArtifacts.razor write-back path**
`record.AdoWorkItemId = result.AdoWorkItemId` — `int? = int?` ✅
`record.AdoWorkItemUrl = result.AdoWorkItemUrl` — `string? = string?` ✅
`!string.IsNullOrEmpty(result.AdoWorkItemUrl)` null guard on render — ✅

---

## What to Fix

**One change only:**

In `Migrations/20260506143015_MakeAdoWorkItemFieldsNullable.cs`, `Down()` method — add an `UpdateData` call to back-fill NULL `ado_work_item_id` values to `0` before the `AlterColumn` restores the NOT NULL constraint. Pattern is already present for `ado_work_item_url` — apply the same pattern to `ado_work_item_id`.

---

_Reviewed by Hawkeye — 2026-05-06_
