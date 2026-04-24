# Template Authoring Checklist

## Prerequisites

- Python 3.8+ with `python-docx` installed (`pip install python-docx`)
- Run from: `/home/fredw/projects/fip/services/proposal-generator`
- Command: `python3 scripts/generate-templates.py`

## Single-Run Rule

Every `{tag}` must be contained in ONE Word run. Never split a tag across formatting boundaries.

**How to verify:** Open the `.docx` as a ZIP, inspect `word/document.xml`, and confirm each `{...}` tag appears within a single `<w:t>` element. python-docx programmatic creation guarantees this when you set the full tag string in one `add_run()` call.

**Do NOT:**
```python
p.add_run('{insured')
p.add_run('Name}')  # BROKEN: tag split across two runs
```

**Do:**
```python
p.add_run('{insuredName}')  # Correct: single run
```

## Table Formatting Requirements

Every table must have these Word XML properties applied:

| Property | Where | Purpose |
|----------|-------|---------|
| `w:keepNext` | All paragraph runs in rows (except last row) | Prevents orphan rows across page breaks |
| `w:tblHeader` | Header rows | Repeats header on each page |
| `w:keepLines` | All table cells | Keeps cell content together |

Use `apply_table_formatting(table, has_header=True)` to apply all at once.

## Loop Syntax Patterns

### Table Row Loops

Place `{#array}` in the **first cell** and `{/array}` in the **last cell** of the row to repeat.

```
| {#items}{name} | {value}{/items} |   <- entire row repeats
```

### Paragraph Loops (paragraphLoop: true)

Place `{#array}` and `{/array}` on their **own standalone paragraphs**. Content paragraphs go between them.

```
{#scheduleItems}           <- own paragraph
Item {itemNumber}          <- content paragraph
{/scheduleItems}           <- own paragraph
```

### Nested Loops

Outer loop uses paragraph style, inner loop can use table row style:

```
{#premiumRows}{coverageLabel} | {exposureHighlights} | {formattedPremium}{/premiumRows}
```

## Conditional Section Patterns

- Truthy: `{#field}...{/field}` — renders when field is truthy
- Falsy: `{^field}...{/field}` — renders when field is null/false/empty
- Inline conditional: `{#isAdmitted}Admitted{/isAdmitted}{^isAdmitted}Surplus Lines{/isAdmitted}`

## Raw XML Injection

`{@lobSectionsXml}` and `{@boilerplateSectionsXml}` must each be in their **own dedicated paragraph** with **NO other text**.

```python
p = doc.add_paragraph()
p.add_run('{@lobSectionsXml}')  # Nothing else in this paragraph
```

## Data Contract Notes

- `carrier` is an **OBJECT** — use `{carrier.name}`, `{carrier.amBestRating}`
- `deductibles[].formattedValue` — use `{formattedValue}` for display (shows "$25,000" for flat, "5%" for percentage)
- `scheduleItems[].children` may be null (e.g., WC) — only include children loop if LOB has nested items
- `premiumRows[].formattedPremium` — pre-formatted premium string for display; use `{formattedPremium}` in table row loops

## Verification

After generating templates:

1. Check file sizes are > 0: `ls -la templates/**/*.docx`
2. Validate ZIP structure: `python3 -c "import zipfile; zipfile.ZipFile('templates/verticals/nba/master.docx').testzip()"`
3. Inspect XML for split runs: unzip and search for split `{` / `}` across `<w:t>` elements
