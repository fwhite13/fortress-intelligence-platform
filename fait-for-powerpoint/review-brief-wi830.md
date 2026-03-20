# Review Brief: WI830 — FfP Sprint 3: Data Tables + Template Injection + Chart-as-Image
## Reviewer: Hawkeye (Clint Barton) | Cycle 1 of 2

You are performing a code review of WI830 in the fait-for-powerpoint repo.
Commit: 999bf25. Build: PASS (0 TS errors, 437KB bundle).

## Files to Review (read each one)

- src/taskpane/services/faitApi.ts — fetchTemplateBase64 safety gate
- src/taskpane/services/pptTableWriter.ts — table creation logic
- src/taskpane/services/pptChartRenderer.ts — Chart.js canvas render
- src/taskpane/services/pptSpecParser.ts — spec parsing for new types
- src/taskpane/services/pptTemplateService.ts — template injection
- public/manifest.xml — MinVersion check
- manifest.local.xml — MinVersion check
- package.json — chart.js dependency placement

## Priority Checks

### HIGH-1: fetchTemplateBase64 safety gate (faitApi.ts)
- `// TODO: DO NOT SHIP` comment must be present
- Real endpoint call must be commented out / absent
- `console.warn()` noting hardcoded behavior must be present
- Function must return hardcoded TEST_PPTX_BASE64, not call the real API

### HIGH-2: specificCellProperties dimensions (pptTableWriter.ts)
- totalRows = spec.rowCount + 1 (header row included in count)
- specificCellProperties must have EXACTLY totalRows rows
- Each row must have exactly spec.columnCount entries
- Empty data cells must use `{}` not null/undefined

### HIGH-3: chart.js placement (package.json)
- chart.js must be in "dependencies", NOT "devDependencies"
- It's a runtime browser dependency

### HIGH-4: chart.js named imports (pptChartRenderer.ts)
- No `import 'chart.js/auto'` anywhere in the codebase
- Must import named: Chart, CategoryScale, LinearScale, BarController, BarElement,
  LineController, LineElement, PointElement, PieController, ArcElement, Title, Tooltip, Legend
- Chart.register(...) must register all those types

### HIGH-5: responsive:false + animation:false (pptChartRenderer.ts)
- `responsive: false` must be in Chart options (prevents resize observer issues off-screen)
- `animation: false` must be in Chart options (ensures complete render before toDataURL)

### MEDIUM-1: addTable() totalRows argument (pptTableWriter.ts)
- slide.shapes.addTable(totalRows, columnCount, options)
- First arg must be spec.rowCount + 1 (or equivalent variable including header)

### MEDIUM-2: insertSlidesFromBase64 formatting (pptTemplateService.ts)
- Default formatting = PowerPoint.InsertSlideFormatting.useDestinationTheme
- Optional override from ppt_template_spec.keepSourceFormatting

### MEDIUM-3: Canvas cleanup (pptChartRenderer.ts)
- chart.destroy() AND document.body.removeChild(canvas) must happen on BOTH success and error
- Must use try/finally or equivalent to guarantee cleanup

### MEDIUM-4: Both manifests MinVersion="1.8"
- public/manifest.xml: <Set Name="PowerPointApi" MinVersion="1.8"/>
- manifest.local.xml: <Set Name="PowerPointApi" MinVersion="1.8"/>

### LOW-1: Spec parser completeness (pptSpecParser.ts)
- parseTableSpec() must exist and handle ppt_table_spec blocks
- parseChartSpec() must exist and handle ppt_chart_spec blocks
- parseTemplateSpec() must exist and handle ppt_template_spec blocks

### LOW-2: FfE repo untouched
- ~/projects/fait-for-excel/ src files must NOT be modified
- Only pipeline/ artifacts are expected

## Instructions
Read each file listed above. For each Priority Check:
1. State the finding (PASS / FAIL / WARN)
2. Quote the exact relevant code lines
3. Note any issues with severity: CRITICAL / IMPORTANT / NITPICK

Produce a structured report with all findings.
