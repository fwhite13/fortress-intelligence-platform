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

## XLSX Generation

**One tool, always:** Use `POST /api/artifacts/generate-xlsx` for any XLSX output — plain tables, multi-sheet workbooks, and pivot tables all go through the same endpoint.

**Request format:**
- `title` — workbook title (string)
- `sheets` — array of `{ name, columns: [string], rows: [[values]] }` (1 or more)
- `pivot` — optional; include when a pivot table is needed:
  - `sourceSheet`, `pivotSheetName`, `rowLabels`, `columnLabels`, `valueField`, `summaryFormula`, `reportFilters`

**Pivot tables:** Include the `pivot` config block — ClosedXML handles it natively. No special-casing needed.

**Never create a standalone chart sheet.** Charts must be embedded in a data sheet alongside their source data. A tab whose only content is a chart is silently excluded from PDF rendering by LibreOffice.

**For pivot output:** Tell the user: "Open in Microsoft Excel for the interactive pivot table — other viewers may show a blank pivot sheet."

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
