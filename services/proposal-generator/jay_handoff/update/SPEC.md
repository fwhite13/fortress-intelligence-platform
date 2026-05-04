# NBAIS WC Proposal Generator — AI Engineer Specification

**Version:** 2.1 (FINAL)
**Owner:** NBAIS / Caleb
**Last updated:** 2026-04-30

---

## 1. Overview

This specification describes the production system for generating personalized Workers' Compensation insurance proposals for NBAIS members. The proposal is rendered from a self-contained HTML template populated with member-specific data scraped from the carrier (BAWNSIG) quote PDF, then converted to PDF for member delivery.

### High-level flow

1. Carrier (BAWNSIG, via Lusense) issues a quote PDF for a member.
2. AI generator scrapes structured data from the quote.
3. AI generator populates merge field tokens in `proposal.html`.
4. Server renders populated HTML to PDF.
5. PDF is delivered to the member by the producer (Dianne Slater) for review and signature.

---

## 2. Source files in this bundle

| File | Purpose |
|---|---|
| `proposal.html` | Master template — single self-contained HTML file. CSS is inlined. Both NBAIS logos are base64-embedded. No external dependencies. |
| `sample_output.pdf` | Reference PDF rendered from the template using WeasyPrint, showing how the final output should look. Use this to validate your rendering pipeline produces matching output. |
| `SPEC.md` | This document. |

The template is fully self-contained — no external CSS files, no external image files. The engineer can drop `proposal.html` into any rendering environment and produce a PDF without dependency management.

---

## 3. Rendering pipeline

The template was authored to be tool-agnostic but has been **specifically tuned for WeasyPrint**. The reference `sample_output.pdf` was rendered with WeasyPrint.

### Recommended: WeasyPrint (Python)

```python
from weasyprint import HTML

# Populate merge fields first
with open('proposal.html', 'r') as f:
    html = f.read()
html = html.replace('{{MEMBER_NAME}}', member_name)
# ... replace other tokens ...

# Render to PDF
HTML(string=html).write_pdf('output.pdf')
```

### Alternative: Puppeteer (Node.js)

```javascript
await page.setContent(populatedHtml, { waitUntil: 'networkidle0' });
await page.pdf({
  path: 'output.pdf',
  format: 'Letter',
  printBackground: true,
  margin: { top: 0, right: 0, bottom: 0, left: 0 }
});
```

If using Puppeteer, expect minor visual differences from the reference PDF — Chrome's flex layout handling is more forgiving than WeasyPrint's. Validate against the reference PDF.

### Alternative: Prince XML

Commercial tool, highest fidelity, drop-in replacement for WeasyPrint.

### Critical rendering settings

- Page size: **US Letter (8.5" × 11")**
- Page margins: Handled by the CSS `@page` rule (0.5in top/bottom, 0.55in left/right). Do not override.
- Background printing: **MUST be enabled** (required for navy banners, shaded table rows, callout backgrounds)
- DPI / scale: **default (100%)** — do not scale down or shrink to fit

---

## 4. WeasyPrint-specific layout notes

The template was tuned for WeasyPrint after observing layout differences from browser rendering. The following adjustments are baked into the CSS:

- **Page margins are in `@page`**, not in `.page` padding. WeasyPrint uses `@page` for actual page-break boundaries.
- **`.page` has fixed `height: 9in`** with `display: block`. Do not change to `flex` — WeasyPrint's flex implementation will overflow content across pages.
- **`.page-footer` uses `position: absolute; bottom: 0`** to pin to page bottom. Do not use `margin-top: auto` in flex containers — WeasyPrint renders this as a filled grey block.
- **Body background is white in print**, grey only in screen media query. Do not change — grey body bleeds through where content doesn't fill the page.

If you switch rendering engines, you may need to revisit these. Test by rendering a sample and visually comparing to `sample_output.pdf`.

---

## 5. Page order (FINAL)

