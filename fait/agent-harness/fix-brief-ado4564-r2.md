# Fix Brief: ADO4564 R2 — update_user_profile review fixes

## File
`/home/fredw/projects/fip/fait/agent-harness/harness-server.js`

## Three targeted fixes — no other changes.

---

## Fix 1 — GUID validation in /tools/update_user_profile endpoint

Around line 1305–1310, in the `app.post('/tools/update_user_profile', ...)` handler:

Current code (lines ~1303–1309):
```javascript
app.post('/tools/update_user_profile', async (req, res) => {
    const { userId, content, mode } = req.body || {};
    if (!userId || !content) {
        return res.status(400).json({ error: 'userId and content are required' });
    }
    const effectiveMode = mode || 'merge';
```

Replace with (add GUID validation AFTER null check, BEFORE effectiveMode):
```javascript
app.post('/tools/update_user_profile', async (req, res) => {
    const { userId, content, mode } = req.body || {};
    if (!userId || !content) {
        return res.status(400).json({ error: 'userId and content are required' });
    }
    if (!/^[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}$/i.test(userId)) {
        return res.status(400).json({ error: 'Invalid userId' });
    }
    const effectiveMode = mode || 'merge';
```

Note: Do NOT re-declare `GUID_RE` as a const since it's already declared later in the file at the `/import-memory` endpoint. Use the inline regex pattern instead.

---

## Fix 2 — Set isError on HTTP failure in the Bedrock handler

Around lines 4506–4522, in the `else if (toolUseAccumulator.name === 'update_user_profile')` branch inside the try block:

Current code:
```javascript
                                const upResult = await upRes.json();
                                toolResult = upResult.success ? 'Profile updated.' : `Error: ${upResult.error}`;
                                toolResultText = `\n\n[Profile Update]\n${JSON.stringify(upResult, null, 2)}\n\n`;
                                emitToolCall(res, 'builtin', toolUseAccumulator.name, 'done', `${toolUseAccumulator.name} complete`);
```

Replace with:
```javascript
                                const upResult = await upRes.json();
                                if (!upRes.ok || !upResult.success) {
                                    isError = true;
                                    toolResultText = `\n\n[Profile Update Error]\n${JSON.stringify(upResult, null, 2)}\n\n`;
                                    emitToolCall(res, 'builtin', toolUseAccumulator.name, 'error', `Error: ${String(upResult.error || 'unknown').substring(0, 100)}`);
                                } else {
                                    toolResultText = `\n\n[Profile Update]\n${JSON.stringify(upResult, null, 2)}\n\n`;
                                    emitToolCall(res, 'builtin', toolUseAccumulator.name, 'done', `${toolUseAccumulator.name} complete`);
                                }
```

---

## Fix 3 — Remove dead toolResult assignment

In Fix 2 above, the `toolResult = upResult.success ? 'Profile updated.' : \`Error: ${upResult.error}\`` line is being replaced entirely (it's dead code — toolResult is not consumed downstream). The replacement in Fix 2 above already omits it — just confirm it's gone.

---

## Verification
After making changes, run:
```bash
node --check /home/fredw/projects/fip/fait/agent-harness/harness-server.js
```
It must exit with code 0 (no output = success).

## Constraints
- Only touch the two locations described above
- No other changes to any file
- Do not add any imports or new variables beyond what's specified
- Preserve all indentation and surrounding code exactly
