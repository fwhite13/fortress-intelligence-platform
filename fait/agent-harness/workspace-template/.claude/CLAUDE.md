# CLAUDE.md — CC Workspace Rules

## File Discipline
You are working in a persistent workspace. **Prefer modifying existing files over creating new ones.**

- When the user refers to an existing file by name, open and modify that file
- When the user says "update", "fix", "change", "add to", or "improve" something — find the existing file and edit it
- Only create a new file when the user explicitly asks for a new file, or the task genuinely requires a new artifact that does not exist yet
- When in doubt: list the workspace files first, then decide

## Workspace Awareness
At the start of each task, you will receive a list of files currently in the working folder. Use this to understand what already exists before writing anything.

## Web Tools

**web_search** — Use for discovery: finding pages, researching topics, answering questions about what exists on the web. Returns a list of relevant URLs and summaries. Use when the user asks a general question that benefits from current web information.

**web_fetch** — Use for extraction: reading the actual content of a specific page the user has provided or that you found via web_search. Returns the full page text as markdown. Use when:
- The user provides a URL and asks you to read, summarize, or extract information from it
- The user asks you to "match the style of" or "follow the format of" a specific website
- You've found a promising result via web_search and need to read the full content
- The user asks for specific details that wouldn't be in a search snippet

Do not use web_search when the user has already given you a specific URL — use web_fetch directly.
Do not use web_fetch for general questions where you don't have a target URL — use web_search first.

## Excel Pivot Table Limitation

**openpyxl and xlsxwriter do not support native Excel pivot tables.** Neither library can generate the PivotCache XML that Excel requires for interactive pivot tables — formulas, slicers, pivot field lists, and drill-down do not work with programmatically generated pivot XML.

### When a user asks for a pivot table:
1. **Always acknowledge the limitation first**: "Note: Python libraries available here (openpyxl/xlsxwriter) cannot generate native interactive Excel pivot tables. I'll create a structured summary table as an alternative."
2. **Generate a clearly-labeled summary table** using openpyxl with:
   - A prominent header cell labeled: `Summary Table (Excel interactive pivot tables require Microsoft Excel — not available in this environment)`
   - Row/column aggregations using Python (pandas groupby or manual dict aggregation)
   - Formatted borders, headers, and number formatting for readability
3. **Never describe the output as a pivot table** — always use "summary table" or "aggregation table"
4. **Offer an alternative if appropriate**: "If you need interactive pivot functionality, I can create the raw data in a separate sheet — you can then insert a pivot table yourself in Excel."

### Why this limitation exists
- `openpyxl` can write basic `PivotTable` XML nodes but does not populate `PivotCache` (the data cache Excel requires to make the pivot functional) — the result opens as an empty/broken pivot in Excel
- `xlsxwriter` explicitly does not support pivot tables
- `pywin32`/`xlwings` require Windows + Excel — not available in Linux Fargate
- Raw XML injection is possible but produces fragile, version-sensitive output not worth the maintenance cost

### Correct pattern
```python
import openpyxl
from openpyxl.styles import Font, PatternFill, Alignment, Border, Side

wb = openpyxl.Workbook()
ws = wb.active
ws.title = "Summary"

# Header note
ws['A1'] = "Summary Table (Excel interactive pivot tables require Microsoft Excel — not available in this environment)"
ws['A1'].font = Font(italic=True, color="666666", size=9)

# Your aggregated data here...
```

## Working with Binary Files

### PDF files
- PDFs under 3 MB are passed to you as native document blocks — you can read them directly.
- PDFs 3–15 MB have text extracted server-side; charts and images will not be visible.
- PDFs over 15 MB cannot be read in chat mode — tell the user to switch to task mode.

### Excel files (.xlsx, .xls)
- Excel files cannot be read directly in chat mode.
- When a user asks about an Excel file, tell them to switch to task mode where you can use Python (`openpyxl`) to analyze it.

### Durable extraction pattern
When you analyze a PDF or Excel file in task mode and the user may want to reference it again in chat mode:
- Write a structured markdown extract to the working folder: `<original-name>-extract.md`
- Include key data, tables, summaries — whatever makes the file useful in text form
- The assistant can then `read_file` that extract on future turns without needing another CC task
