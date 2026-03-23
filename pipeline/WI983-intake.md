# WI#983 — Pipeline: Kanban Stale Deal Indicators

## Type
Feature / UX

## Source
TIG mock-up (TIG_Portal_v1.html) — Lauren Williams

## Description
Kanban cards on the Pipeline page should visually indicate stale deals — accounts with no stage movement for an extended period.

## Expected Behavior

### Card Stale States
- **Warn** (amber): account stale 14–20 days — amber left border + amber background tint + "STALE" badge on card footer
- **Urgent** (red): account stale 21+ days or renewal within 60 days — red left border + red background tint + "URGENT" badge

### Badge appearance
- Small pill badge: "14d stale" or "URGENT" in appropriate color
- Positioned in card footer alongside existing premium/status info

### Stale calculation
- Based on `updated_at` or last stage movement timestamp on the opportunity record
- If no `last_stage_moved_at` field exists, use `updated_at` as proxy

## Notes
- Mock-up CSS: `.kcard.stale { border-color: var(--amber); border-left: 3px solid var(--amber); background: #fffdf5 }`
- Mock-up CSS: `.kcard.stale-urgent { border-color: var(--red); border-left: 3px solid var(--red); background: #fff5f5 }`
- Stale flags: `.stale-flag.warn { background:#fef3c7;color:#92400e }` / `.stale-flag.urgent { background:#fee2e2;color:#991b1b }`
- If DB doesn't have a last_stage_moved_at column, add one (set to created_at for existing records)
