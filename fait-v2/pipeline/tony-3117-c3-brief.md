# Tony Stark — ADO#3117 Cycle 3 Fixes

## Working Directory
`/home/fredw/projects/fip/fait-v2`

## File to Fix
`src/FortressAI.V2.Web/wwwroot/css/fortress.css`

## Fix 1 — Remaining hardcoded hex colors

### `.chat-kb-toggle.active`
- `color: #fff` → `color: var(--color-white)` (or `var(--color-bg)` if `--color-white` doesn't exist — check `:root` first)
- `var(--accent-blue, #2196F3)` used as background/border fallback → replace with clean `var(--color-primary)` or `var(--color-info)`. Remove the `#2196F3` hex fallback entirely.

### `.jump-to-bottom`
- `color: #d4af37` → `var(--color-accent)` (this is the gold color)
- **`.jump-to-bottom:hover`:** `color: #e8c84a` → check if `--color-accent-hover` or `--color-accent-light` exists in `:root`. If not, ADD `--color-accent-hover: #e8c84a` to `:root` and use `var(--color-accent-hover)` here.

## Fix 2 — Two remaining `max-width: 900px` literals

The variable `--chat-content-max-width: 900px` already exists in `:root`. Two spots were missed:
- `.message` (around line 901) — `max-width: 900px` → `max-width: var(--chat-content-max-width)`
- `.chat-input-wrapper` (around line 1075) — `max-width: 900px` → `max-width: var(--chat-content-max-width)`

Use grep to find exact lines and replace them.

## Steps
1. Read `:root` in fortress.css to check which color variables exist (`--color-white`, `--color-primary`, `--color-info`, `--color-accent`, `--color-accent-hover`)
2. Make all the replacements described above
3. Run `cd /home/fredw/projects/fip/fait-v2 && dotnet build` — 0 errors required
4. `git add src/FortressAI.V2.Web/wwwroot/css/fortress.css`
5. `git commit -m "fix(fait#3117): c3 — remaining hex colors to vars, final 900px literals to var"`
6. Report the commit hash

## Important
- Do NOT add new color variables to `:root` unless checking first that they don't already exist
- Only modify `fortress.css` (and `:root` additions if needed for hover color)
- No scope creep — only the fixes listed above
