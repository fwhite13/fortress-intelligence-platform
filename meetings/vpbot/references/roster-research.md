# Teams roster detection — research notes

**Problem:** `TeamsHandler.pollRoster()` read `[data-stream-type="Video"][data-tid]` tiles.
Those only exist for participants whose camera is on, so in camera-off meetings (the usual
case) the roster came back empty and transcripts had no speaker attribution.

Researched 2026-10-04.

## Sources reviewed

### 1. Attendee (attendee-labs/attendee, open source) — **in-page call state**
`bots/teams_bot_adapter/teams_chromedriver_payload.js`

- Gets the active call object from the Teams web client's own globals:
  ```js
  if (window.callingDebug?.observableCall) activeCall = window.callingDebug.observableCall;
  if (window.msteamscalling?.deref) {
      const call = window.msteamscalling.deref().callingService.getActiveCall();
      if (call) activeCall = call;
  }
  ```
- `activeCall.participants` = remote participants (`id`, `displayName`, `state`,
  `endpoints`, `meetingRole`); `activeCall.localSignalingParticipant` = the bot.
- Filters out nameless entries and `state === 7` (waiting in lobby).
- Polls this every few seconds; a `rosterUpdate` websocket message (Trouter, `3:::`
  frames) triggers a short fast-poll window. Earlier versions parsed the
  `rosterUpdate` websocket body directly (`decodedBody.participants[*].details.displayName`)
  but they moved to polling the call object.
- **Camera-agnostic, includes participants outside the visible gallery, no UI clicks.**
  Downside: depends on undocumented client internals.

### 2. Vexa (Vexa-ai/vexa, open source) — **tiles + roster panel**
`core/meetings/modules/teams-capture/src/msteams-speakers.ts`, `roster-panel.test.ts`

- Canonical participant surface is `[data-stream-type][data-tid]` — *any* stream type,
  display name in `data-tid`; voice activity via `[data-tid="voice-level-stream-outline"]`.
- Documented failure ("meeting 37"): gallery degraded to one tile, tile walk found nothing
  even though a named participant was in the meeting. Added a roster-panel reader as a
  second surface. Panel selectors: `[data-tid="roster"]`, `[data-tid="people-pane"]`,
  `[data-tid="roster-section"]`, `[data-tid*="participant-list"]`,
  `[role="tree"][aria-label*="articipant"]`; row selectors: `[data-tid="roster-participant"]`,
  `[data-tid*="roster-item"]`, `[data-tid*="participantRosterListItem"]`, `[role="treeitem"]`,
  `[role="listitem"]`. Name falls back to `span[title]` / `aria-label`.
- Vexa deliberately **never opens** the panel, because its code also runs as a browser
  extension in a human's Teams client. For a dedicated bot client the panel only changes
  the bot's own view (not other attendees'), so opening it is acceptable for us.

### 3. ScreenApp meeting-bot (screenappai/meeting-bot, open source)
`src/bots/MicrosoftTeamsBot.ts`

- Only needs a participant *count* (to auto-leave). Scans `[data-tid*="roster" i]`,
  `[aria-label*="people" i]`, `[aria-label*="participant" i]` for text like "3 people",
  plus body text such as "You're the only one in this meeting". No per-name roster.

### 4. Recall.ai blog — "How to build a Microsoft Teams bot" / "When to build a Teams bot with Puppeteer"
- Covers caption scraping only (`[data-tid="closed-caption-renderer-wrapper"]`,
  `span[data-tid="author"]`, `span[data-tid="closed-caption-text"]`). No roster selectors.
- Useful warning: Teams serves **different DOMs for headed vs headless** and per user-agent;
  selectors churn, so multiple fallbacks are needed.

### 5. Microsoft docs
- Official participant APIs (Graph `onlineMeetings` attendance reports, Bot Framework
  `GetParticipantAsync`, Graph calling bots) need a registered Teams app/tenant consent —
  not usable by a browser guest bot. Attendance reports are post-meeting only.

## Decision

`pollRoster()` now tries three sources in order, first one that works wins:

1. **Call state** (Attendee technique) — authoritative; an empty list is trusted as
   "nobody else is here".
2. **Roster panel** — opened once via `[data-tid="roster-button"]` the first time it's
   needed and **left open** (no 30s open/close churn). Audio capture is unaffected by
   panel layout.
3. **Stream tiles of any type** (`[data-stream-type][data-tid]`) — last resort, only covers
   rendered tiles.

The bot itself is excluded via `BOT_NAME` / `BOT_NAMES_CSV`; `(You)`/`(Guest)` suffixes and
email-style names are stripped. Every source is wrapped so failures log and return —
`pollRoster()` never throws. Logs record which source produced the roster
(`[Teams][Roster] N participants via <source>`), so CloudWatch shows which path is live.

## Open questions / follow-up
- Verify against a live meeting which source fires in our headless Chromium + UA.
- Panel row selectors are best-effort (no first-hand DOM capture yet); if call state turns
  out unavailable, capture a DOM snapshot of the open panel and tighten them.

## Links
- https://github.com/attendee-labs/attendee
- https://github.com/Vexa-ai/vexa
- https://github.com/screenappai/meeting-bot
- https://www.recall.ai/blog/how-to-build-a-microsoft-teams-bot
- https://www.recall.ai/blog/puppeteer-microsoft-teams-bot
- https://learn.microsoft.com/en-us/microsoftteams/platform/apps-in-teams-meetings/meeting-apps-apis
