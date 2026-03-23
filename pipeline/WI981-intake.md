# WI#981 — Branding: Shift to Navy + Red Palette

## Type
Design / Branding

## Source
Lauren Williams feedback (email 2026-03-20 5:52 PM); Fred White direction

## Description
Lean into the black/navy and red color palette more strongly, consistent with the Titan mock-up Lauren provided (NOT the IAAPA mock-up). Current UI has teal/cerulean as a primary accent color; this should shift to red.

## Expected Changes

### Color Token Updates
| Element | Current | Target |
|---------|---------|--------|
| Primary action buttons | Teal `#00bcd4` / cerulean | Red (e.g. `#c0392b` or `#e53935`) |
| Nav selected/active state | Teal highlight bar | Red highlight |
| User avatar background | Teal | Red |
| Link / accent color | Teal | Red |
| CTA hover states | Teal variants | Red variants |
| Status badge (Waiting on Market) | Teal/cyan | Adjust to red-adjacent or neutral |

### Keep As-Is
| Element | Value |
|---------|-------|
| Sidebar background | Dark navy `#1a1f36` (or equivalent) |
| Logo gold accent | Gold/amber |
| Kanban card backgrounds | White |
| Page background | Light gray |
| Text colors | Dark navy / charcoal |

## Notes
- Lauren: "Lean into the black and red more like the Titan mock up I sent vs IAAPA mock up"
- Fred will ask Lauren for a more complete mock-up for the full style rework — this WI captures the initial palette shift
- Scope: global CSS token / variable change, not per-component overrides
- Verify: all buttons, badges, nav highlights, avatars, and interactive states reflect the new palette
- Do NOT change sidebar nav structure or layout — color shift only for now
