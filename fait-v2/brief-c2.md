# CSS Variable Compliance Fixes — ADO#3117 Cycle 2

## File: src/FortressAI.V2.Web/wwwroot/css/fortress.css

Make the following precise edits to fortress.css. Use exact text matching.

---

### Step 1: Add new :root variables

Find this exact text near the end of the :root block:
```
  --mobile-nav-shadow: 0 -2px 8px rgba(0, 0, 0, 0.08);
}
```

Replace with:
```
  --mobile-nav-shadow: 0 -2px 8px rgba(0, 0, 0, 0.08);
  --chat-input-max-height: 200px;
  --chat-content-max-width: 900px;
  --font-weight-light: 300;
}
```

---

### Step 2: Fix .chat-empty-state padding

Find:
```
    padding: 3rem 2rem;
    color: var(--color-text-muted);
```

Replace with:
```
    padding: var(--space-12) var(--space-8);
    color: var(--color-text-muted);
```

---

### Step 3: Fix .chat-input-field height values

Find:
```
    min-height: 40px;
    max-height: 200px;
```

Replace with:
```
    min-height: var(--space-10);
    max-height: var(--chat-input-max-height);
```

---

### Step 4: Fix .chat-send-btn dimensions

Find:
```
.chat-send-btn {
    width: 40px;
    height: 40px;
```

Replace with:
```
.chat-send-btn {
    width: var(--space-10);
    height: var(--space-10);
```

---

### Step 5: Fix .chat-streaming-cursor font-weight

Find this exact block (the one in .chat-streaming-cursor):
```
.chat-streaming-cursor {
    animation: blink 1s infinite;
    color: var(--color-accent);
    font-weight: 300;
}
```

Replace with:
```
.chat-streaming-cursor {
    animation: blink 1s infinite;
    color: var(--color-accent);
    font-weight: var(--font-weight-light);
}
```

---

### Step 6: Fix .chat-streaming-indicator max-width

Find:
```
.chat-streaming-indicator {
    display: flex;
    align-items: baseline;
    gap: var(--space-1);
    padding: var(--space-1) 0;
    color: var(--color-text-primary);
    font-size: var(--text-base);
    line-height: 1.65;
    max-width: 900px;
```

Replace with:
```
.chat-streaming-indicator {
    display: flex;
    align-items: baseline;
    gap: var(--space-1);
    padding: var(--space-1) 0;
    color: var(--color-text-primary);
    font-size: var(--text-base);
    line-height: 1.65;
    max-width: var(--chat-content-max-width);
```

---

### Step 7: Fix .chat-artifact-progress max-width

Find:
```
.chat-artifact-progress {
    display: flex;
    align-items: center;
    gap: var(--space-3);
    padding: var(--space-2) var(--space-4);
    background: var(--color-surface-sunken);
    border-radius: var(--radius-md);
    max-width: 900px;
```

Replace with:
```
.chat-artifact-progress {
    display: flex;
    align-items: center;
    gap: var(--space-3);
    padding: var(--space-2) var(--space-4);
    background: var(--color-surface-sunken);
    border-radius: var(--radius-md);
    max-width: var(--chat-content-max-width);
```

---

### Step 8: Fix .chat-pill-icon — remove !important, use tokens, fix specificity

Find:
```
.chat-pill-icon {
    font-size: 1rem !important;
    width: 16px !important;
    height: 16px !important;
}
```

Replace with:
```
.chat-pill-icon,
.mud-chip .chat-pill-icon {
    font-size: var(--text-lg);
    width: var(--space-4);
    height: var(--space-4);
}
```

---

### Step 9: Fix .message-bubble max-width

Find:
```
.message-bubble {
    display: flex;
    flex-direction: column;
    margin-bottom: var(--space-4);
    max-width: 900px;
```

Replace with:
```
.message-bubble {
    display: flex;
    flex-direction: column;
    margin-bottom: var(--space-4);
    max-width: var(--chat-content-max-width);
```

---

### Step 10: Fix .message-bubble.message-user corner radius (WRONG SIDE)

Find:
```
    border-top-left-radius: var(--radius-sm);
    align-self: flex-end;
```

Replace with:
```
    border-top-right-radius: var(--radius-sm);
    align-self: flex-end;
```

---

## After all edits, run these commands in order:

1. `cd /home/fredw/projects/fip/fait-v2 && dotnet build 2>&1 | tail -10`
2. Verify 0 errors
3. `git -C /home/fredw/projects/fip/fait-v2 add src/FortressAI.V2.Web/wwwroot/css/fortress.css`
4. `git -C /home/fredw/projects/fip/fait-v2 commit -m "fix(fait#3117): CSS variable compliance — space tokens, correct user bubble corner radius, blue-purple accent var"`
5. Report the commit hash

## Notes
- Fix 6 (#7c83ff accent color in ChatView.razor): NOT PRESENT in codebase — skip.
- Do NOT change max-width: 900px in `.message` (line ~898) or `.chat-input-wrapper` (line ~1072) — those are not in scope.
- Do NOT change the `font-weight: 300` that may appear elsewhere — only change the one in `.chat-streaming-cursor`.