| # | Section | Notes |
|---|---|---|
| 1 | Cover Page | NBAIS branding, member meta |
| 2 | Cover Letter | About + program highlights + what's included. **No date/address/RE/Dear letterhead block** — content starts at "About this proposal" heading. |
| 3 | Premium Summary | Coverage at a Glance table |
| 4 | Coverage Details (1 of 2) | Policy info, named insured, limits, surplus contribution |
| 5 | Coverage Details (2 of 2) | **Class Schedule + Excluded Persons + SIG Disclosure** — expandable tables sit at top of page |
| 6 | Next Steps + Member Authorization | **Signature page — bindable** |
| 7 | Coverage Recommendations (1 of 3) | Commercial Lines part 1 |
| 8 | Coverage Recommendations (2 of 3) | Commercial Lines part 2 + Personal + Bonds |
| 9 | Employee Benefits Recommendations | Group benefits, life dept, retirement |

The signature page (page 6) binds the WC coverage above it. Coverage Recommendations and Employee Benefits come after as informational leave-behind, separate from what the member signed.

---

## 6. Merge fields

All merge fields use double-curly-brace syntax: `{{FIELD_NAME}}`. The AI generator must perform a string replacement on the HTML before rendering.

### Field reference

| Field | Source on carrier quote | Format example | Required |
|---|---|---|---|
| `{{MEMBER_NAME}}` | "Quote Prepared For" line — entity name | `Carson Valley Excavation and Concrete, LLC` | Yes |
| `{{MEMBER_ADDRESS}}` | Address block under member name | `306 Bath St., Carson City, NV 89703` | Yes |
| `{{MEMBER_LEGAL_NAME}}` | Legal entity — typically same as MEMBER_NAME unless DBA | `Carson Valley Excavation and Concrete, LLC` | Yes |
| `{{POLICY_PERIOD}}` | "Policy Period" line | `4/24/2026 – 12/31/2026` | Yes |
| `{{QUOTE_DATE}}` | "Quote Date" line | `4/27/2026` | Yes |
| `{{CLASS_CODE}}` | Class Code column | `6217` | Yes |
| `{{CLASS_DESCRIPTION}}` | Class Code Description column | `Excavation & Drivers` | Yes |
| `{{EST_ANNUAL_PAYROLL}}` | Estimated Annual Payroll column | `$144,000` | Yes |
| `{{RATE}}` | Rate column | `$4.995` | Yes |
| `{{EST_PREMIUM}}` | Estimated Premium column | `$7,192.80` | Yes |
| `{{SURPLUS_CONTRIBUTION}}` | "Surplus Contribution (8%)" line | `$575.42` | Yes |
| `{{EMPLOYERS_LIABILITY_FEE}}` | "Employer's Liability" charge line | `$120.00` | Yes |
| `{{TOTAL_ESTIMATED_PREMIUM}}` | "Total Estimated Premium" line | `$7,888.22` | Yes |
| `{{DOWN_PAYMENT}}` | "Initial Downpayment" line | `$1,972.05` | Yes |
| `{{EXCLUDED_PERSON_1}}` | First name on Form D-43 rejection list | `James Moretti` | Conditional (see §7) |
| `{{EXCLUDED_PERSON_2}}` | Second name on Form D-43 rejection list | `Sandra Cole` | Conditional (see §7) |

### Field appearance in template

Tokens are visually distinguished using `<span class="mf">{{FIELD_NAME}}</span>`. The CSS renders these in red italic to make placeholders obvious during template review.

After replacement, the populated value should appear in normal black text. Recommended approach: replace the entire span:

```python
# Replace token AND remove the .mf wrapper
html = html.replace(
    '<span class="mf">{{MEMBER_NAME}}</span>',
    member_name
)
```

For the Named Insured field on Page 4 (which uses bold navy styling), preserve the surrounding `<p>` styling but replace the `<span class="mf">` with plain text or `<strong>`.

---

## 7. Conditional logic

### Excluded Persons block (Page 5)

| Quote contains | Action |
|---|---|
| **Zero excluded persons** | Remove the entire `<h3>Excluded Persons</h3>` block, the table, and the two `<p class="fine">` notes that follow. Keep Class Schedule and SIG Disclosure. |
| **One excluded person** | Populate `{{EXCLUDED_PERSON_1}}`. Remove the second `<tr>` row containing `{{EXCLUDED_PERSON_2}}`. |
| **Two excluded persons** | Populate both fields. No structural changes. |
| **Three or more excluded persons** | Populate the first two rows, then duplicate the row pattern for each additional person. |

