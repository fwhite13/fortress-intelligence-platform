# Tony Stark Build Brief — ADO#3117 + ADO#3118

## Working Directory
`/home/fredw/projects/fip/fait-v2`

---

## Bug 1 — ADO#3117: Chat UI styling does not match FAIT v1

### Context
FAIT v2 ChatView.razor uses specific CSS classes that are either undefined in fortress.css or use
hardcoded non-CSS-variable values. This causes visual inconsistency vs FAIT v1.

### Issues Found (from code analysis)

**A) `MessageBubble.razor` has wrong structure vs v1**

v1 MessageBubble uses:
```
<div class="message message-user | message-assistant">
  <div class="message-avatar">...</div>
  <div class="message-body">
    <div class="message-content">...</div>
    <div class="message-meta">...</div>
  </div>
</div>
```

v2 MessageBubble.razor uses:
```
<div class="message-bubble message-user | message-assistant">
  <div class="message-content">...</div>
  <div class="message-meta">...</div>
</div>
```

The v2 CSS in fortress.css has the v1 `.message`, `.message-body`, `.message-avatar` classes defined,
BUT v2 MessageBubble.razor uses `.message-bubble` as the wrapper (no avatar, no body wrapper).
The `.message-bubble` CSS entry at line ~2407 only has `display: flex; flex-direction: column; margin-bottom: ...`
without the full v1 styling.

The `.message-user .message-content` and `.message-assistant .message-content` CSS rules reference
`.message-user` and `.message-assistant` as selectors — those classes ARE on the v2 bubble wrapper,
so those rules DO apply. But the user bubble background/border-radius styling should work.

**B) Hardcoded colors in ChatView.razor that must use CSS variables**

In ChatView.razor `<style>` block, `.chat-run-as-task-btn` uses hardcoded values:
- `#444` → should be `var(--color-border)` 
- `#999` → should be `var(--color-text-muted)`
- `#7c83ff` → should be `var(--color-accent)` (or `var(--color-primary)`)

**C) Missing CSS definitions in fortress.css for v2-specific chat classes**

ChatView.razor uses these classes that have NO definition in fortress.css:
- `.chat-input-bar` — the sticky bottom input bar wrapper
- `.chat-input-top-row` — KB pills row above input
- `.chat-input-bottom-row` — textarea + send button row  
- `.chat-input-field` — the textarea
- `.chat-send-btn` — the send button
- `.chat-empty-state` — empty/loading state centered container
- `.chat-empty-title` — h-tag in empty state
- `.chat-empty-subtitle` — subtitle in empty state
- `.chat-streaming-indicator` — streaming text area
- `.chat-streaming-text` — streaming content
- `.chat-streaming-cursor` — blinking cursor
- `.chat-artifact-progress` — CC artifact progress bar wrapper
- `.chat-artifact-progress-step` — step text
- `.chat-artifact-cancel-btn` — cancel button
- `.chat-pill-icon` — icon size in pills (used in kb pills, agent pills)
- `.chat-pill-label` — label text in pills

These are defined inline in the ChatView.razor `<style>` block partially for some (agent pills, kb pills),
but the critical structural ones (input bar, empty state, streaming) are NOT defined anywhere.

**D) Message bubble needs streaming/active state styling**

v1 has `.message.streaming` class — v2 doesn't use that but uses a separate `.chat-streaming-indicator` div.
This is fine but needs proper CSS.

### Fix Plan

**Step 1: Fix hardcoded colors in ChatView.razor `<style>` block**

Replace the `.chat-run-as-task-btn` hardcoded values:
```css
/* BEFORE */
.chat-run-as-task-btn {
    background: transparent;
    border: 1px solid var(--chat-border-color, #444);
    color: var(--chat-text-muted, #999);
    ...
}
.chat-run-as-task-btn:hover:not(:disabled) {
    color: var(--chat-accent-color, #7c83ff);
    border-color: var(--chat-accent-color, #7c83ff);
}

/* AFTER */
.chat-run-as-task-btn {
    background: transparent;
    border: 1px solid var(--color-border);
    color: var(--color-text-muted);
    ...
}
.chat-run-as-task-btn:hover:not(:disabled) {
    color: var(--color-accent);
    border-color: var(--color-accent);
}
```

**Step 2: Add missing chat CSS classes to fortress.css**

Add the following to the `/* ============ CHAT ============ */` section in fortress.css
(after the existing `.chat-container` definition, before or after the existing chat styles):

