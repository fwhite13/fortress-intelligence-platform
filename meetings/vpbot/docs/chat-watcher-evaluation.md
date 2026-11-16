# Teams chat watcher: side panels, captions and a spoken leave command

WI #9463, 2026-11-16. Part B of the WI: evaluation only, no behaviour change beyond the part A fix.

Labels: **[V]** verified in code (path:line at commit a3f75e3a on `feature/wi-9463`) or in the log lines quoted in the WI. **[I]** inferred (Teams web client behaviour, general knowledge). Nothing here was observed in a live meeting. Paths are relative to `meetings/vpbot/`.

## Summary

- **Root cause of the missed "Notetaker leave" (fixed in part A).** The watcher took `compose.closest('[data-tid*="pane"], …')` as the chat pane. In current Teams that is `chat-pane-compose-message-footer`, so it read the footer's toolbar buttons as 2 "messages" [V: the WI's prod log line; old code at `src/bot/teams.ts` in 8f17841a, `readChatMessages`]. The new resolver walks up from the compose box, skips the footer and returns `source=none` when it finds no list [V: `src/bot/teams.ts:1643` onward].
- **B1: several panes.** Teams has one side-panel slot. The chat pane and the People panel can take it from each other, but the code limits this to **at most one swap per meeting**: the roster opens the People panel once, and the chat watcher takes the slot back within 4 s. The swap can land in the middle of the join notice and make it fail [I]. When call-state works (meeting 174: roster source `call-state`), the People panel only supplements names, so it is close to unneeded.
- **B1: captions while chat is open.** **Yes, the captions keep working.** [I] The captions render in the meeting stage, not in the side panel. If Teams re-creates the caption container, the observer re-attaches within 2 s [V: `src/bot/teams.ts:2518-2519`]. The re-attach is silent, so it can only be confirmed indirectly (see the log lines to check).
- **B2: spoken "Notetaker, leave".** Recommend **option (a)**: match the wake phrase in the page against finalised caption text, using the same whole-utterance regex. Only a boolean leaves the page. Size **S**. Option (b), on-box speech recognition, is not recommended. It competes for the 2 vCPU that the recording needs, and it cannot say who spoke.

---

## B1. Several panes at once

### (i) Chat pane, People panel and the roster poll

**One slot.** The Teams meeting window shows one side panel at a time: Chat, People, Info, Apps and so on. Opening one replaces the other [I: Teams UI behaviour]. The code already assumes this: "WI #8211: chat and People panels share one slot — roster waits for chat to resolve" [V `src/bot/teams.ts:155-156`], and "only one side panel can be open at a time (WI #8211)" [V `:1186-1190`].

**What runs, in order** [V `src/bot/meeting-bot.ts:356-377`]:

1. `postAdmissionChatNotification` runs fire-and-forget. It opens the chat pane through `#chat-button`, or through the More menu at narrow widths. It then waits up to 20 s, polling to 60 s, for the compose box, and posts the join notice [V `teams.ts:1191-1497`].
2. `enableLiveCaptions` (Alt+Shift+C) is awaited [V `meeting-bot.ts:375`, `teams.ts:173-185`].
3. `startRosterPolling` polls once immediately, then every 30 s [V `teams.ts:2017-2028`].
4. `startActiveSpeakerPolling` attaches the caption observer [V `meeting-bot.ts:377`].
5. When step 1 settles (`.finally`) and `CHAT_LEAVE_COMMAND !== '0'`, `startChatCommandWatcher` starts. It runs every 4 s [V `meeting-bot.ts:362-372`, `teams.ts:1509-1512`].

**The roster and the People panel** [V `teams.ts:2069-2096`, `:2290-2312`]:

