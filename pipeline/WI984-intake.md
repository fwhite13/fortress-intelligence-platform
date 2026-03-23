# WI#984 — Team Notes Page

## Type
Feature (new page)

## Source
TIG mock-up (TIG_Portal_v1.html) — Lauren Williams

## Description
A shared Team Notes page for internal communication between TIG team members, visible to all authenticated users. Filterable by account and by team/author.

## Expected Behavior

### Notes list
- Threaded list of notes, most recent first
- Each note: avatar + name + team tag (colored pill) + timestamp + note text
- Two visual styles: TIG notes (light green tint) vs Higg notes (amber tint) — for now, since we're single-role, all notes are "TIG" style
- Long notes show full text (no truncation)

### Filters
- **Account dropdown** — filter to notes linked to a specific account (or "All Accounts")
- **Team filter** — All / TIG Only / Higg Only (for now, just "All" since single role)

### Compose
- Textarea at bottom with "Post" button
- Account association dropdown (optional — can post without linking to an account)
- On post: note appears at top of list immediately

### Note linking
- Notes can be linked to an account/opportunity via the account dropdown
- Linked notes also appear on the account's side panel or detail view

## Notes
- Mock-up: notes tagged with `@AccountName` pattern for linking — can simplify to dropdown for now
- Font sizes / spacing from mock-up: avatar 26px rounded-square, note text 11.5px, timestamp 9.5px
- For now: no edit/delete on posted notes (can add later)
- No real-time push required for Phase 1 — page refresh is fine
