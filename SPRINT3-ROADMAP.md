# FfE Sprint 3+ Roadmap

**Author:** Reed Richards (Software Architect)  
**Date:** 2026-03-16  
**Source:** Bruce's `RESEARCH-ADVANCED.md` + full source audit  
**Purpose:** Prioritized planning doc for Fred — what to build next and in what order

---

## Critical Finding: What's Already Built

Before any prioritization: I read the full source. The codebase is significantly ahead of where the sprint numbering implies. Here is the true current state:

| Feature | Sprint | Status |
|---------|--------|--------|
| Chat against FAIT KBs | S1 | ✅ Done |
| Read selected range + inject context | S2 | ✅ Done |
| Write cell values via AI suggestions | S2 | ✅ Done |
| Write-back dialog (Accept All / Review Each) | S2 | ✅ Done |
| Chart generation (FAIT → chart spec → insert) | S4 | ✅ Done |
| Pivot table creation (FAIT → pivot spec → insert) | S4 | ✅ Done |
| Conditional formatting (FAIT → CF spec → apply) | S4 | ✅ Done |
| Sort/filter (FAIT → sort spec → apply) | S5 | ✅ Done |
| Error/formula scanner | S2 | ✅ Done |
| FORGE KB search | S3 | ✅ Done |
| Session persistence | S5 | ✅ Done |
| Slash command picker | S5 | ✅ Done |

**The codebase is through Sprint 5.** Sprint 3 roadmap planning is actually Sprint 6+ planning. The framing below uses "Sprint 3+" in the sense of "what hasn't been built yet."

---

## What Genuinely Hasn't Been Built

After reading the source and Bruce's advanced research, the genuine gaps are:

1. **Table-aware context** — the add-in reads raw range values but doesn't detect or leverage Excel Table objects (`ListObject`). Enterprise data is almost always in Tables.
2. **Reactive updates via `onChanged`** — FAIT responds to what the user asks, but doesn't watch the spreadsheet for changes. There's no live data awareness.
3. **Write full dataset to range** — `writeRangeData()` spec'd in Sprint 2 but not yet built. Direct "write this table to A1" action.
4. **"Write table to sheet" UI trigger** — user action to take FAIT's last tabular response and write it to a selected cell. Depends on `writeRangeData()`.
5. **Named range registration** — FAIT outputs go to ranges that aren't tracked. User can't say "write to the same range as last time."
6. **Cell comments/annotations from FAIT** — FAIT adds `sheet.comments.add()` in `excelWriter.ts` already, but there's no dedicated comment flow. AI audit trail is incomplete.
7. **Multi-sheet report generation** — FAIT currently operates on the active sheet only. High-value use case: generate a new Report sheet with FAIT's analysis.
8. **Formula evaluation via `workbook.functions`** — no feature uses this yet. Enables FAIT to check computed values without writing to cells.
9. **Spill/dynamic array awareness** — no detection or handling of spill ranges.

---

## Recommended Sprint Sequence

### Sprint 6 — Write Table to Range (the missing Sprint 2 gap)
**Effort: Small**

This should have been Sprint 2. It's the most obvious missing piece: FAIT generates a table in its response, user wants to put it in the sheet. Right now there's no way to do that except the cell-by-cell suggestion dialog.

**Delivers:** "Write this table to sheet" button on any FAIT response that contains tabular data. User clicks, selects a target cell, data writes.

**Why first:** Unblocks direct data output. Everything else builds on write capability.

---

### Sprint 7 — Table Object Awareness
**Effort: Medium**

Excel Tables (`ListObject`) are the dominant data structure in enterprise use. Currently the add-in reads raw range values and can't tell if it's reading a Table or a plain range. This matters because:
- Table column names are semantically important (FAIT can reference them in prompts)
- Writing to a Table should extend it, not overwrite beyond its bounds
- FAIT could create Table-structured output rather than raw ranges