- `pollRoster` reads call-state first. It calls `readRosterFromPanel` in two cases:
  - call-state returned names: the panel is a supplement (WI #7919, for an organizer missing from `participants`);
  - call-state is unreachable (`null`).
- `readRosterFromPanel` checks whether the panel is visible (1 s probe). If it is not, and no attempt has been made yet, it:
  - returns `null` while the chat notification has not resolved ("Skipping People panel — chat notification still in progress", `:2299`);
  - otherwise sets `rosterPanelOpenAttempted = true` and clicks the People button **once**: "Opened roster panel (left open for remaining polls)" (`:2303-2307`).
- After that one attempt the method never clicks again. When the panel is not visible it returns `null` (`:2312`), and the poll keeps the call-state names.

**The chat watcher** [V `teams.ts:1597-1625`]: `ensureChatPaneOpen` checks whether the compose box is visible. If it is not, and `#chat-button` is not pressed, it clicks the button: "Re-opened chat pane via #chat-button (re-open #n)". The `#n` counter is new in this WI.

**Can they fight and flip the slot?** Only once [V, by the logic above]:

- The roster's single People-panel click takes the slot.
- Within ≤ 4 s the chat watcher re-opens chat, and the People panel closes.
- From then on the roster never clicks again, so there is no loop.
- If a loop ever appeared, the new re-open counter would climb past 1. That is the check.

**A real race remains [I].** `onPanelResolved` fires when the chat pane has been *opened* (`resolvePanel()` at `teams.ts:1301`, before "Chat panel opened, waiting up to 20s for compose box..." at `:1327`). It does not wait until the notice is *posted*. The roster's next 30 s poll can therefore open the People panel while the notice is still waiting for a cold-start compose box, which can take 20-60 s (WI #8064). The notice then fails: "WARNING: could not find chat input field — skipping chat notification" (`:1442`). The watcher starts only after the notice settles, so it cannot win the slot back in time. This can happen only when call-state returned ≥ 1 name in that poll. It is not changed here (outside this WI's files). Follow-up: fire `onPanelResolved` after the post, not after the open, or drop the panel supplement.

**Is the People panel needed?** The WI says meeting 174 showed roster source `call-state`. A `call-state+panel` source appears only when the panel added names (`teams.ts:2086`). So in that meeting the panel either never opened or added nothing [V for the code path, I for meeting 174: CC has no access to its log stream]. When call-state works, the panel matters only for the WI #7919 organizer case. Live check: grep the meeting's log for `Opened roster panel` and `call-state+panel`. If `call-state+panel` never appears across recent prod meetings, the supplement can go in a follow-up, which removes the one swap entirely.

### (ii) Does the caption observer survive the chat pane?

