# WI#872 — FAM OS Sprint 3: UI/UX Restyling

**Priority:** 2 (High)
**Tags:** famos; ui; sprint3; restyling; mudblazor
**Spec:** ~/projects/fip/famos/FAMOS-SPRINT3-SPEC.md

## Objective
Restyle FAM OS to match Lauren's mockup (`IAAPA_Portal_v2_restyled.html`). Zero functional changes — purely visual/CSS/theme work.

## Key Changes (from spec)
1. **Theme:** Replace `FipTheme.cs` with new MudTheme using `#002050` navy + `#0090d0` sky-blue + `#f2f4f7` cream background
2. **Fonts:** Inter → Plus Jakarta Sans (body) + Fraunces serif (headings, KPI values) via Google Fonts
3. **Layout:** MainLayout sidebar 240px→262px, topbar 48px→54px, content padding 16px→24px/28px
4. **New component:** `StatCard.razor` — KPI card with left-accent-bar (4px colored border-left, Fraunces number, uppercase label)
5. **NavMenu:** Custom pill nav with `#002050` bg, sky-blue active border-left, section labels (uppercase, letter-spacing)
6. **Pipeline board:** Column headers → white cards with colored dot + label + count badge
7. **Kanban cards:** Lightweight div wrappers replacing MudCard, hover sky-blue border
8. **Status chips:** 20px border-radius pill style
9. **Buttons:** 7px radius, 1.5px borders, no text-transform
10. **famos.css:** Full replacement (complete file provided in spec Section 7)

## Files to Touch (from spec)
- `FamOs.Web/Theme/FipTheme.cs` — new MudTheme
- `FamOs.Web/Layout/MainLayout.razor` + `.razor.css`
- `FamOs.Web/Layout/NavMenu.razor` + `.razor.css`
- `FamOs.Web/Pages/Dashboard.razor` + `.razor.css`
- `FamOs.Web/Pages/Pipeline.razor` + `.razor.css`
- `FamOs.Web/Pages/OpportunityWorkspace.razor` + `.razor.css`
- `FamOs.Web/Components/StatCard.razor` (NEW)
- `FamOs.Web/wwwroot/css/famos.css` (full replacement)
- `FamOs.Web/App.razor` (add Google Fonts link)

## Build
- Monorepo: `~/projects/fip/`
- CodeBuild project: `fip-famos-build`
- ECR repo: `famos-web`
- ECS service: `famos-dev` (dev only per standing rule)

## Acceptance Criteria
See spec Section 9 (20-item checklist). Key items:
- Sidebar is `#002050` navy, not `#1a2332`
- Page bg is cream (`#f2f4f7`), not white
- KPI numbers render in Fraunces serif
- Pipeline column headers show colored dot + label + count badge
- No functional regressions (Pipeline, OpportunityWorkspace, dialogs all work)

## Dev Notes
- Do NOT touch any C# business logic, EF Core models, or API endpoints
- Do NOT change any page routing or component structure
- MudBlazor v7 only — no additional component libraries
- Read full spec before starting; the CSS file in Section 7 is ready to drop in
