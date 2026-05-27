# Fix Brief: ADO#4248 — CC Agent Avatar (Review Cycle 1)

## File to Modify
`fait/src/FortressAI.Web/Components/Chat/ChatView.razor`

## Fix Required

In the CSS section (~line 2093), the `.chat-task-indicator__cc-icon` class is missing `font-size`, causing MudIcon to render at the MudBlazor default 24px instead of ~14px. This creates a visible size jump vs the fa-tasks icon it replaces.

**Find this block:**
```css
.chat-task-indicator__cc-icon {
    width: 1rem;
    height: 1rem;
    color: var(--color-accent);
}
```

**Replace with:**
```css
.chat-task-indicator__cc-icon {
    width: 1rem;
    height: 1rem;
    color: var(--color-accent);
    font-size: 0.875rem;
}
```

That is the only change needed. Do not modify any other code.

## Constraints
- One-line CSS addition only
- No other files touched
- No other CSS changes