### Multiple class codes

The Employee Classification Schedule on Page 5 currently shows a single row. If the carrier quote contains multiple class codes:

1. Duplicate the data row pattern for each class code.
2. Populate each row's merge fields with the corresponding class code's data.
3. Update the "Total Estimated Premium" footer row to sum all rows' premiums.
4. The `{{EST_PREMIUM}}` field on Page 3 (Premium Summary) should reflect the total of all class codes combined.

### Producer contact info

Currently hardcoded as "Dianne Slater" with placeholder fields for title, phone, email, office address, and website (e.g., `[Phone Number]`). These are intentional placeholders pending NBAIS confirmation. When confirmed, replace these directly in the template — they are static, not per-member.

---

## 8. Page break handling

Both expandable tables (Class Schedule and Excluded Persons) live at the top of page 5 by design. Typical cases will have 2–4 class codes and 0–4 excluded persons — the page has space for this without overflow.

If content overflows page 5, the renderer should respect:
- `page-break-inside: avoid` on individual `<tr>` elements (no mid-row splits)
- Repeating `<thead>` on tables that span pages

WeasyPrint handles both natively. **Page count is variable** — proposals may render in 8–12 pages depending on conditional sections and table expansion.

---

## 9. Member signature page (Page 6)

This page is the bindable acceptance for the WC coverage. It includes:

- Two-column contact grid (NBAIS Producer + Program Office)
- "Member Authorization" section with the following authorization paragraph (locked — do not modify):

> By signing below, the undersigned acknowledges receipt of this Workers' Compensation Insurance proposal and authorizes Nevada Builders Alliance Insurance Services (NBAIS) to bind coverage as described herein, effective on the policy period stated above. The undersigned confirms that the payroll, classification codes, and excluded persons listed in this proposal are accurate to the best of their knowledge and understands that final premium is subject to audit. The required initial down payment will be remitted online via the secure payment link provided upon binding.

- Four-line signature block: **By / Print Name / Title / Date**

**This signature block is for the member only.** No producer signature line. No additional fields (no FEIN, no witness, no notary).

---

## 10. Constraints

### Do not modify
- Section order
- Banner colors (navy `#1F3864`, blue `#2E75B6`, lt-blue `#EBF3FB`)
- Page count or per-page layout
- Header/footer structure
- Merge field token names
- Authorization language on signature page
- The base64-embedded logos
- WeasyPrint-specific layout adjustments noted in §4

### Do not condense
- Each `.page` div is intended to render as exactly one printed page.
- Do not adjust `padding`, `width`, or `height` on the `.page` class.
- If new content is needed (e.g., additional class codes that would not fit), add a new page section rather than condensing existing content.

### Output format
- Final output must be **PDF**.
- Do not deliver HTML to members directly.
- Do not deliver Word docs.

---

## 11. Validation checklist

Before delivering each generated proposal to a member, the system should confirm:

- [ ] No `{{...}}` tokens remain in the rendered PDF
- [ ] No red italic merge-field-style text remains in the rendered PDF (would indicate `.mf` span was not removed during replacement)
- [ ] Page count is between 8 and 12 pages depending on conditional sections
- [ ] All dollar amounts on Premium Summary match the carrier quote exactly
- [ ] Member name appears consistently on cover, cover letter, and footer (every interior page)
- [ ] Both NBAIS logos render correctly (stacked on cover, horizontal on every interior page header)
- [ ] No layout overflow — content stays within page boundaries
- [ ] No grey bars or unfilled background visible on any page
- [ ] Signature block on page 6 has four blank lines, not pre-filled
- [ ] Visual comparison against `sample_output.pdf` shows matching layout

---

## 12. Future enhancements (NOT in scope for v1)

Do not add without explicit direction from NBAIS:

- Experience Modification Rate (Emod) section
- Endorsements schedule
- Audit notes
- Market response section
- Executive summary section
- Service team page
- About NBAIS page (intentionally removed)
- Marketing-quality additions: dividend history, competitor comparison, savings callouts
- Multiline proposal template (separate spec, not yet started)

---

## 13. Contact

Questions on this specification should be directed to **Caleb** at NBAIS / FAM Operations.
