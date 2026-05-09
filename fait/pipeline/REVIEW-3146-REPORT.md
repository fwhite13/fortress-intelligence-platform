# Review Report — ADO#3146: Session Resumption Brief

### Verdict: NEEDS-CHANGES

---

## CC Review Summary

Ran CC (Sonnet) against both changed files with adversarial brief. CC read all 21 checklist items plus discovered 3 off-checklist issues. 17 of 21 checklist items PASS. 

Real issues confirmed: 1 Important bug (S3 key path divergence), 1 Important race (send button enabled during brief streaming), 1 Important UX defect (markdown not rendered). 2 Nitpick CSS violations. 1 off-checklist efficiency concern (minor).

No critical blockers. No security issues. Spec compliance is otherwise solid.

---

## Spec Compliance Check

**§2 Files Modified:**
- `fait-v2/agent-harness/harness-server.js` — ✅ modified as specified
- `fait/src/FortressAI.Web/Components/Chat/ChatView.razor` — ✅ modified as specified

**§6 Out of Scope:**
- Pipeline docs (BUILD/REVIEW/DEPLOY for 3145) committed in same commit — these are pipeline artifacts, not code changes. ✅ Acceptable.

**§7 Acceptance Criteria:**
- [x] `__resumption_brief__` trigger handler in harness — ✅ present, correct placement
- [x] `HeadObjectCommand` for MEMORY.md timestamp — ✅ present, non-fatal error handling — ❌ **wrong S3 key path (see C1)**
- [x] Last topic extraction from history — ✅ correct, handles case variants, 80-char truncation
- [x] Fallback text "Ready when you are." — ✅ present
- [x] Cold-start only (`_wasColdStart` in `HandleAgentReady`) — ✅ correct
- [x] Warm-start path never fires brief — ✅ correct
- [x] `_resumptionBriefSent` guard — ✅ correct, set synchronously before await
- [x] CSS variable compliance — ❌ **two raw values (see I2/I3)**
- [x] `node --check` passes — ✅
- [x] `dotnet build` 0 errors — ✅

**Spec compliance verdict:** ⚠️ PARTIAL — Spec compliant in structure; three behavioral/quality issues require fixes before PASS.

---

## Consistency Audit

**Files cross-referenced:**
- `harness-server.js` line 1101 (`users/${userId}/MEMORY.md`) ↔ lines 1162–1166, 1292–1297 (`workspaces/${userId}/memory/MEMORY.md`) — ❌ **PATH MISMATCH (see C1)**
- `harness-server.js` `sendEvent({ type: 'done', exitCode: 0 })` ↔ `ChatView.razor` `evt.Type is "done" or "error"` break — ✅ match
- `harness-server.js` `__brief_start__` sentinel ↔ `ChatView.razor` `content.Contains("__brief_start__")` strip — ✅ match
- `harness-server.js` `message === '__resumption_brief__'` ↔ `ChatView.razor` `Message: "__resumption_brief__"` — ✅ exact match
- Send button `Disabled` predicate ↔ `_isBriefStreaming` — ❌ **NOT INCLUDED (see C2)**

---

## Issues Found

| Severity | File | Line | Issue | Fix |
|----------|------|------|-------|-----|
| **Important** | `harness-server.js` | 1101 | S3 key path diverges from rest of harness — MEMORY.md never found | See C1 below |
| **Important** | `ChatView.razor` | 224, 235, 240, 249, 253 | Send button not gated on `_isBriefStreaming` — race condition with `HandleSend` | See C2 below |
| **Important** | `harness-server.js` L1121/`ChatView.razor` L80 | — | Markdown not rendered — `**bold**` displayed as literal asterisks | See C3 below |
| Nitpick | `ChatView.razor` | 1355 | `border-left: 3px solid` — hardcoded pixel value, no CSS var | `var(--border-width-accent, 3px)` |
| Nitpick | `ChatView.razor` | 1362 | `line-height: 1.5` — no CSS var | `var(--line-height-normal, 1.5)` |

---

## Critical / Important Details

### C1: S3 key path mismatch — MEMORY.md never found

**File:** `harness-server.js` line 1101  
**Category:** Correctness / consistency  
**Issue:** The resumption brief uses `${S3_PREFIX}users/${userId}/MEMORY.md` to HEAD MEMORY.md. Every other MEMORY.md read in the harness uses `${S3_PREFIX || ''}workspaces/${userId}/memory/MEMORY.md` (lines 1162–1166 CC path, 1292–1297 Bedrock path). These are different S3 keys. The HEAD will always 404, the catch suppresses it silently, and `memoryTimestamp` remains `null` forever. The "Memory synced: [date]" line in the brief will never appear.

