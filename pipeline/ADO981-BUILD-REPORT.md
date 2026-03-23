# Build Report — ADO #981
**Pipeline: Kanban card click opens side drawer instead of navigating to full page**

---

## Summary
Replaced direct navigation on kanban card click with a MudDrawer quick-view panel. Cards now emit an `EventCallback<Opportunity>` that Pipeline.razor handles by opening a right-side temporary drawer. A "View Full Details" button inside the drawer handles full-page navigation. Avatar background color corrected from `var(--sky)` to `#C0272D` (TIG red).

---

## CC Invocation
```bash
cd ~/projects/fip/famos/src/FamOs.Web
cat /tmp/ado981-brief.md | claude --model sonnet --dangerously-skip-permissions -p
```

---

## Files Changed (3 only)

| File | Change |
|------|--------|
| `Components/Shared/OpportunityCard.razor` | Remove Nav injection + NavigateToOpportunity; add EventCallback\<Opportunity\> OnClick; wire @onclick="OnCardClick"; fix avatar bg #C0272D |
| `Components/Pages/Pipeline.razor` | Wire OnClick="OpenDrawer" on cards; add MudDrawer Anchor.End Temporary 420px; add _selectedOpp/_drawerOpen fields; add OpenDrawer/CloseDrawer/GetStageDisplayName methods |
| `wwwroot/css/famos.css` | Append drawer CSS classes (.famos-drawer-header, .famos-drawer-title, .famos-drawer-stage, .famos-drawer-body, .famos-drawer-section, .famos-drawer-row, .famos-drawer-label, .famos-drawer-value) |

---

## Commit
`b25fe20` — pushed to `origin/main`

---

## Self-Review Checklist

- [x] `OpportunityCard` has `[Parameter] EventCallback<Opportunity> OnClick`
- [x] `OpportunityCard` no longer navigates directly
- [x] `OpportunityCard` avatar bg changed from `var(--sky)` to `#C0272D`
- [x] `Pipeline.razor` has `_selectedOpp` + `_drawerOpen` fields
- [x] `OpenDrawer(Opportunity)` sets both fields
- [x] `CloseDrawer()` clears both fields
- [x] `OpportunityCard` in foreach uses `OnClick="OpenDrawer"`
- [x] MudDrawer `Anchor.End`, `DrawerVariant.Temporary`, `Width="420px"`
- [x] Drawer shows: Name, Stage, Premium, EffDate, AM, Signal, LastStageTransition
- [x] "View Full Details" button navigates and closes drawer
- [x] `GetStageDisplayName` helper exists
- [x] CSS classes added to famos.css
- [x] Only 3 files changed

---

## Acceptance Criteria — Met

1. ✅ Clicking a kanban card opens a 420px right-side MudDrawer (Temporary, Anchor.End)
2. ✅ Drawer displays: opportunity name, lifecycle stage, estimated premium, effective date, account manager, dominant signal chip, last stage transition date
3. ✅ Drawer has X close button (MudIconButton, Icons.Material.Filled.Close)
4. ✅ Drawer closes on ESC key (CloseOnEscapeKey="true")
5. ✅ "View Full Details" button navigates to /opportunity/{id} and closes drawer
6. ✅ Avatar background color corrected to #C0272D (TIG red)
7. ✅ No direct navigation from OpportunityCard (Nav injection removed)

---

*Built by Tony Stark — ADO #981 — 2026-03-20*
