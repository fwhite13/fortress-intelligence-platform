# CC Brief: ADO4572 — Fix resumption brief HeadObjectCommand → GetObjectCommand

## File to edit
`/home/fredw/projects/fip/fait/agent-harness/harness-server.js`

## What to change

Find this exact block (around lines 2908–2914):

```js
            try {
                const memKey = `${S3_PREFIX}workspaces/${userId}/memory/MEMORY.md`;
                const headCmd = new HeadObjectCommand({ Bucket: S3_BUCKET, Key: memKey });
                const headResp = await s3Client.send(headCmd);
                memoryTimestamp = headResp.LastModified ? new Date(headResp.LastModified).toLocaleDateString('en-US', { month: 'short', day: 'numeric' }) : null;
            } catch (e) {
                console.warn(`[harness] resumption brief: could not get MEMORY.md timestamp: ${e.message}`);
            }
```

Replace it with:

```js
            try {
                const memKey = `${S3_PREFIX}workspaces/${userId}/memory/MEMORY.md`;
                const memContent = await fetchS3File(memKey);
                memoryTimestamp = memContent ? new Date().toLocaleDateString('en-US', { month: 'short', day: 'numeric' }) : null;
            } catch (e) {
                memoryTimestamp = null;
            }
```

## Rules
- Make ONLY this single block replacement — no other changes
- Do not modify any surrounding logic
- Do not remove the `let memoryTimestamp = null;` line above the block
- Do not touch the guard at `!hasHistory && !memoryTimestamp`
- This is a JS file — no compilation needed, just verify the syntax is clean

## Done
When complete, output the git diff of the change only.
