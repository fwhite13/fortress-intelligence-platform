# ADO#4249 — Ephemeral Chips — Cycle 2 Review Brief

## Task
Verify that three specific fixes were applied correctly in `fait/agent-harness/harness-server.js` (commit `12378215` on top of `d1f81cc2`).

## File to Review
`/home/fredw/projects/fip/fait/agent-harness/harness-server.js`

---

## Fix Verification Targets

### I1: `getBuiltinSummary` default case
- **Expected:** `default: return 'Working...';`
- **Must NOT contain:** any reference to `toolName` in the default case (the original bug was `` return `${toolName}...` ``)
- **Verify:** Read the `getBuiltinSummary` function (around line 328). Confirm the `default` branch returns the literal string `'Working...'` with no template literal and no `toolName` variable reference.

### N2: `web_search` chip
- **Expected:** `toolInput.query ? \`Searching: ${chipTrunc(toolInput.query, 50)}\` : 'Searching...'`
- **Verify:** Read the `web_search` branch (around line 4421-4422). Confirm the chip uses a conditional — if `toolInput.query` exists, show `Searching: <truncated query>`; otherwise show the static fallback `'Searching...'`.

### N1: `ado_create_work_item` chip
- **Expected:** `toolInput.title ? \`Filing WI: ${chipTrunc(toolInput.title)}\` : 'Filing WI...'`
- **Verify:** Read the `adoSummaries` object (around line 4402). Confirm the `ado_create_work_item` entry uses a conditional — if `toolInput.title` exists, show `Filing WI: <truncated title>`; otherwise show the static fallback `'Filing WI...'`.

---

## Context Check
1. Read lines 328–362 for `getBuiltinSummary` — verify ALL cases, not just default
2. Read lines 4395–4430 for the ADO+web_search chip block
3. Check that no other nearby code was accidentally modified

## Pass Criteria
- All three fixes exactly match expected strings above
- No `toolName` reference in the `getBuiltinSummary` default case
- Surrounding code unchanged and intact

## Report Format
For each fix: VERIFIED or FAILED, with the actual code found.
Then: overall PASS or FAIL.