**Where the captions live.**
- The container is `[data-tid="closed-caption-v2-virtual-list-content"]` [V `teams.ts:113`, "confirmed via DevTools 2026-10-12"].
- Live captions render in the meeting stage, as the caption band under the video grid and above the call controls. They are not in the right-hand side panel [I: Teams web layout; the code does not record the container's ancestors].
- Opening, closing or switching the side panel therefore does not remove the captions from the DOM. It does narrow the stage, which may make React re-render the stage subtree [I].

**What the observer does** [V `teams.ts:2408-2533`]:

- It waits up to 30 s for the container, injects one `MutationObserver` and logs "Caption MutationObserver attached".
- It does **not** attach only once. `attach()` runs at once, and a page-side `setInterval` every 2 s re-attaches to whatever element now matches the selector whenever the old container is no longer connected (`:2504-2519`): "Teams may replace the container (e.g. captions toggled); re-attach when it does".
- A re-attach loses at most the captions already on screen at that moment. They are marked finalised and not reported, to avoid wrong timing (`:2509-2514`). The cost is one utterance, a gap under 2 s.

**Limits** [V]:

- The re-attach writes no log line, because it runs inside the page. A re-attach can only be seen as continued caption entries.
- If the old container stays connected but Teams starts writing captions into a second, new container, the observer would not notice [I: unlikely; the virtual list re-uses its root].
- If captions are switched off for the bot and stay off, no container exists. Nothing re-enables them: `enableLiveCaptions` runs once (`meeting-bot.ts:375`).
- Keyboard focus is not a problem. The chat compose box takes focus only after captions are already on (`meeting-bot.ts:375` runs before the watcher starts) [V order; I that focus does not toggle captions].

**Answer: yes**, the caption observer keeps working while the chat pane is open. [I] The captions are in the stage. [V] A container replacement is handled within 2 s.

**Log lines to check in one dev meeting** (CloudWatch `/ecs/firm-vpbot`):

1. Captions on:
   - `[Teams][Captions] Caption container appeared`
   - `[Teams][ActiveSpeaker] Caption MutationObserver attached`
2. The chat pane is open, and the chat fix works:
   - `[Teams] ✅ Chat notification posted`
   - `[Teams][ChatCmd] First chat read: N message(s); container=…; selector=…; source=…`, with N ≥ 1 (the join notice) and the container a message list rather than `chat-pane-compose-message-footer`.
3. Slot swaps:
   - `[Teams][Roster] Opened roster panel` (0 or 1)
   - `[Teams][ChatCmd] Re-opened chat pane via #chat-button (re-open #n)`: n should stop at 1.
4. Captions survive with chat open:
   - `[Teams][ActiveSpeaker] Caption entry #1: …`, then `#50`, `#100` … continuing **after** the timestamps of the lines in 2 and 3.
   - At the end: `[Teams][ActiveSpeaker] Capture stopped (U utterances, E entries, source: captions)`, with U roughly in line with the minutes of speech.
   - The meeting's speaker timeline in FIRM should cover the whole meeting, not stop at the moment the chat pane opened.
5. If the chat list is not found: `[Teams][ChatCmd] No chat messages found in 5 consecutive polls — one-time side-panel structure dump …`. Send that line back. It holds the DOM data for the next one-line selector fix.

---

## B2. Spoken "Notetaker, leave"

### Facts in the WI, checked

| Claim | Status |
|---|---|
| The observer reads caption text inside the page; only `{name, startMs, endMs, revision}` leaves it (WI #8877 privacy choice) | **Correct** [V `teams.ts:2392-2396` doc comment; `:2461-2463` builds `utterance = {name, startMs, endMs, revision}` and calls the binding; the text stays in the page-side `states` map]. Note: the speaker's display **name** does leave the page and is logged on caption entry #1 and every 50th entry (`:2551`). |
| An utterance is finalised after `CAPTION_SETTLE_MS` or when a newer caption element appears | **Correct** [V `:117` (300 ms), `:2472-2481`]. Correction: the text can change again after finalisation. Teams rewrites recent captions as recognition improves, and that is reported as `revision: true` (`:2453-2461`). |
| Consecutive same-speaker utterances are merged | **Correct, but in Node, not in the page** [V `handleCaptionUtterance`, `:2554-2557`]. The page reports each utterance separately. That matters for option (a): the page sees each caption element's own text, which is exactly the whole-utterance unit that is wanted. |

### Options

**(a) Match the wake phrase in the page, against finalised caption text (recommended).**

- Pass `buildLeaveCommandRegex(botName).source` into the observer's `page.evaluate`, so there is one regex for chat and speech [V `src/bot/chat-commands.ts:48-56`].
- In `finalize()`, revisions included, normalise the text and test it with the same whole-utterance rule as `isLeaveCommand`: collapse whitespace and allow at most 120 characters [V `chat-commands.ts:59-62`].
- On a match, call a second binding, `__firmOnCaptionCommand({ command: 'leave', speaker })`. Caption text never leaves the page. The speaker name already leaves today.
- In Node, reuse the chat path: the same `_chatCommandFired` single-fire guard, the same acknowledgement posted to chat (say "at <speaker>'s spoken request"), then `onLeave`.
- Run the test **before** the `reportedKeys` de-duplication in `finalize()` [V `teams.ts:2454-2459`]. That check drops a new utterance whose speaker and text were already reported, so a user repeating "Notetaker, leave" word for word would otherwise be ignored. The single-fire guard already prevents a double leave.
- Cost: a regex test per finalised caption, negligible. No new dependencies.

**(b) Speech recognition on the recorded audio in the container (not recommended).**

- **Fit with ffmpeg:** ffmpeg already reads `virtual_out.monitor` at 16 kHz mono into the WAV [V `src/bot/meeting-bot.ts:457-468`]. A second output (`-f s16le pipe:1`) or a second `parec` reader could feed a recogniser.
- **CPU:** the task has 2 vCPU / 4 GB (firm-vpbot:91) [V comment in `firm/src/FortressIntelligenceRM.Web/Services/VoiceSampleProcessingService.cs:26`]. Captures made while CPU-starved were choppy, at a dropout rate of about 370 against 0-5 when clean [V `firm/src/FortressIntelligenceRM.Web/Models/FirmMeeting.cs:77`]. Headless Chromium plus WebRTC already uses most of that. A continuous recogniser would add a steady load [I]: Vosk small-en in grammar or keyword mode is roughly 0.2-0.5 vCPU, and whisper.cpp tiny/base in 2-3 s windows is roughly 0.5-1 vCPU. That risks the recording, which is the product.
- **Latency** would be about 1-3 s [I], no better than captions.
- **Other costs:** the image grows by 50-200 MB of model files. Mixed audio carries no speaker, so the bot cannot say who asked. A recogniser would also need its own tuning for "notetaker".
- Use (b) only if captions turn out to be unavailable in most customer tenants.

**(c) Other options considered.**

- **(c1) FIRM "Stop bot" button.** The vpbot already has `POST /api/meetings/:id/stop` [V `src/index.ts:475`]. A FIRM web button needs no speech at all, but only FIRM users can press it, not guests in the meeting. It is worth having anyway, and it is a separate WI.
- **(c2) Raise-hand or reaction as a signal.** Too ambiguous. Rejected.
- **(c3) Graph transcript or meeting events.** These need tenant app permissions, and Mode A is mothballed. Rejected.

### Recommended option (a), in detail

- **How captions write "Notetaker" [I].**
  - Expected spellings: "Notetaker", "note taker", "Note taker", sometimes "note-taker", and mishearings such as "No taker", "Note maker", "Nate Taker".
  - `botAliases` already covers `notetaker`, `note taker`, the full bot name (`fortress notetaker`) and its first word when ≥ 4 letters (`fortress`) [V `chat-commands.ts:34-45`].
  - Not covered: `note-taker`. A spoken path should normalise `note[\s-]+taker` to `notetaker` before testing; that is a page-side preprocessing step, and the regex stays unchanged.
  - Captions add capitals and a full stop ("Notetaker, leave."). The regex already allows an optional comma or colon after the name and trailing `.!?`, and it is case-insensitive [V `chat-commands.ts:51-55`].
  - "Fortress Notetaker, stop recording." matches through the full-name alias.
  - Mishearings will not match, so the user repeats the command. That is the safe failure.
- **Latency [I].**
  - Teams caption latency is about 1-2 s.
  - Settling adds 300 ms, or the next caption element appears first [V `:117`].
  - Posting the acknowledgement takes about 1-3 s.
  - The leave itself is about 2-5 s.
  - Expect the bot to be gone about 5-10 s after the words are spoken, comparable with the typed command.
- **Captions off or blocked.**
  - The bot turns captions on itself with Alt+Shift+C and logs whether the container appeared [V `teams.ts:173-185`].
  - When a host or tenant policy disables live captions, or the shortcut fails, no container appears and "Caption container not found" is logged [V `:2419`]. Then the spoken command silently does not exist.
  - The typed chat command still works. The follow-up should log once that the spoken command is unavailable in that meeting.
  - Captions follow the meeting's spoken language. In a non-English meeting the phrase will not be transcribed as English [I].
- **False positives.**
  - The whole-utterance rule is anchored with `^…$`, so "we should tell the notetaker to leave" does not match [V regex shape `chat-commands.ts:52-55`].
  - The live risks are someone saying exactly "Notetaker, stop" as a command, and a caption element that holds only that phrase after a pause. Both are deliberate commands.
  - The opposite case is a false negative: Teams may put "…that's it. Notetaker, leave." in one caption element when nobody pauses, and that will not match. The user pauses and repeats.
  - A possible later relaxation is to also test the final sentence of an utterance. Do not do it in v1.
  - The bot cannot hear itself: it joins with its microphone off (`turnOffDevices`, `teams.ts:969`) and never speaks.
- **Who may issue it.** Any participant, including external guests, exactly like the typed command. The speaker's caption name goes into the acknowledgement and the log line. There is no host-only rule, because the bot cannot reliably tell the organizer from captions.
- **Confirmation.** No interactive confirmation. The typed command has none, and a confirm round trip needs a second command and its own state. The acknowledgement is posted to chat *before* leaving. A misfire therefore needs the exact command phrase on its own in a caption, and the cost is an early stop of the recording. Accept that for v1. Revisit only if dev QA shows misfires.
- **Kill switch.** A separate env var, `SPOKEN_LEAVE_COMMAND=0`, mirroring `CHAT_LEAVE_COMMAND=0` (`meeting-bot.ts:364`). `CHAT_LEAVE_COMMAND=0` should not disable the spoken path, and the spoken switch should not disable chat. The vpbot image goes live for dev and prod at once, so Jarvis can set `SPOKEN_LEAVE_COMMAND=0` on the prod task definition until dev QA passes.
- **Size: S.** About 40-60 lines across `teams.ts` (observer, binding, Node handler) and `meeting-bot.ts` (wiring and env var). No new dependencies.

### Draft follow-up WI

**Title:** Bot: spoken "Notetaker, leave" via live captions (in-page wake-phrase match, text never leaves the page) (S)

**Description:**

- In the WI #8877 caption observer (`meetings/vpbot/src/bot/teams.ts`, `attachCaptionObserver`), test every finalised caption utterance, revisions included, against the leave-command regex from `chat-commands.ts`.
  - Pass in `buildLeaveCommandRegex(botName).source`.
  - Normalise whitespace and `note[\s-]+taker` → `notetaker`.
  - Apply the 120-character cap.
  - Test before the `reportedKeys` de-duplication, so a repeated command still counts.
- On a match, call a new page binding with `{command: 'leave', speaker}` only. Caption text never leaves the page.
- In Node, share the single-fire guard with the chat command. Post the existing acknowledgement, worded as a spoken request, then stop with reason `spoken-leave-request`.
- Log, without caption text:
  - `[Teams][VoiceCmd] Leave command spoken by <speaker>`
  - once per meeting, `[Teams][VoiceCmd] Spoken leave unavailable — captions not running` when the caption container never appeared.
- Kill switch `SPOKEN_LEAVE_COMMAND=0`, independent of `CHAT_LEAVE_COMMAND`.
- No firm/ changes. No image build until Jarvis ships it in a quiet window.

**goalCondition:**
> "vpbot TypeScript compiles with 0 errors; the caption observer tests each finalised caption utterance (including revisions) in the page with the same whole-utterance leave regex as the chat command (built from buildLeaveCommandRegex, with note-taker normalised) and sends only {command, speaker} out of the page — no caption text crosses the page boundary or reaches a log; a match posts the existing acknowledgement and stops the bot through the same single-fire guard as the chat command; SPOKEN_LEAVE_COMMAND=0 disables only the spoken path and CHAT_LEAVE_COMMAND=0 only the chat path; a meeting without captions logs one 'spoken leave unavailable' line; no firm/ files changed; branch pushed to github; no more than 40 turns."

**Dev QA:**
- Say "Notetaker, leave" alone, after a pause. The bot acknowledges and leaves within about 10 s.
- Say "we should tell the notetaker to leave" in a sentence. Nothing happens.
- Set `SPOKEN_LEAVE_COMMAND=0`. Speaking does nothing, and typing still works.

---

## What Fred must verify live (dev)

1. **Typed command.** Type "Notetaker leave" and, in a second meeting, "Notetaker, leave". Expected:
   - an acknowledgement in chat;
   - the bot leaves within about 8 s;
   - the meeting goes to Transcribing;
   - the log has `Leave command received`.
2. **List found.** `First chat read` shows a count of 1 or more. Its container is the message list (expected something like `message-pane-list-*`, `role=list` or `role=log`), not `chat-pane-compose-message-footer`. The selector is a message selector, or `pane-lines` from a list.
3. **If the list was not found.** The one-time structure dump line appears after about 20 s of empty polls. Send it back: it is enough to name the right selector.
4. **B1.** With the chat pane open, speak for a minute. Caption entries keep coming (`Caption entry #…` and the end-of-meeting `Capture stopped (…)` counts), and the FIRM speaker timeline has entries across the meeting. Check that `re-open #n` stays at ≤ 1, and whether `Opened roster panel` and `call-state+panel` appear.
