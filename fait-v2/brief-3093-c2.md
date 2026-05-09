# Fix brief: ADO#3093 — Startup warning for INTERNAL_API_TOKEN

## File to edit
`/home/fredw/projects/fip/fait-v2/agent-harness/harness-server.js`

## Current state
- Fix 1 (preference detection wired into Bedrock path) is ALREADY DONE. Do NOT touch it.
- Fix 2 (startup warning) is NOT yet done.

## The ONLY change to make

In the bootstrap IIFE near the bottom of the file (around line 1437), add a startup warning before `app.listen`:

**Find this exact text:**
```javascript
// Bootstrap GCP credentials then start server
(async () => {
    await bootstrapGcpCredentials();
    app.listen(PORT, '0.0.0.0', () => {
```

**Replace with:**
```javascript
// Bootstrap GCP credentials then start server
(async () => {
    if (!INTERNAL_API_TOKEN) {
        console.warn('[harness] WARNING: INTERNAL_API_TOKEN not set — preference writes will fail with 401');
    }
    await bootstrapGcpCredentials();
    app.listen(PORT, '0.0.0.0', () => {
```

## Verification after change
Run: `node --check /home/fredw/projects/fip/fait-v2/agent-harness/harness-server.js`
It must exit with code 0.

## Constraints
- ONLY make this one change. Nothing else.
- Do not modify lines 1395-1397 (preference detection — already wired).
- Do not add any imports or other modifications.
