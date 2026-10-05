# Zoom roster detection — research notes

**Goal:** collect a participant roster from the Zoom web client (browser bot in `src/bot/zoom.ts`),
camera-agnostic, matching the Teams roster (PR #61) and feeding the same `rosterTimeline`
field of the `recording_complete` callback.

Researched 2026-10-04.

## Sources reviewed

### Recall.ai — "How to build a Zoom bot from scratch"
https://www.recall.ai/blog/how-to-build-a-zoom-bot

Playwright against the Zoom web client:
- Open panel: `page.getByRole("button", { name: "open the participants list" })`
- Rows: `.participants-item__item-layout`
- Name: `.participants-item__display-name`
- Avatar: `.participants-item__avatar`
- List container: `.ReactVirtualized__Grid__innerScrollContainer[role='rowgroup']`

### Recall.ai — "How to join Zoom using Puppeteer"
https://www.recall.ai/blog/how-to-join-zoom-using-puppeteer
- Zoom's web UI churns; prefer role/text selectors and keep fallbacks.

## Decision

`ZoomHandler.pollRoster()` (30s interval + immediate poll after join/recording start):
1. **Participants panel** — opened once via the footer "open the participants list" button
   and **left open**. Rows read via `.participants-item__display-name`.
2. **Video tile name labels** (`.video-avatar__avatar-name` etc.) — last resort.

The Zoom PWA shell (app.zoom.us) hosts the meeting in an iframe, and the existing chat code
found the footer button in the main frame but the chat UI inside the iframe — so every frame
is searched for both the button and the panel.

The bot is excluded via its join name / `BOT_NAME` / `BOT_NAMES_CSV`; role suffixes like
`(Host, me)` are stripped. All failures log and return — polling never throws.

## Open questions / follow-up
- Panel list is `ReactVirtualized`: only rendered rows are in the DOM, so very large meetings
  (more rows than fit in the panel) may be under-counted. Scrolling the list is a possible follow-up.
- Selectors are from public write-ups, not a first-hand DOM capture of our headless Chromium;
  verify against a live meeting via `[Zoom][Roster] N participants via <source>` in CloudWatch.
