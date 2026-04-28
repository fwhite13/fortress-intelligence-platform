# Review Report — ADO#2497

**Task:** Add new fields to WorkItemRecord and ArtifactSet models
**Commit:** `f527f50`
**Review Cycle:** 1 of 2
**Reviewer:** Hawkeye (Clint Barton)
**Date:** 2026-04-28

---

### Verdict: ✅ PASS

---

## CC Review Invocation

```bash
cd /home/fredw/projects/fip/nexus && \
  cat /tmp/review-2497-brief.txt | claude --model sonnet --print --dangerously-skip-permissions
```

Brief at `/tmp/review-2497-brief.txt` — adversarial spec covering all 12 checks across Critical, Important, and Nitpick tiers.

---

## Spec Compliance Check

**Spec:** `memory/projects/nexus-decomp-upgrade-spec-2026-04-27.md` — §4 (Data Model Changes)

**§4 Codebase Map:**
| File | Status |
|------|--------|
| `Models/Entities/WorkItemRecord.cs` | ✅ Modified — 5 new fields added |
| `Models/Entities/ArtifactSet.cs` | ✅ Modified — `ExternalDependencyCount` added |
| `Data/NexusDbContext.cs` | ✅ Modified — EF config for all new fields |
| `Migrations/20260428030456_AddDecompositionUpgradeFields_20260427.cs` | ✅ Created |
| `Migrations/20260428030456_AddDecompositionUpgradeFields_20260427.Designer.cs` | ✅ Created (auto-generated) |
| `Migrations/NexusDbContextModelSnapshot.cs` | ✅ Updated |

**§9 Out of Scope:**
- ✅ No out-of-scope changes detected. Tony touched only the files specified in the brief.

**§4 Acceptance Criteria:**
- [x] `PredecessorTitles: List<string>?` — ✅ Present, nullable, JSON-serialized with ValueComparer
- [x] `IsExternalDependency: bool` default `false` — ✅ Present, default `false`
- [x] `ExternalOwner: string?` — ✅ Present, nullable, `varchar(100)`
- [x] `WiTemplate: WiTemplateType` from `Services/IWiClassifier.cs` — ✅ NOT redefined in Models; imported via `using FortressNexus.Web.Services`
- [x] `TestedByTitles: List<string>?` — ✅ Present, nullable, JSON-serialized with ValueComparer
- [x] `ExternalDependencyCount: int` default `0` — ✅ Present on `ArtifactSet`, default `0`
- [x] `WiType`/`WorkItemType` supports "Test Case" — ✅ Field is `VARCHAR(50)` string (pre-existing); "Test Case" is already storable; no enum constraint to break

**Spec compliance verdict:** ✅ COMPLIANT

---

## Consistency Audit

**Files Cross-Referenced:**

| Pair | Result |
|------|--------|
| `WorkItemRecord.cs` ↔ `NexusDbContext.cs` — property names | ✅ All 5 new properties mapped correctly |
| `NexusDbContext.cs` HasColumnName ↔ Migration column names | ✅ All 6 column names match exactly |
| `WiTemplate` C# default (`WiTemplateType.Standard`) ↔ Migration default (`"Standard"`) | ✅ Consistent — EF's `HasConversion<string>()` serializes `Standard` → `"Standard"` |
| `IWiClassifier.cs` enum values ↔ `WiClassifierService.cs` usage | ✅ Enum-only comparisons, no stray string literals |
| `ArtifactSet.ExternalDependencyCount` ↔ Migration target table | ✅ Migration correctly targets `artifact_sets`, not `work_item_records` |
| `NexusDbContextModelSnapshot.cs` ↔ DbContext config | ✅ Snapshot reflects all 6 new columns with correct types and column names |

**WiTemplateType Redefinition Check:**
- Defined in: `Services/IWiClassifier.cs` (line 13) ✅
- `WorkItemRecord.cs`: uses `using FortressNexus.Web.Services;` — no local redefinition ✅
- No other file in `Models/` defines this enum ✅

**ValueComparer Check (Critical for JSON list fields):**
- `PredecessorTitles`: `Metadata.SetValueComparer(new ValueComparer<List<string>?>(...))` ✅ — NexusDbContext.cs lines ~162–165
- `TestedByTitles`: `Metadata.SetValueComparer(new ValueComparer<List<string>?>(...))` ✅ — NexusDbContext.cs lines ~177–180
- Both use JSON round-trip equality, hash, and snapshot — correct pattern

**Raw SQL / String Comparison Audit:**
- Searched `WiClassifierService.cs` for any string comparisons against `wi_template` values
- All comparisons use enum type (`WiTemplateType.Standard`, etc.) — no raw `"standard"` or `"test-case"` string literals ✅

---