**Delivers:**
- `excelReader` detects if selected range is inside a Table and includes table name + structured headers in context
- `excelWriter` has a `writeToTable()` function that appends rows or updates a named Table
- Prompt context quality improves significantly for structured data

**Why here:** After write capability exists (S6), improving what gets read and written is the natural next step.

---

### Sprint 8 — Named Range Registration & Stable Addressing
**Effort: Small-Medium**

FAIT currently has no memory of where it wrote data. If the user asks "update that range you wrote earlier," FAIT has no way to resolve it. Named ranges solve this.

**Delivers:**
- After a write operation, FAIT optionally creates a named range for the output (e.g., `FAIT_Output_20260316`)
- User can reference named ranges by name in prompts ("update the FAIT_Output range")
- Settings panel shows list of FAIT-created named ranges with delete option
- Context formatter includes named range info when reading selection (if selection is a named range, include its name)

**Why here:** Relatively low effort, high value for repeat-use workflows. Completes the read/write loop.

---

### Sprint 9 — Reactive Workbook Watching (onChanged)
**Effort: Medium**

The add-in currently polls selection every 2 seconds. It doesn't react to data changes. This sprint adds event-driven awareness.

**Delivers:**
- Optional "watch mode" toggle in the taskpane header
- When enabled: `worksheet.onChanged` fires when user edits a cell in the watched range
- FAIT automatically runs a configured prompt against the updated range (e.g., "re-check for errors" or "update my summary")
- Uses `context.runtime.enableEvents = false` during FAIT writes to prevent infinite loops
- `triggerSource` check (requires bumping to ExcelApi 1.14) to ensure FAIT doesn't react to its own writes

**Why here:** This is a step-change in UX — from "pull" (user asks FAIT) to "push" (FAIT reacts). High value but needs write stability first.

**API version note:** Full implementation needs ExcelApi 1.14 for `triggerSource`. Can build at 1.13 with a debounce guard instead of `triggerSource` — functional but slightly less reliable.

---

### Sprint 10 — Multi-Sheet Report Generation
**Effort: Medium-Large**

Currently FAIT writes to the active sheet. The high-value pattern for analysts is: "take this data, analyze it, generate a Report sheet with summary + chart." This sprint makes that a first-class workflow.

**Delivers:**
- `/report` slash command: "Generate a report sheet from the selected data"
- FAIT analyzes selection, creates a new sheet named `FAIT Report — [date]`
- Writes: summary table, key metrics, and auto-generates a chart on the new sheet
- Tab colored distinctively (gold, per FIP brand)
- Existing sheet is untouched

**Why here:** Multi-sheet requires write stability (S6), Table awareness (S7), and named ranges (S8) to do well. Earlier would produce low-quality output.

---

### Sprint 11 — Formula Intelligence via workbook.functions
**Effort: Small-Medium**

Bruce's research confirms `workbook.functions` lets FAIT evaluate Excel built-in functions without writing to cells. This enables a new class of interactions.

**Delivers:**
- FAIT can answer "what would VLOOKUP return for X?" without touching the workbook
- Formula validation: "is this SUMIF formula correct for this range?" — FAIT evaluates and compares
- `/formula` slash command: user describes what they want, FAIT generates the formula string, previews the result using `workbook.functions` before offering to write it

**Why here:** Lower effort than it appears (all infrastructure exists), but requires the full context pipeline (S7) to be genuinely useful.

---

## Per-Sprint Summary Table

