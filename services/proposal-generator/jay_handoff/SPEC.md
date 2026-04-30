# NBAIS WC Proposal Generator — AI Engineer Specification

**Version:** 2.0 (FINAL)
**Owner:** Caleb / NBAIS
**Last updated:** 2026-04-30

---

## 1. Overview

This specification describes the production system for generating personalized Workers' Compensation insurance proposals for NBAIS members. The proposal is rendered from an HTML template populated with member-specific data scraped from the carrier (BAWNSIG) quote PDF, then converted to PDF for member delivery.

### High-level flow

1. Carrier (BAWNSIG, via Lusense) issues a quote PDF for a member.
2. AI generator scrapes structured data from the quote.
3. AI generator populates merge fields in `proposal.html`.
4. Server renders populated HTML to PDF.
5. PDF is delivered to the member by the producer (Dianne Slater) for review and signature.

---

## 2. Source files

| File | Purpose |
|---|---|
| `proposal.html` | Master template — 9 page sections with merge field tokens |
| `styles.css` | Print-targeted stylesheet — controls all layout and branding |
| `logo_horizontal.png` | NBAIS horizontal lockup — used in page headers |
| `logo_stacked.png` | NBAIS stacked logo — used on cover page |

All files must reside in the same directory at runtime. Relative paths in the HTML reference the CSS and image files directly.

---

## 3. Rendering pipeline

The engineer may use any HTML-to-PDF rendering tool. The template was authored to be tool-agnostic. Tested options:

- **WeasyPrint (Python)** — `HTML("proposal.html").write_pdf("output.pdf")`
- **Puppeteer (Node.js)** — `page.pdf({ format: 'Letter', printBackground: true })`
- **Prince XML** — commercial, highest fidelity
- **wkhtmltopdf** — works but has older CSS support; not preferred

### Critical rendering settings

- Page size: **US Letter (8.5" × 11")**
- Margins: **0** (the CSS handles internal padding via `.page` class)
- Background printing: **enabled** (required for navy banners, shaded rows)
- DPI / scale: **default (100%)** — do not scale down or shrink to fit

---

## 4. Page order (FINAL)

| # | Section | Notes |
|---|---|---|
| 1 | Cover Page | NBAIS branding, member meta |
| 2 | Cover Letter | About + program highlights + what's included |
| 3 | Premium Summary | Coverage at a Glance table |
| 4 | Coverage Details (1 of 2) | Policy info, named insured, limits, surplus contribution |
| 5 | Coverage Details (2 of 2) | **Class Schedule + Excluded Persons + SIG Disclosure** — expandable tables sit at top of page |
| 6 | Next Steps + Member Authorization | **Signature page — bindable** |
| 7 | Coverage Recommendations (1 of 3) | Commercial Lines part 1 |
| 8 | Coverage Recommendations (2 of 3) | Commercial Lines part 2 + Personal + Bonds |
| 9 | Employee Benefits Recommendations | Group benefits, life dept, retirement |

The signature page (page 6) binds the WC coverage above it. Coverage Recommendations and Employee Benefits come after as informational leave-behind, separate from what the member signed.

---

## 5. Merge fields

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
| `{{EXCLUDED_PERSON_1}}` | First name on Form D-43 rejection list | `James Moretti` | Conditional (see §6) |
| `{{EXCLUDED_PERSON_2}}` | Second name on Form D-43 rejection list | `Sandra Cole` | Conditional (see §6) |

### Field appearance

Tokens are visually distinguished in the template using `<span class="mf">{{FIELD_NAME}}</span>`. The CSS renders these in red italic to make placeholders obvious during template review. After replacement, the surrounding `<span class="mf">` should be removed or its styling overridden so the populated value appears in normal black text.

```python
# Replace token AND remove the .mf wrapper
html = html.replace('<span class="mf">{{MEMBER_NAME}}</span>', f'<strong>{member_name}</strong>')
```

---

## 6. Conditional logic

### Excluded Persons block (Page 5)

Tables appear at the top of page 5 by design — they are positioned to grow downward as needed without breaking pagination.