## Critical Issues: 0

None found.

---

## Important Issues: 0

None found.

---

## VARCHAR vs ENUM — Explicit Call

**The brief asked me to make an explicit call on this. Here it is:**

**PASS — VARCHAR(20) with PascalCase string conversion is acceptable for this codebase.**

Rationale:
- The spec's MySQL ENUM (`'standard','infrastructure','migration','test-case'`) provides DB-level rejection of invalid values. Tony's VARCHAR(20) does not.
- However, the existing `wi_type` / `work_item_type` field is also VARCHAR(50) with free-form string storage — Tony is following the established codebase pattern, not deviating from it.
- All access to `wi_template` goes through EF. Invalid values can only enter via a code change that sets an invalid `WiTemplateType` enum value — which the C# type system prevents.
- The PascalCase on-disk format (`"Standard"`, `"Infrastructure"`, `"Migration"`, `"TestCase"`) diverges from the spec's kebab-case ENUM (`'standard'`, `'infrastructure'`, `'migration'`, `'test-case'`). This is a tracking note, not a blocker, since no non-EF consumers currently access `wi_template`.

**Risk:** If a future developer writes raw SQL against `wi_template` expecting lowercase/kebab-case values (as the spec documents them), they will find PascalCase. This is the only residual risk.

**Recommendation (Nitpick, not blocking):** Document the PascalCase-on-disk convention in a code comment on the `WiTemplate` property in `WorkItemRecord.cs`, so future developers know to use `"Standard"` not `"standard"` if they ever write raw SQL.

---

## Nitpicks: 2

**N1 — PascalCase EF serialization vs. spec's kebab-case ENUM values**
- `WorkItemRecord.cs:24` — `WiTemplate` defaults to `WiTemplateType.Standard`, serialized as `"Standard"` (PascalCase)
- Spec §4 defines `ENUM('standard','infrastructure','migration','test-case')` (lowercase/kebab)
- Migration confirms PascalCase default: `defaultValue: "Standard"`
- Not blocking. Track if raw DB access is ever introduced.
- Suggested fix: Add code comment on `WiTemplate` property noting the on-disk format.

**N2 — No MySQL COMMENT annotations on migration columns**
- Minor. Spec §4 includes SQL COMMENT strings for `predecessor_titles`, `external_owner`, and `tested_by_titles`. These aren't reflected in the migration.
- Not blocking — EF Core has no first-class mechanism for MySQL column comments without custom annotations.
- Could be added via raw SQL in migration if desired, but not required.

---

## Positive Observations

- **ValueComparer pattern is correct** — Tony used the JSON round-trip approach for both list fields, which is the right call for EF change tracking on mutable `List<T>`. Easy to get wrong; he got it right.
- **WiTemplateType placement is clean** — Living in `Services/IWiClassifier.cs` alongside the interface that uses it, imported by `WorkItemRecord.cs`. No cross-layer type leakage.
- **Migration is complete and reversible** — `Up()` adds all 6 columns, `Down()` drops all 6. Tables are correct (5 on `work_item_records`, 1 on `artifact_sets`). Clean.
- **Column name convention** — All 6 new columns follow the existing `snake_case` convention. No drift.
- **No TODO/debug artifacts** — Code is clean.

---

## 🚀 Deploy Note for Rhodey

**Migration NOT yet applied to dev DB.** Tony had no local MySQL available.

Before (or during) deploy, run:

```bash
cd /home/fredw/projects/fip/nexus/src/FortressNexus.Web
dotnet ef database update
```

This is the only action required. Migration `AddDecompositionUpgradeFields_20260427` is ready to apply. This is not a blocker for PASS — just a deploy prerequisite.

---

## Acceptance Criteria Verification

| Criterion | Verified |
|-----------|----------|
| `PredecessorTitles: List<string>?`, JSON-serialized | ✅ Present + HasConversion + ValueComparer |
| `IsExternalDependency: bool`, default `false` | ✅ Present, default `false` in property and migration |
| `ExternalOwner: string?` | ✅ Present, nullable, `varchar(100)` |
| `WiTemplate: WiTemplateType` (not redefined) | ✅ From `IWiClassifier.cs`, not redefined |
| `TestedByTitles: List<string>?`, JSON-serialized | ✅ Present + HasConversion + ValueComparer |
| `ExternalDependencyCount: int`, default `0` | ✅ On ArtifactSet, default `0` |
| Migration Up() adds all 6 columns | ✅ Verified |
| Migration Down() drops all 6 columns | ✅ Verified |
| Column names match EF config | ✅ All 6 verified |
| wi_template default consistent | ✅ `"Standard"` in migration, `WiTemplateType.Standard` in C# |

---

_Hawkeye — you see what others miss. Code ships._
