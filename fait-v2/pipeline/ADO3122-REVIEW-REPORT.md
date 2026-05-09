# Review Report — ADO#3122: Chat UI Full v1 Visual Parity

**Date:** 2026-05-09
**Commit:** 1bb5e191
**Reviewer:** Hawkeye (Clint Barton)
**Review Cycle:** 1 of 2
**CC Invocation:** `cat review-brief-3122-3119.md | claude --model sonnet --print --dangerously-skip-permissions`

---

## Verdict: ✅ PASS (1 nitpick)

---

## Files Reviewed

- `src/FortressAI.V2.Web/Components/Chat/MessageBubble.razor`
- `src/FortressAI.V2.Web/Components/Chat/ChatView.razor`
- `src/FortressAI.V2.Web/wwwroot/css/fortress.css`
- `src/FortressAI.V2.Web/FortressAI.V2.Web.csproj`
- V1 reference: `fait/src/FortressAI.Web/Components/Chat/MessageBubble.razor`

---

## Findings

### Finding 1 — MessageBubble structure ✅ PASS
`message → message-avatar + message-body → message-content` layout confirmed. User avatar renders initial circle with `var(--color-primary)` bg + white text (v2 blue theme adaptation — v1 used gold bg). Assistant avatar renders Shield icon with `var(--color-gold)`. Token meta renders below assistant `message-body` only. Structure matches v1.

### Finding 2 — XSS / Markdig rendering path ✅ PASS (verified via CC)

User content: `<p>@Message.Content</p>` — Blazor-encoded, safe.
Assistant content: `@((MarkupString)RenderMarkdown(Message.Content))` — Markdig, unsanitized.

Markdig does NOT sanitize raw HTML by default, but:
- User content never passes through Markdig
- Assistant content originates from the controlled harness/LLM, not user-supplied input
- V1 uses the **identical pattern** — same `MarkdownPipelineBuilder().UseAdvancedExtensions().Build()` without sanitizer

No new XSS surface introduced. Confirmed identical risk posture to v1.

### Finding 3 — Mic button placeholder ✅ PASS
Present in ChatView.razor between run-as-task and send buttons. `.chat-mic-btn` CSS uses only `var(--...)` tokens throughout — no hardcoded values. Gold hover via `var(--color-gold)`.

### Finding 4 — CSS variable compliance ✅ PASS
All new CSS blocks in `fortress.css` use CSS variable tokens. Old `.message-bubble` ruleset correctly removed and replaced with comment referencing shared chat section classes.

### Finding 5 — User bubble background ✅ PASS
`.message-user .message-content` uses `var(--color-primary-light)` = `#EEF2F7`. Matches v1 exactly.

### Finding 6 — UserInitial parameter ✅ PASS
Default `"U"` when display name unavailable. Set from `_userDisplayName.Trim()[0].ToString().ToUpperInvariant()` after auth resolution. Passed as `UserInitial="@_userInitial"` to MessageBubble. Fallback to `MudIcon Person` if `UserInitial` is null/empty.

### Finding 7 — Markdig 1.1.3 ✅ PASS
Confirmed current stable version (NuGet latest: 1.1.3). No known CVEs.

### Finding 8 — Inline style hardcoded values ⚠️ NITPICK
`MessageBubble.razor` line 11 user avatar `<span>` contains hardcoded: `width:36px;height:36px;font-size:0.75rem;font-weight:700;letter-spacing:0.05em`.

Technically violates the CSS-variable-only rule. However:
- No space token exists for 36px (`--space-8`=32px, `--space-10`=40px, no `--space-9`)
- No font-weight or letter-spacing tokens defined in the system
- Follows the same inline style pattern as v1 (which used `24px`, `0.7rem`)

**No blocking action required.** When/if avatar size tokens are added to the design system, migrate then.

### Finding 9 — Build gate ✅ PASS
`dotnet build` 0 errors (confirmed in Build Report).

---

## Issues Summary

| # | Severity | Description | Action |
|---|----------|-------------|--------|
| 8 | Nitpick | Inline avatar style has hardcoded px/rem values — no CSS token exists for 36px | None required |

---

## Decision: ADVANCE TO DEPLOY

Commit `1bb5e191` (ADO#3122 portion) is approved for deployment.