| Quote contains | Action |
|---|---|
| **Zero excluded persons** | Remove the entire `<h3>Excluded Persons</h3>` block, the table, and the two notes that follow. Keep Class Schedule and SIG Disclosure. |
| **One excluded person** | Populate `{{EXCLUDED_PERSON_1}}`. Remove the second `<tr>` row containing `{{EXCLUDED_PERSON_2}}`. |
| **Two excluded persons** | Populate both fields. No structural changes. |
| **Three or more excluded persons** | Populate the first two rows, then duplicate the row pattern for each additional person. |

### Multiple class codes

The Employee Classification Schedule on Page 5 currently shows a single row. If the carrier quote contains multiple class codes:

1. Duplicate the data row pattern for each class code.
2. Populate each row's merge fields with the corresponding class code's data.
3. Update the "Total Estimated Premium" footer row to sum all rows' premiums.
4. The `{{EST_PREMIUM}}` field on Page 3 (Premium Summary) should reflect the total of all class codes combined.

### Page break handling

Both expandable tables (Class Schedule and Excluded Persons) live at the top of page 5 by design. Caleb expects a maximum of 2–4 class codes and 0–4 excluded persons in typical cases — the page has space for this without overflow.

If content does overflow page 5, ensure your renderer respects:
- `page-break-inside: avoid` on individual `<tr>` elements (no mid-row splits)
- Repeating `<thead>` on tables that span pages

WeasyPrint handles both natively.

### Producer contact info

Currently hardcoded as "Dianne Slater" with placeholder fields for title, phone, email, office address, and website (e.g., `[Phone Number]`). These are intentional placeholders pending NBAIS confirmation. When confirmed, replace these directly in the template — they are static, not per-member.

---

## 7. Member signature page (Page 6)

This page is the bindable acceptance for the WC coverage. It includes:

- Two-column contact grid (NBAIS Producer + Program Office)
- "Member Authorization" section with the following authorization paragraph:

> By signing below, the undersigned acknowledges receipt of this Workers' Compensation Insurance proposal and authorizes Nevada Builders Alliance Insurance Services (NBAIS) to bind coverage as described herein, effective on the policy period stated above. The undersigned confirms that the payroll, classification codes, and excluded persons listed in this proposal are accurate to the best of their knowledge and understands that final premium is subject to audit. The required initial down payment will be remitted online via the secure payment link provided upon binding.

- Four-line signature block: **By / Print Name / Title / Date**

**This signature block is for the member only.** No producer signature line. No additional fields (no FEIN, no witness, no notary).

---

## 8. Constraints

### Do not modify
- Section order
- Banner colors (navy `#1F3864`)
- Page count or per-page layout
- Header/footer structure
- Merge field token names
- Authorization language on signature page

### Do not condense
- Each `.page` div is intended to render as exactly one printed page.
- Do not adjust `padding`, `width`, or `height` on the `.page` class.
- If new content is needed (e.g., additional class codes that would not fit), add a new page section rather than condensing existing content.

### Output format
- Final output must be **PDF**.
- Do not deliver HTML to members directly.
- Do not deliver Word docs.

---

## 9. Validation checklist

Before delivering each generated proposal to a member, the system should confirm:

- [ ] No `{{...}}` tokens remain in the rendered PDF
- [ ] Page count is between 8 and 11 pages depending on conditional sections
- [ ] All dollar amounts on Premium Summary match the carrier quote exactly
- [ ] Member name appears consistently on cover, cover letter (×2), and footer (every page)
- [ ] Logos render correctly on cover page and every interior page header
- [ ] No layout overflow — content stays within page boundaries
- [ ] Signature block on page 6 has four blank lines, not pre-filled

---

## 10. Future enhancements (NOT in scope for v1)

Do not add without explicit direction from Caleb:

- Experience Modification Rate (Emod) section
- Endorsements schedule
- Audit notes
- Market response section
- Executive summary section
- Service team page
- About NBAIS page (intentionally removed)
- Marketing-quality additions: dividend history, competitor comparison, savings callouts

---

## 11. Contact

Questions on this specification should be directed to **Caleb** at NBAIS.
