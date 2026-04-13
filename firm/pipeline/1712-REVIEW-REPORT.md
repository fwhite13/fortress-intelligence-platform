# Review Report — ADO #1712 — Download Missing Sections

**Reviewer:** Hawkeye (Clint Barton) — Cycle 1  
**Commit:** `8e08230`  
**Date:** 2026-04-13  
**Scope:** `MeetingsApiController.cs` — `DownloadSummary` + `ActionItem` class  
**Risk:** Low

---

## Verdict: ✅ PASS

---

## Spec Compliance Check

**What was built:** Removed `else` gating structured sections from `SummaryText`. Sections now always append. Each section has its own try/catch. Added `[JsonPropertyName]` to `ActionItem` class. S3 early-return path untouched.

**Files touched:**
- `Controllers/MeetingsApiController.cs` — ✅ correct file, correct change

**Scope:** ✅ No out-of-scope logic changes (TeamsGraphService.cs received only a TODO comment)

**Spec compliance verdict:** ✅ COMPLIANT

---

## CC Review Summary

CC ran adversarial analysis against all 8 targeted checks. No issues found. All findings below are PASS.

---

## Consistency Audit

| Check | Files | Result |
|-------|-------|--------|
| `[JsonPropertyName]` on `ActionItem` match summarizer output | `MeetingsApiController.cs:956-961` ↔ `TeamsGraphService.cs:499` | ✅ Exact match: `"description"`, `"owner"`, `"deadline"` |
| `KeyDecisionsJson` deserialized as `List<string>` matches format | `MeetingsApiController.cs:401` ↔ `TeamsGraphService.cs:500` | ✅ Summarizer outputs flat string array; `List<string>` is correct |
| `FollowUpsJson` deserialized as `List<string>` | `MeetingsApiController.cs:429` ↔ summarizer | ✅ Same flat array format |
| S3 path unchanged | `MeetingsApiController.cs:370-380` | ✅ Returns before structured section code |

---

## Critical Issues: 0

---

## Important Issues: 0

---

## Nitpicks: 0

---

## Detailed Check Results

### CHECK 3: Download Always-Append Logic — PASS

The `else` is gone. Structured sections are at top-level scope, each isolated:

```csharp
if (!string.IsNullOrEmpty(summary.SummaryText))
{
    mdSb.AppendLine(summary.SummaryText);
    mdSb.AppendLine();
}

// Always append structured sections
if (!string.IsNullOrEmpty(summary.KeyDecisionsJson))
{
    try { ... } catch { /* Non-fatal */ }
}
if (!string.IsNullOrEmpty(summary.ActionItemsJson))
{
    try { ... } catch { /* Non-fatal */ }
}
if (!string.IsNullOrEmpty(summary.FollowUpsJson))
{
    try { ... } catch { /* Non-fatal */ }
}
```

A malformed JSON in one section does not prevent the others from rendering. Isolation is correct.

### CHECK 4: S3 Path Unchanged — PASS

S3 early-return at lines 370–380 executes before the `StringBuilder` is constructed. The structured section code begins at line 397. The S3 branch exits the method entirely — no regression, known limitation preserved as documented.

### CHECK 6: KeyDecisionsJson Format — PASS

Summarizer prompt (`TeamsGraphService.cs:500`):
```
"keyDecisionsJson": "["Decision text here"]"
```

Flat string array. Controller deserializes as `List<string>` — correct.

### CHECK 7: Scope Compliance — PASS

Logic changes confined to `DownloadSummary` method and `ActionItem` class in `MeetingsApiController.cs`. `TeamsGraphService.cs` received only a TODO comment (no logic changes). No out-of-scope edits.

### CHECK 8: Owner Null Guard — PASS

```csharp
items.ForEach(i => mdSb.AppendLine($"- **{i.Owner ?? "TBD"}**: {i.Description} _(due: {i.Deadline ?? "TBD"})_"));
```

Both `Owner` and `Deadline` null-coalesce to `"TBD"`. Old code had bare `{i.Owner}` — null owner would have rendered `****: description`. Fixed.

---

## Spec Fidelity

- ✅ `else` removed — structured sections unconditionally append
- ✅ Three independent try/catch blocks — one bad JSON section cannot kill the rest
- ✅ `ActionItem` class has `[JsonPropertyName]` on all three properties
- ✅ `Owner` null-guarded in download output
- ✅ S3 path returns before any structured section code — known limitation preserved and documented
- ✅ `KeyDecisionsJson` and `FollowUpsJson` correctly deserialized as `List<string>` (matches summarizer flat-array output)

---

## Positive Observations

- Per-section try/catch is the right pattern here — download should be best-effort, not all-or-nothing. Even if action items JSON is corrupt, users still get decisions and follow-ups.
- `if (decisions?.Any() == true)` guard prevents empty section headers from appearing in the download when the list is populated but empty.
- The `mdSb.AppendLine()` separator after `SummaryText` ensures structured sections don't run directly into the end of the overview markdown.

---

_Hawkeye — Cycle 1 complete. Ships._
