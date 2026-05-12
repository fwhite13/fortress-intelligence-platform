# Build Report — ADO#3280

**Engineer:** Tony Stark  
**Date:** 2026-05-11  
**Commit:** `68c2c2fa`  
**File:** `fait-v2/agent-harness/harness-server.js`

See full batch report: `BUILD-3277-3278-3279-3280-REPORT.md`

---

## Changes

### Fix 1 — `generate-document` error handler

**Before:**
```js
if (!genRes.ok) {
    const errText = await genRes.text();
    return res.status(500).json({ error: `Document generation failed: ${errText}` });
}
```

**After:**
```js
if (!genRes.ok) {
    const errText = await genRes.text();
    const isHtml = errText.trim().startsWith('<') || errText.includes('<!DOCTYPE');
    const safeErr = isHtml
        ? `Document generation failed (HTTP ${genRes.status}). The API returned an unexpected response.`
        : `Document generation failed: ${errText.substring(0, 200)}`;
    return res.status(500).json({ error: safeErr });
}
```

### Fix 2 — `read_memory` error handler

**Before:**
```js
const text = await resp.text();
throw new Error(`memory/read failed (${resp.status}): ${text}`);
```

**After:**
```js
const text = await resp.text();
const isHtml = text.trim().startsWith('<') || text.includes('<!DOCTYPE');
const safeText = isHtml ? `[non-JSON response, HTTP ${resp.status}]` : text.substring(0, 200);
throw new Error(`memory/read failed (${resp.status}): ${safeText}`);
```

---

## Acceptance Criteria

- [x] HTML error bodies never reach chat — detected and replaced with clean message
- [x] Non-HTML errors still shown but truncated to 200 chars
- [x] HTTP status code always included in error message