```css
/* === FAIT v2 Chat — Structural classes === */

.chat-input-bar {
    display: flex;
    flex-direction: column;
    background: var(--color-surface);
    border-top: 1px solid var(--color-border);
    padding: var(--space-2) 0 var(--space-3);
    flex-shrink: 0;
}

.chat-input-top-row {
    display: flex;
    align-items: center;
    padding: var(--space-1) var(--space-4);
    gap: var(--space-2);
}

.chat-input-bottom-row {
    display: flex;
    align-items: flex-end;
    gap: var(--space-2);
    padding: var(--space-2) var(--space-4) 0;
}

.chat-input-field {
    flex: 1;
    border: 1px solid var(--color-border);
    border-radius: var(--radius-lg);
    padding: var(--space-2) var(--space-3);
    font-family: var(--font-primary);
    font-size: var(--text-base);
    line-height: 1.5;
    resize: none;
    background: var(--color-surface);
    color: var(--color-text-primary);
    transition: border-color var(--transition-fast);
    min-height: 40px;
    max-height: 200px;
}

.chat-input-field:focus {
    outline: none;
    border-color: var(--color-accent);
    box-shadow: 0 0 0 3px var(--color-gold-muted);
}

.chat-input-field::placeholder {
    color: var(--color-text-muted);
}

.chat-input-field:disabled {
    opacity: 0.6;
    cursor: not-allowed;
}

.chat-send-btn {
    width: 40px;
    height: 40px;
    border: none;
    border-radius: var(--radius-md);
    background: var(--color-primary);
    color: var(--color-text-on-primary);
    cursor: pointer;
    display: flex;
    align-items: center;
    justify-content: center;
    flex-shrink: 0;
    transition: background var(--transition-fast);
}

.chat-send-btn:hover:not(:disabled) {
    background: var(--color-primary-hover);
}

.chat-send-btn:disabled {
    opacity: 0.4;
    cursor: not-allowed;
}

.chat-send-icon {
    font-size: 1rem;
}

/* Empty / loading states */
.chat-empty-state {
    display: flex;
    flex-direction: column;
    align-items: center;
    justify-content: center;
    flex: 1;
    text-align: center;
    padding: 3rem 2rem;
    color: var(--color-text-muted);
}

.chat-empty-title {
    font-size: var(--text-lg);
    font-weight: var(--font-semibold);
    color: var(--color-text-primary);
    margin: var(--space-3) 0 var(--space-2);
}

.chat-empty-subtitle {
    font-size: var(--text-sm);
    color: var(--color-text-secondary);
    max-width: 480px;
}

/* Streaming indicator */
.chat-streaming-indicator {
    display: flex;
    align-items: baseline;
    gap: var(--space-1);
    padding: var(--space-1) 0;
    color: var(--color-text-primary);
    font-size: var(--text-base);
    line-height: 1.65;
    max-width: 900px;
    width: 100%;
    margin: 0 auto;
}

.chat-streaming-text {
    white-space: pre-wrap;
    word-break: break-word;
}

.chat-streaming-cursor {
    animation: blink 1s infinite;
    color: var(--color-accent);
    font-weight: 300;
}

/* Artifact / CC progress */
.chat-artifact-progress {
    display: flex;
    align-items: center;
    gap: var(--space-3);
    padding: var(--space-2) var(--space-4);
    background: var(--color-surface-sunken);
    border-radius: var(--radius-md);
    max-width: 900px;
    margin: 0 auto;
    width: 100%;
}

.chat-artifact-progress-step {
    flex: 1;
    font-size: var(--text-sm);
    color: var(--color-text-secondary);
}

.chat-artifact-cancel-btn {
    font-size: var(--text-xs);
    color: var(--color-text-muted);
    flex-shrink: 0;
}

/* Pill icon + label sizing */
.chat-pill-icon {
    font-size: 1rem !important;
    width: 16px !important;
    height: 16px !important;
}

.chat-pill-label {
    font-size: var(--text-sm);
    white-space: nowrap;
}
```

**Step 3: Align message bubble CSS in fortress.css**

The `.message-bubble` class in fortress.css (around line 2407) needs full v1-parity styling
for user vs assistant messages. Update it:

```css
/* Message bubbles — match FAIT v1 */
.message-bubble {
    display: flex;
    flex-direction: column;
    margin-bottom: var(--space-4);
    max-width: 900px;
    width: 100%;
    margin-left: auto;
    margin-right: auto;
}

.message-bubble .message-content {
    font-size: var(--text-base);
    line-height: 1.65;
}

.message-bubble.message-user .message-content {
    background: var(--color-primary-light);
    padding: var(--space-3) var(--space-4);
    border-radius: var(--radius-lg);
    border-top-left-radius: var(--radius-sm);
    align-self: flex-end;
    max-width: 80%;
}

.message-bubble.message-assistant .message-content {
    padding: var(--space-1) 0;
}

.message-bubble .message-meta {
    display: flex;
    align-items: center;
    gap: var(--space-3);
    margin-top: var(--space-2);
    font-size: var(--text-xs);
    color: var(--color-text-muted);
}
```

---

## Bug 2 — ADO#3118: KB management panel not showing user's knowledge bases

