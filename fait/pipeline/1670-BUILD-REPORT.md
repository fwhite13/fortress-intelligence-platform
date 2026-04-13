# Build Report — WI #1670 — Pre-send email confirmation step

**Date:** 2026-04-08
**Commit:** `63d0212`
**Engineer:** Tony Stark (software-engineer)

---

## What was built

Added a mandatory pre-send email confirmation instruction to the m365 system prompt guidance in `ChatView.razor`. Before calling `m365__send_email`, the AI must now present a preview (To, Subject, Message preview) and explicitly ask the user "Shall I send this email? (yes / no)" — waiting for confirmation before proceeding.

---

## Architecture investigation

- **How m365 tool calls are made:** The AI (Claude via Bedrock) calls MCP tools directly — FAIT's `McpToolService.ExecuteToolAsync` executes whatever tools Bedrock decides to call. There is NO middleware intercept point between the AI making a tool_use decision and the tool executing.
- **Only intercept point:** The system prompt. FAIT builds `effectiveSystemPrompt` in `ChatView.razor` `HandleSend`, and the `m365Guidance` block is appended there.
- **Email tool name:** `m365__send_email` (namespaced: `{server.Slug}__{tool.Name}`)
- **Existing guidance:** Already had "Always confirm before sending emails" — but soft language Claude ignored.
- **Option chosen:** **Option A — System prompt instruction** (only viable option for this architecture)

---

## Files changed

- `src/FortressAI.Web/Components/Chat/ChatView.razor` — Added `**CRITICAL — Email sending confirmation (MANDATORY — no exceptions):**` block to `m365Guidance` in `HandleSend`. The new block:
  - States MUST ALWAYS before calling `m365__send_email`
  - Specifies exact preview format: To, Subject, Message preview (first 200 chars)
  - Requires explicit yes/no confirmation
  - Handles rejection ("no / cancel / stop") — do NOT send, offer to edit
  - States "Email sending is irreversible. Never skip this step."
  - Placed BEFORE the existing `**CRITICAL — Email addresses:**` block

---

## CC sessions run

1 CC session (Sonnet) — single-file targeted change. No parallelization needed.

---

## Build result

```
dotnet build — 0 errors, 31 warnings (all pre-existing MudBlazor analyzer warnings, none related to this change)
```

**Commit:** `63d0212`

---

## Acceptance criteria verification

- [x] New block contains "MUST ALWAYS" — ✅ line 756
- [x] Preview format specified (To, Subject, Message preview) — ✅ lines 757-759
- [x] Explicit confirmation required before send — ✅ lines 761-764
- [x] Rejection handling (no/cancel/stop) — ✅ line 762
- [x] "irreversible. Never skip this step." — ✅ line 765
- [x] Existing `{ownEmailBullet}` interpolation preserved intact — ✅ verified
- [x] Build: 0 errors — ✅

---

## Known edge cases / things Clint should scrutinize

1. **AI compliance:** This is a prompt-layer guard, not code-level enforcement. A sufficiently determined user can ask Claude to "skip the preview" and Claude may comply. For a true hard block, Option B (code intercept in the tool execution loop) would be needed — but there's no hook in the current architecture without significant refactoring.

2. **Verbatim string escaping:** The original brief included double-quotes in the preview format (e.g. `"yes / no"`) — CC correctly avoided these inside the `$@"..."` raw string to prevent early string termination. The confirmation prompt uses plain text instead.

3. **"Previously asked to send" edge case:** The instruction explicitly handles the scenario where a user asked "send that" earlier — the AI must still show preview for the specific send. This prevents confirmation bypass via implied prior consent.

---

## How to test locally

1. Connect M365 in FAIT (Settings → Microsoft 365)
2. Open a new chat
3. Say: "Send an email to [name] saying hello"
4. **Expected:** AI shows preview (To, Subject, Message) and asks "Shall I send this email? (yes / no)" BEFORE executing `m365__send_email`
5. Say "no" — expected: AI does NOT send, offers to edit
6. Say "yes" — expected: AI calls `m365__send_email`

---

## Status

Ready for Clint review.