**Evidence:**
```js
// Line 1101 — resumption brief:
const memKey = `${S3_PREFIX}users/${userId}/MEMORY.md`;

// Line 1162–1166 — CC/Bedrock task path:
const prefix = S3_PREFIX || `workspaces/${userId}/`;
fetchS3File(`${prefix}memory/MEMORY.md`)   // → workspaces/{userId}/memory/MEMORY.md
```

**Fix:**
```diff
- const memKey = `${S3_PREFIX}users/${userId}/MEMORY.md`;
+ const prefix = S3_PREFIX || `workspaces/${userId}/`;
+ const memKey = `${prefix}memory/MEMORY.md`;
```

---

### C2: Race condition — send button enabled during brief streaming

**File:** `ChatView.razor` lines 224, 235, 240, 249, 253  
**Category:** Correctness / race condition  
**Issue:** `SendResumptionBrief` sets `_isBriefStreaming = true` during streaming but never touches `isStreaming`. All five `Disabled` predicates on the send button, attach button, and task toggle gate only on `isStreaming`. A user can click Send while the brief is streaming, firing `HandleSend` concurrently, resulting in two simultaneous `SendTurnAsync` calls to the harness for the same userId. The harness has no per-user concurrency guard.

**Fix (minimal):** Add `_isBriefStreaming` to all `Disabled` predicates:
```diff
- Disabled="@isStreaming"
+ Disabled="@(isStreaming || _isBriefStreaming)"
```
Apply to lines 224, 235, 240, 249. For line 253:
```diff
- Disabled="@(isStreaming || (string.IsNullOrWhiteSpace(_userInput) && !_pendingAttachments.Any()))"
+ Disabled="@(isStreaming || _isBriefStreaming || (string.IsNullOrWhiteSpace(_userInput) && !_pendingAttachments.Any()))"
```

---

### C3: Markdown rendered as raw text in brief card

**Files:** `harness-server.js` lines 1121–1123, `ChatView.razor` line 80  
**Category:** UX correctness  
**Issue:** The harness sends `**Picking up where we left off**` and `*topic*` as markdown syntax. The client renders `_briefContent.ToString()` inside a plain `<div>` with no markdown pipeline. Users see literal asterisks.

**Fix (server-side, simpler):** Strip markdown from the brief strings in harness-server.js:
```diff
- briefParts.push('**Picking up where we left off**\n\n');
+ briefParts.push('Picking up where we left off\n\n');
  if (lastTopic) {
-     briefParts.push(`Last time: *${lastTopic}*\n\n`);
+     briefParts.push(`Last time: ${lastTopic}\n\n`);
  }
```

**Fix (client-side, consistent with rest of app):** Pipe `_briefContent` through the existing markdown renderer before inserting into `MarkupString`, matching how `MessageBubble` renders message content.

Server-side fix is simpler and preferred unless the existing markdown renderer is already used for message bubbles and can be trivially reused.

---

## Spec Fidelity

The implementation correctly meets the structural spec: handler in harness, cold-start-only trigger, resumption brief card, CSS. The feature *works* end-to-end in happy path. Three fixes needed:

1. S3 key → "Memory synced" date never appears without the fix
2. Race guard → theoretical concurrent call risk on fast users
3. Markdown → cosmetic correctness issue

None require architectural changes. All are one-line or two-line fixes.

---

## What to fix (NEEDS-CHANGES)

Tony: three specific fixes, all small:

1. **`harness-server.js` line 1101** — Change MEMORY.md key from `users/${userId}/MEMORY.md` to `${S3_PREFIX || ''}workspaces/${userId}/memory/MEMORY.md` (match the pattern at lines 1162/1292).

2. **`ChatView.razor` lines 224, 235, 240, 249, 253** — Add `|| _isBriefStreaming` to every `Disabled` predicate that currently gates on `isStreaming` alone. Five occurrences.

3. **Either** strip markdown from `harness-server.js` brief strings (remove `**` and `*` wrappers, lines 1121–1123) **or** run `_briefContent` through the existing markdown renderer before display. Server-side removal is simpler.

Nitpicks (optional, will not block PASS if left):
- `border-left: 3px` → use CSS var
- `line-height: 1.5` → use CSS var

---

_Hawkeye — 2026-05-09_
