# Build Report — ADO #5115

## What was built
Updated `CLAUDE.md` with honest documentation of the Excel pivot table limitation. CC cannot generate native interactive Excel pivot tables (no PivotCache XML). Added guidance for CC to acknowledge this to users and deliver clearly-labeled summary tables instead.

## Files changed
- `fait/agent-harness/CLAUDE.md` — New section `## Excel Pivot Table Limitation` appended

## Parallelization used
No — single-file doc update.

## CC sessions run
1 CC run (3 turns, goal met).

## What the documentation says
1. **Limitation:** openpyxl/xlsxwriter cannot generate PivotCache XML. The pivot opens empty/broken in Excel.
2. **Behavior rule:** CC must acknowledge the limitation upfront before delivering an alternative
3. **Alternative:** Generate a clearly-labeled "Summary Table (note: Excel interactive pivot tables require Microsoft Excel...)" with manual aggregation
4. **Language rule:** Never call it a "pivot table" — always "summary table" or "aggregation table"
5. **Optional offer:** Offer to put raw data in a separate sheet so the user can create a real pivot in Excel

## Goal choice
Implemented Option 4 (CLAUDE.md documentation). Option 1-3 (pywin32, xlwings, raw XML injection) are not viable on Linux Fargate — Windows/Excel required or high maintenance cost.

## Acceptance criteria verification
- [x] Root cause confirmed and documented (PivotCache XML gap)
- [x] CLAUDE.md updated at `fait/agent-harness/CLAUDE.md`
- [x] CC behavior guidance: acknowledge limitation, use summary table, correct labeling
- [x] Commit `6e9558a5` on fred-dev

## Known edge cases / things Clint should scrutinize
- This is a guidance document — CC compliance depends on it reading CLAUDE.md at task start (which it does by default in the harness workspace)
- If a user explicitly asks for raw XML injection anyway, CC may still attempt it — the doc discourages but doesn't prevent it. Accept as reasonable.