### Root Cause
Investigation findings:
1. Fred's user record EXISTS in `users` table with id=`8ead3439-8d9f-40af-b2f7-0c1305e41859`, entra_oid=`d7d94e9e-cd35-479a-bad2-e162d40b52c1` — user resolution IS working correctly
2. `kb_entries` table has 0 rows total — Fred has no KB entries to show
3. S3 bucket `fortress-tools/kb-docs/personal/8ead3439-8d9f-40af-b2f7-0c1305e41859/` does NOT exist — Fred has no uploaded KB documents
4. `KbDocumentService.ListDocumentsAsync` catches all exceptions silently and returns `[]` — any S3 errors are silently swallowed
5. KnowledgeBase.razor has no diagnostic logging of the resolved `_userId` or loaded entry counts

**The panel IS working — it's showing empty state because Fred genuinely has no KB entries or documents.**
The bug is that there's no diagnostic logging to confirm this, and exceptions in `ListDocumentsAsync` are silently swallowed.

### Fix Plan

**Step 1: Add diagnostic logging to KnowledgeBase.razor `OnInitializedAsync`**

After resolving `_userId`, add:
```csharp
_logger.LogInformation("KnowledgeBase init: resolved userId={UserId} from oid={Oid}", _userId, oid);
```

After loading entries:
```csharp
_logger.LogInformation("KnowledgeBase init: loaded personalEntries={PersonalCount}, teams={TeamCount}, personalDocs={DocCount} for userId={UserId}",
    _personalEntries.Count, _teams.Count, _personalDocuments.Count, _userId);
```

**Step 2: Improve error visibility in `ListDocumentsAsync` exception handling**

In `KbDocumentService.cs`, the `ListDocumentsAsync` method catches exceptions with `LogWarning`.
This is fine. But the warning message should include the userId for easier debugging.
The current code already includes prefix which contains userId, so it's acceptable.
Just ensure the log level is appropriate (Warning is fine).

**Step 3: Add null/empty guard with better auth error message in KnowledgeBase.razor**

The current code checks `string.IsNullOrEmpty(_userId)` and sets `_authError` — this is correct.
Add logging for the null-oid case too:

```csharp
// Before existing check:
if (string.IsNullOrEmpty(oid))
{
    _logger.LogWarning("KnowledgeBase: could not extract OID from auth claims");
    _authError = "Please log in to access the Knowledge Base.";
    _loading = false;
    return;
}

// After dbUser lookup:
if (dbUser == null)
{
    _logger.LogWarning("KnowledgeBase: no user record found for oid={Oid}", oid);
}
```

---

## Execution Instructions

For each file change, use Claude Code to make the edits. Write the changes carefully.

**Files to modify:**
1. `/home/fredw/projects/fip/fait-v2/src/FortressAI.V2.Web/wwwroot/css/fortress.css`
   - Add missing chat CSS classes (chat-input-bar, chat-input-field, chat-send-btn, chat-empty-state, etc.)
   - Update `.message-bubble` section for v1 parity
   
2. `/home/fredw/projects/fip/fait-v2/src/FortressAI.V2.Web/Components/Chat/ChatView.razor`
   - Fix hardcoded color values in `<style>` block to use CSS variables

3. `/home/fredw/projects/fip/fait-v2/src/FortressAI.V2.Web/Components/Pages/KnowledgeBase.razor`
   - Add diagnostic logging in `OnInitializedAsync`

4. `/home/fredw/projects/fip/fait-v2/src/FortressAI.V2.Web/Services/KbDocumentService.cs`
   - Ensure LogDocumentService.ListDocumentsAsync logs userId clearly (verify current logging is sufficient)

**After all changes:**
1. Run `dotnet build` in `/home/fredw/projects/fip/fait-v2/src/FortressAI.V2.Web/` — must be 0 errors
2. `git add` changed files
3. `git commit -m "fix(fait#3117,fait#3118): chat UI v1 parity + KB panel user resolution"`

**Key CSS variable rules — MANDATORY:**
All colors must use CSS variables. NO hardcoded hex colors.
Available variables: `--color-primary`, `--color-accent`, `--color-border`, `--color-text-primary`,
`--color-text-secondary`, `--color-text-muted`, `--color-surface`, `--color-surface-sunken`,
`--color-gold`, `--color-gold-muted`, `--color-primary-light`, `--color-primary-hover`,
`--color-text-on-primary`, `--color-text-on-accent`

Spacing: `--space-1` through `--space-12`
Typography: `--text-xs`, `--text-sm`, `--text-base`, `--text-md`, `--text-lg`
Font weights: `--font-regular`, `--font-medium`, `--font-semibold`, `--font-bold`
Radius: `--radius-sm`, `--radius-md`, `--radius-lg`, `--radius-xl`, `--radius-full`
Transitions: `--transition-fast`, `--transition-normal`, `--transition-slow`