| Sprint | What It Delivers | APIs Used | Effort | Dependencies |
|--------|-----------------|-----------|--------|-------------|
| S6: Write Table to Range | "Write to sheet" button on tabular FAIT responses | `range.getResizedRange`, `range.values` | Small | WI#813 refactor |
| S7: Table Object Awareness | Detect Excel Tables in selection; write to Tables | `worksheet.tables`, `table.getDataBodyRange`, `table.rows.add` | Medium | S6 |
| S8: Named Range Registration | FAIT registers its output ranges; stable readdressing | `workbook.names.add`, `namedItem.getRange` | Small-Med | S6 |
| S9: Reactive Workbook Watching | Watch mode — FAIT reacts to user edits | `worksheet.onChanged`, `runtime.enableEvents` | Medium | S6, S8 |
| S10: Multi-Sheet Reports | `/report` slash command → new Report sheet | `worksheets.add`, `worksheet.tabColor`, chart APIs | Med-Large | S7, S8 |
| S11: Formula Intelligence | Evaluate formulas without writing; `/formula` command | `workbook.functions`, `range.formulas` | Small-Med | S7 |

---

## FAIT Differentiators vs Claude for Excel

Microsoft's Copilot for Excel and Claude's Excel integration both provide AI-assisted spreadsheet help. Here's what FAIT can do that those can't easily match — beyond the assumed data sovereignty advantage:

### 1. FAIT Knowledge Base Grounding (FORGE)
Copilot and Claude answer questions from public training data. FAIT answers from your firm's private KB. When a user asks "what's the approved discount rate for Q3 projections?", FAIT finds it in your internal documents. This is already built and is the primary differentiator.

### 2. Structured Output with Confirmation Gate
FAIT's write-back dialog (Accept All / Review Each / Reject) gives users control over every AI-proposed change before it touches the workbook. Copilot writes directly. This is significant for compliance-conscious financial workflows.

### 3. Custom Slash Commands for Firm-Specific Workflows
The `/` command system can be extended with firm-specific workflows (e.g., `/compliance-check`, `/rebalance`, `/attribution`). These map to structured prompts that FAIT executes with awareness of the firm's data model. This is not possible in off-the-shelf Copilot.

### 4. Error/Formula Auditing with Domain Context
The error scanner (`errorScanner.ts`) already flags `#REF!`, `#VALUE!`, hardcoded magic numbers, etc. With FORGE grounding, FAIT can explain _why_ a formula might be wrong in the context of the firm's data conventions — not just flag it.

### 5. Reactive Watching (Sprint 9)
When built: FAIT can monitor a cell range and automatically trigger a configured analysis when values change. This enables "always-on" data quality checking during model building — something neither Copilot nor a generic LLM integration provides.

### 6. Multi-Sheet Report Generation with Firm Templates (Sprint 10)
Reports generated by FAIT will follow firm-specific formats (Fortress brand colors, standard table styles). Generic AI tools produce generic-looking output.

---

## API Requirement Set Implications

**Current baseline: ExcelApi 1.13**

### Features that work within 1.13 (all recommended sprints S6–S11):

| Sprint | APIs Needed | Covered by 1.13? |
|--------|-------------|-----------------|
| S6 | `getResizedRange`, `range.values` | ✅ Yes (1.1) |
| S7 | `worksheet.tables`, table CRUD | ✅ Yes (1.1–1.7) |
| S8 | `workbook.names.add`, `namedItem` | ✅ Yes (1.4) |
| S9 | `worksheet.onChanged`, `runtime.enableEvents` | ✅ Yes (1.7, 1.9) |
| S10 | `worksheets.add`, `tabColor`, charts | ✅ Yes (1.7, 1.8) |
| S11 | `workbook.functions`, `range.formulas` | ✅ Yes (1.2, 1.1) |

**The entire recommended roadmap runs on ExcelApi 1.13.** No version bump required.

### Optional bump to 1.14 for Sprint 9:
`triggerSource` on `WorksheetChangedEventArgs` lets FAIT distinguish user edits from its own writes. Without it, Sprint 9 needs a debounce/flag guard instead (functional, less elegant). Bumping to 1.14 requires:
- M365 Windows ≥ Build 14326 (August 2021)
- Mac ≥ 16.52
- Excel Online: ✅ always supported

