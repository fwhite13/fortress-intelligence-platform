# CC Brief: WI832 Cycle 2 — Two Precise Fixes

You are making exactly 2 surgical fixes in the `/home/fredw/projects/fip/` repo. No scope creep. No other changes.

---

## Fix 1 — CoworkWeb.csproj: net8.0 → net9.0

**File:** `/home/fredw/projects/fip/cowork/src/CoworkWeb/CoworkWeb.csproj`

Change the TargetFramework from net8.0 to net9.0:

```xml
<!-- BEFORE -->
<TargetFramework>net8.0</TargetFramework>

<!-- AFTER -->
<TargetFramework>net9.0</TargetFramework>
```

That is the only change in this file.

---

## Fix 2 — SSE close handler in tasks.ts

**File:** `/home/fredw/projects/fip/cowork/src/CoworkAgent/src/routes/tasks.ts`

In the `GET /tasks/:id/stream` route handler, add a cancellation flag and close handler. The current code after `res.flushHeaders()` looks like this:

```typescript
  res.flushHeaders();

  try {
    for await (const chunk of gen) {
      res.write(`data: ${JSON.stringify(chunk)}\n\n`);

      if (chunk.type === 'result' || chunk.type === 'error') {
        taskStreams.delete(id);
        break;
      }
    }
  } catch (err: any) {
```

Replace that block with:

```typescript
  res.flushHeaders();

  let cancelled = false;
  req.on('close', () => {
    cancelled = true;
  });

  try {
    for await (const chunk of gen) {
      if (cancelled) break;
      res.write(`data: ${JSON.stringify(chunk)}\n\n`);

      if (chunk.type === 'result' || chunk.type === 'error') {
        taskStreams.delete(id);
        break;
      }
    }
  } catch (err: any) {
```

That is the only change in this file.

---

## Summary of changes
- `cowork/src/CoworkWeb/CoworkWeb.csproj`: `net8.0` → `net9.0`
- `cowork/src/CoworkAgent/src/routes/tasks.ts`: add `cancelled` flag, `req.on('close', ...)`, and `if (cancelled) break;` at top of loop

Make only these two changes. Do not touch anything else.
