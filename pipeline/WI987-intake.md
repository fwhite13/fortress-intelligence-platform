# WI#987 — Branding: Add Fraunces Serif Font for KPI/Display Numbers

## Type
Branding / UX

## Source
TIG mock-up (TIG_Portal_v1.html) — Lauren Williams

## Description
The TIG mock-up uses two fonts:
- **Plus Jakarta Sans** — body text, UI labels, nav, buttons (sans-serif)
- **Fraunces** — KPI numbers, page titles, editorial display headings (serif, gives the "premium insurance" feel)

FAMOS currently uses a single sans-serif font throughout. Add Fraunces as a display/editorial font for KPI values and major page headings.

## Expected Behavior
- Import Fraunces from Google Fonts: `family=Fraunces:ital,wght@0,400;0,600;0,700;1,400`
- Apply to: KPI card values, large numeric displays, major dashboard headings
- Do NOT apply to: nav, buttons, labels, table content, form fields
- CSS class or variable: `.font-display` or `--font-display: "Fraunces", serif`

## Notes
- Mock-up import: `@import url('https://fonts.googleapis.com/css2?family=Plus+Jakarta+Sans:wght@300;400;500;600;700;800&family=Fraunces:ital,wght@0,400;0,600;0,700;1,400&display=swap')`
- Fraunces is a "wonky" optical-size variable serif — it reads as premium/editorial at large sizes
- Keep Plus Jakarta Sans as the body/UI font (already in use or substitute with current font)
- This WI is a dependency for WI#982 (Dashboard KPI cards) — Tony should bundle them or do this first
