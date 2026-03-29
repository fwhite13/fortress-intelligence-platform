# ADO#1350 — FIRM FirmDbContext HasColumnType("JSON") Removal — Hawkeye Review Brief

## Context
Commit `2bac7aa` removes `.HasColumnType("JSON")` from three `string?` properties in `FirmMeetingSummary` entity config inside `FirmDbContext.cs`. Root cause: Pomelo's `ElementMappingConvention` throws NullRef in `FindCollectionMapping` when `HasColumnType("JSON")` is applied to `string?` properties.

## Files to Read
1. `firm/src/FortressIntelligenceRM.Web/Data/FirmDbContext.cs` — read the FULL file

## Diff Being Reviewed
```diff
-            entity.Property(e => e.ActionItemsJson).HasColumnName("action_items_json").HasColumnType("JSON");
-            entity.Property(e => e.KeyDecisionsJson).HasColumnName("key_decisions_json").HasColumnType("JSON");
-            entity.Property(e => e.FollowUpsJson).HasColumnName("follow_ups_json").HasColumnType("JSON");
+            entity.Property(e => e.ActionItemsJson).HasColumnName("action_items_json");
+            entity.Property(e => e.KeyDecisionsJson).HasColumnName("key_decisions_json");
+            entity.Property(e => e.FollowUpsJson).HasColumnName("follow_ups_json");
```

## Review Tasks — be thorough and adversarial

### Task 1: Verify the three changed lines
Confirm that in the current file:
- `ActionItemsJson` has `HasColumnName("action_items_json")` but NO `HasColumnType("JSON")`
- `KeyDecisionsJson` has `HasColumnName("key_decisions_json")` but NO `HasColumnType("JSON")`
- `FollowUpsJson` has `HasColumnName("follow_ups_json")` but NO `HasColumnType("JSON")`

### Task 2: Check full FirmMeetingSummary entity configuration block
Locate the entire `entity => entity.ToTable("firm_meeting_summaries")` (or similar) block for FirmMeetingSummary.
Verify:
- HasKey is present and correct
- HasIndex on MeetingId is still present
- HasForeignKey / HasOne / WithMany / navigation properties intact
- HasColumnType("TEXT") on SummaryText still present (this one is correct — TEXT is safe for non-collection string)
- ModelUsed MaxLength still set
- CreatedAt default still set
- No accidental deletions or modifications beyond the 3 targeted lines

### Task 3: Full-file scan for HasColumnType("JSON") or HasColumnType("json")
Search the ENTIRE FirmDbContext.cs for:
- Any remaining `HasColumnType("JSON")` — case sensitive
- Any remaining `HasColumnType("json")` — lowercase variant
- Any remaining `HasColumnType("Json")` — mixed case
Report every occurrence found, including line number and surrounding context.
If NONE found: confirm clearly.

### Task 4: Scan for similar risk patterns
Look for any other `string?` or `string` properties in FirmDbContext that use `HasColumnType(...)` with any JSON-like value. Also look for `HasColumnType` used with any value other than "TEXT", "longtext", "varchar", "char", "decimal", "datetime", "tinyint(1)" — i.e., flag anything unusual.

### Task 5: Scope creep check
The only file that should be changed in this commit is `firm/src/FortressIntelligenceRM.Web/Data/FirmDbContext.cs`.
Confirm no other files were touched by reading the directory structure or checking for any other modified files.

## Pass/Fail Criteria
PASS if:
- All three HasColumnType("JSON") calls are gone
- All three HasColumnName calls are preserved with correct column names
- No other entity config in FirmMeetingSummary was touched
- No other HasColumnType("JSON") anywhere in the file
- No scope creep

FAIL/NEEDS-CHANGES if any of the above are violated.

## Output Format
Report findings for each Task (1-5) with:
- Explicit PASS or FAIL for each task
- Exact line numbers and code snippets for any issues
- Final verdict: PASS or NEEDS-CHANGES
