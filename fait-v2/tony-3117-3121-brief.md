# Tony Stark — ADO#3117 + ADO#3121 Combined Build Pass

## Working Directory
`/home/fredw/projects/fip/fait-v2`

## File to Fix
`src/FortressAI.V2.Web/wwwroot/css/fortress.css`

---

## ADO#3121 — User Chat Bubble Wrong Background Color

The `.message-bubble.message-user .message-content` selector currently uses
`background: var(--color-primary-light)` which renders as a pale blue-grey (#EEF2F7) —
visually indistinguishable from the page background. User bubbles must be dark navy
(`var(--color-primary)`) with white text so they stand out.

### Fix 1 — `.message-bubble.message-user .message-content`

**Current (around line 2597):**
```css
.message-bubble.message-user .message-content {
    background: var(--color-primary-light);
    padding: var(--space-3) var(--space-4);
    border-radius: var(--radius-lg);
    border-top-right-radius: var(--radius-sm);
    align-self: flex-end;
    max-width: 80%;
}
```

**Required (exact replacement):**
```css
.message-bubble.message-user .message-content {
    background: var(--color-primary);
    color: var(--color-text-on-primary);
    padding: var(--space-3) var(--space-4);
    border-radius: var(--radius-lg);
    border-top-right-radius: var(--radius-sm);
    align-self: flex-end;
    max-width: 80%;
}
```

Changes:
- `background: var(--color-primary-light)` → `background: var(--color-primary)` (dark navy)
- ADD `color: var(--color-text-on-primary)` (white text — `--color-text-on-primary: #ffffff` already defined in `:root`)

### Fix 2 — Check `.message-bubble` and `.message-bubble.message-user` for border rule

Scan all `.message-bubble` rules in the file. If any `border` property exists on:
- `.message-bubble` (the wrapper)
- `.message-bubble.message-user` (if it exists as a separate rule from `.message-bubble.message-user .message-content`)

...set `border: none` or remove it. The dark navy bubble should have no separate border frame.

From the current state, `.message-bubble` does NOT have a border property — just verify this is still true and leave it clean.

### Fix 3 — Alignment verification (NO change needed if already correct)

The `.message-bubble.message-user .message-content` already has `align-self: flex-end`.
The `.message-bubble` wrapper already has `margin-left: auto; margin-right: auto`.
This is correct — NO change required. Just confirm visually in the diff.

---

## ADO#3117 — Remaining Items (Verify Clean)

ADO#3117 passed Cycle 3 review at commit `9b352982`. The following items were
already fixed and are confirmed passing. **Do NOT touch these** — just confirm they
are still correct after your 3121 edits:

- `.chat-empty-state` padding: `var(--space-12) var(--space-8)` ✅ confirmed present
- `.chat-send-btn` width/height: `var(--space-10)` ✅ confirmed present
- `.chat-input-field` min-height: `var(--space-10)` ✅ confirmed present

---

## Steps

1. Open `src/FortressAI.V2.Web/wwwroot/css/fortress.css`
2. Find `.message-bubble.message-user .message-content` (around line 2597)
3. Apply Fix 1: change background + add color property
4. Scan all `.message-bubble*` rules for any stray `border` — confirm none exist or remove
5. Run `cd /home/fredw/projects/fip/fait-v2 && dotnet build src/FortressAI.V2.Web/FortressAI.V2.Web.csproj` — 0 errors required
6. `git add src/FortressAI.V2.Web/wwwroot/css/fortress.css`
7. `git commit -m "fix(fait#3121): user chat bubble background var(--color-primary), add color-text-on-primary"`
8. Report the exact commit hash

## Important
- Only modify `fortress.css`
- No scope creep beyond the changes listed
- `--color-text-on-primary: #ffffff` is already in `:root` — do NOT add a duplicate
- Do NOT modify the ADO#3117 items that are already passing
