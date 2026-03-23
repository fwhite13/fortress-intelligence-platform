# WI#921 — CSS Button Styling Regressions (3 bugs)

**Priority:** High — blocking Steve demo
**Component:** FAMOS — Pipeline page, Opportunity Workspace, New Opportunity dialog
**Repo:** fip monorepo (`fip/famos/`)

---

## Background

WI919 CSS audit was supposed to fix all button styling. QA reported PASS but three bugs are visibly present in the live dev environment. Natasha's QA either tested a cached build or misread the screenshots. These need to be fixed and re-tested with explicit verification criteria.

---

## Bug 1 — "+ New Opportunity" button wrong variant (Pipeline page header)

**Where:** Pipeline page (`/pipeline`), top-right header button

**What's wrong:** "+ New Opportunity" is rendering as an outlined/ghost button (white background, dark border, dark text) instead of the primary filled style.

**Expected:** `famos-btn-primary` — dark navy background, white text, same style as seen on the Dashboard's "Pipeline →" and "Tasks →" reference buttons... wait, no. Those ARE correctly outlined. The primary CTA on the pipeline page should be filled navy.

**Fix:** Verify the correct class is applied to the "+ New Opportunity" button in `Pipeline.razor` or equivalent. Should be `famos-btn-primary` (filled), not `famos-btn-outline`.

---

## Bug 2 — Dark text on dark background on primary buttons (modal + opp page)

**Where:** 
- "Create" button in New Opportunity dialog
- "Route to Market" button on Opportunity Workspace page

**What's wrong:** Button has correct dark navy background (`famos-btn-primary`) but text is rendering dark (navy/dark blue) instead of white. Text is nearly illegible — dark on dark.

**Root cause:** `famos-btn-primary` CSS class is applying background-color but `color: white` is either missing, has insufficient specificity, or MudBlazor's theme is overriding it inside modal/dialog contexts.

**Fix:** 
- Add `color: white !important` to `.famos-btn-primary` in the CSS, OR
- Verify MudBlazor theme isn't overriding button text color in dialog/overlay contexts
- Must be verified in BOTH page context AND modal/dialog context

---

## Bug 3 — "Close" button different height than sibling buttons (Opportunity Workspace header)

**Where:** Opportunity Workspace header button row ("Assign Owner" | "Park" | "Close")

**What's wrong:** "Close" button (`famos-btn-danger`) is a different height/size than the adjacent "Assign Owner" and "Park" buttons (`famos-btn-outline`). Creates visually uneven button row.

**Fix:** Ensure `famos-btn-danger` has identical `height`, `padding`, `line-height`, and `font-size` as `famos-btn-outline`. All three buttons should be the same size — color difference is intentional, size difference is not.

---

## Acceptance Criteria

1. "+ New Opportunity" on Pipeline page renders as filled navy button with white text
2. "Create" button in New Opportunity dialog has white text on dark navy background — legible
3. "Route to Market" button on Opportunity Workspace has white text on dark navy background — legible
4. "Close", "Assign Owner", and "Park" buttons in Opportunity Workspace header are identical height
5. All four verified by Natasha with explicit screenshot comparison against `correct.png` reference

## QA Note for Natasha

Reference screenshots are in `~/.openclaw/workspace/memory/projects/tig-screenshots/`:
- `correct.png` — reference for correct styling
- `bad_header_button.png` — Bug 1
- `bad_modal_button.png` — Bug 2
- `opportunity_page_issues.png` — Bugs 2 + 3

**Natasha must explicitly verify:** white text on dark navy background in BOTH page AND modal/dialog context. Do not pass based on class name alone — visually confirm text is white and readable.