**Verdict:** Bump to 1.14 when Sprint 9 ships. The build date requirement is easily met for any M365 subscriber in 2026. Only concern: Office 2021 LTSC caps at 1.13. If LTSC support matters, stay at 1.13 and use the debounce guard.

### Features that would require going beyond 1.14:
- `valuesAsJson` (rich cell types): requires 1.16 — not in recommended roadmap
- Allow-edit range protection: requires 1.16 — not in recommended roadmap  
- `onNameChanged` event: requires 1.17 — not in recommended roadmap
- ExcelApi 1.18+: requires M365 subscription (no LTSC support) — nothing in this roadmap needs it

---

## Deferred / Not Recommended

### PivotTable creation (already built, but future enhancement deferred)
`pivotBuilder.ts` exists and creates pivot tables from flat data. The next level — pivot filters, slicers, calculated fields — requires significant effort for moderate incremental value. Pivot filter API (ExcelApi 1.12) is in baseline, but the UX for "design a pivot filter" is complex. **Defer pivot enhancements past S11.**

### Comments as annotation layer
`excelWriter.ts` already calls `sheet.comments.add()` when writing suggestions. A richer comments feature (thread management, reply reading, @mentions for team workflows) is medium effort for low priority. The annotation value is already partially there via existing write-back. **Defer indefinitely** unless there's a specific ask for team collaboration features.

### Dynamic array / spill range detection
Bruce confirmed `hasSpill` and `getSpillingToRange()` are available in ExcelApi 1.12. Handling spill ranges gracefully when reading context (don't truncate a LAMBDA output) is a correctness improvement. Effort is small but impact is narrow — few FAIT users will be working with LET/LAMBDA/CHOOSECOLS formulas. **Defer to a maintenance sprint when it comes up.**

### Sheet management (rename, reorder, tab color) as explicit feature
Sheet creation is part of Sprint 10. Exposing sheet management as a standalone feature (rename sheets, reorder, set tab colors on demand) adds UI complexity for low value. Users can do this in the Excel UI in seconds. **Not recommended.**

### Arbitrary formula evaluation workaround (scratch cell)
Bruce documented that `workbook.functions` only evaluates built-in functions, not arbitrary formula strings. The workaround (write to hidden scratch cell, read back, clear) is fragile and has side effects (triggers onChanged, creates undo history entries). **Not recommended.** Sprint 11's `/formula` command uses `workbook.functions` for built-ins only; complex formula preview is out of scope.

### Workbook-level save events (`onBeforeSave`)
Available via ExcelApiOnline. Would enable "validate before save" workflows. Medium effort, narrow value unless there's a specific compliance use case. **Defer — revisit if a compliance requirement surfaces.**

### Custom function (UDF) creation
Completely different architecture — requires a separate JavaScript runtime, separate manifest entries, and significant infrastructure. Very high effort, and the value for a task-pane-first tool is marginal. **Not recommended for this roadmap.**

---

## Decision Summary for Fred

**The build queue, in priority order:**

1. **S6 (Small):** `writeRangeData()` + "Write to sheet" button — the missing piece that completes the write story
2. **S7 (Medium):** Table awareness — biggest context quality improvement available
3. **S8 (Small-Med):** Named ranges — completes the read/write memory loop
4. **S9 (Medium):** Reactive watching — step-change in UX, first true "always-on" capability
5. **S10 (Med-Large):** Multi-sheet reports — highest demo value, most visible feature
6. **S11 (Small-Med):** Formula intelligence — differentiating capability for analyst users

**No API version bumps needed until Sprint 9**, at which point bumping to 1.14 is low-risk and broadly available.

**The roadmap stays entirely within the existing infrastructure** (React taskpane, `Excel.run()` pattern, FAIT API, slash command system). No architectural changes required.

---

_Roadmap by Reed Richards | All sprints S6–S11 are frontend-only. FAIT backend unchanged throughout._
