# Review Report — ADO#3188

**Task:** 4.2-A: Harness read_memory + write_memory tools  
**Reviewer:** Hawkeye (Clint Barton)  
**Cycle:** 1 of 2  
**Date:** 2026-05-10

---

### Verdict: FAIL

---

### CC Review Summary

CC was run adversarially against both changed files with targeted checks on all 11 focal points from the review brief. CC confirmed 3 failures:

- **C6 (CRITICAL):** `read_memory` and `write_memory` are not in `toolConfig.tools[]` in the Bedrock ConverseStream path — the Bedrock model cannot call these tools regardless of what the system prompt says
- **C5 (CRITICAL):** The CC spawn path injects `userEmail` into context but not `userId` (the GUID) — CC cannot supply a valid userId to the harness memory endpoints; every call returns 400
- **I4 (Important):** `WriteTopic` has no try/catch for `ArgumentException` from `WriteTopicAsync` — a slug of "MEMORY" causes a 500 rather than a 400 Bad Request

CC also confirmed 8 checks passed cleanly (C1, C2, C3, C4, I1, I2, I3, I5).

---

### Consistency Audit

**Files Cross-Referenced:**
- `MemoryController.cs` ↔ `harness-server.js` — token name `X-Internal-Token` matches ✅
- `MemoryController.cs` ↔ `harness-server.js` — JSON fields `{ userId, slug, title, content }` match ✅
- `BUILTIN_TOOLS` Set ↔ harness endpoint registrations — both `read_memory` and `write_memory` present ✅
- Bedrock `toolConfig` ↔ system prompt tool guidance — **MISMATCH ❌** — system prompt tells model it has `read_memory`/`write_memory`; toolConfig does not define them

---

### Spec Compliance Check

**§ Functional requirements:** Two harness tool handlers added, BUILTIN_TOOLS updated, system prompt guidance injected in both cold-start paths — mechanically present ✅  
**§ Actual functionality:** Tools are non-functional in Bedrock path (not in toolConfig) and non-functional in CC spawn path (userId not injectable) ❌  
**Spec compliance verdict:** ❌ NON-COMPLIANT — tools exist in code but cannot be called in either execution path

---

### Issues Found

| # | Severity | File | Area | Issue | Fix |
|---|----------|------|------|-------|-----|
| C6 | **CRITICAL** | `harness-server.js` | Lines 1411–1453 | `read_memory` and `write_memory` absent from Bedrock `toolConfig.tools[]`. System prompt tells model it has these tools; Bedrock API will never offer them. Feature is dead in all non-task-mode conversations. | Add both tools to `toolConfig.tools[]` with `inputSchema`. Add dispatch branches in the tool-result loop (after `list_workspace_files` branch at line 1490) that POST to `/tools/read_memory` and `/tools/write_memory` with `userId` injected from outer scope. |
| C5 | **CRITICAL** | `harness-server.js` | Lines 1237–1252 (contextParts) | CC spawn path injects `userEmail` but not `userId` GUID. Controller requires `Guid.TryParse(request.UserId)` — invalid/missing userId returns 400. CC cannot call either memory tool. | Inject `userId` into the CC context brief: add `contextParts.push(\`## Session Context\\nuserId: ${userId}\`)` (or equivalent). |
| I4 | **Important** | `MemoryController.cs` | `WriteTopic`, line 58 | `WriteTopicAsync` throws `ArgumentException` for reserved slug "MEMORY" (ADO#3186 guard). No catch — exception propagates as 500. Model sees `{ success: false, error: "memory/write failed (500): ..." }` with no actionable signal. | Wrap the `WriteTopicAsync` call: `catch (ArgumentException ex) { return BadRequest(new { error = ex.Message }); }` |
| N1 | Nitpick | `MemoryController.cs` | All methods | `_logger` injected but never used — zero log statements in controller | Add `_logger.LogInformation` on success reads/writes, `_logger.LogWarning` on auth failures, `_logger.LogError` on exceptions |

---

### Critical Issues — Detail

#### C6: Bedrock toolConfig gap (BLOCKING)

The Bedrock `toolConfig` at lines 1411–1453 defines only two tools:
1. `search_knowledge_base`
2. `list_workspace_files`

`read_memory` and `write_memory` are absent. The Bedrock API contract requires tools to be declared in `toolConfig` before the model can call them. System prompt guidance at lines 1385–1388 instructs the model to use these tools — but since they aren't in `toolConfig`, the model will never attempt to call them. This is dead instruction.

Additionally, the dispatch loop at lines 1490–1506 has no branch for `read_memory` or `write_memory`. Even if the tools were added to `toolConfig`, the dispatch would not route them — they'd fall through to the `search_knowledge_base` default case.

**Required fix:**

```javascript
// In toolConfig.tools[] array, add after list_workspace_files:
{
    toolSpec: {
        name: 'read_memory',
        description: 'Read a memory topic by slug. Returns the stored content for that topic.',
        inputSchema: {
            json: {
                type: 'object',
                properties: {
                    slug: { type: 'string', description: 'The memory topic slug to read' }
                },
                required: ['slug']
            }
        }
    }
},
{
    toolSpec: {
        name: 'write_memory',
        description: 'Write or update a memory topic. Persists content under the given slug.',
        inputSchema: {
            json: {
                type: 'object',
                properties: {
                    slug: { type: 'string', description: 'Topic slug (identifier)' },
                    title: { type: 'string', description: 'Human-readable topic title' },
                    content: { type: 'string', description: 'Content to persist' }
                },
                required: ['slug', 'content']
            }
        }
    }
}

// In the tool dispatch loop, add branches after list_workspace_files handler:
} else if (toolUseAccumulator.name === 'read_memory') {
    try {
        const rmRes = await fetch(`http://localhost:${PORT}/tools/read_memory`, {
            method: 'POST',
            headers: { 'Content-Type': 'application/json' },
            body: JSON.stringify({ userId, slug: toolInput.slug })
        });
        const rmData = await rmRes.json();
        toolResultText = `\n\n[Memory Read]\n${JSON.stringify(rmData, null, 2)}\n\n`;
    } catch (rmErr) {
        toolResultText = `\n\n[Memory Read Error]\n${rmErr.message}\n\n`;
    }
} else if (toolUseAccumulator.name === 'write_memory') {
    try {
        const wmRes = await fetch(`http://localhost:${PORT}/tools/write_memory`, {
            method: 'POST',
            headers: { 'Content-Type': 'application/json' },
            body: JSON.stringify({ userId, slug: toolInput.slug, title: toolInput.title, content: toolInput.content })
        });
        const wmData = await wmRes.json();
        toolResultText = `\n\n[Memory Write]\n${JSON.stringify(wmData, null, 2)}\n\n`;
    } catch (wmErr) {
        toolResultText = `\n\n[Memory Write Error]\n${wmErr.message}\n\n`;
    }
}
```

Note: `userId` comes from outer scope (the same variable used to build the Bedrock API request) — not from `toolInput`. This matches the existing pattern for `list_workspace_files` at line 1495.

#### C5: userId not injected into CC spawn context (BLOCKING)

The CC spawn path builds a context brief from `contextParts[]` (lines 1237–1252). It injects `userEmail` but the memory endpoints require `userId` as a GUID (`Guid.TryParse` on controller line 33). CC has no way to derive the GUID from an email string.

**Required fix:**

```javascript
// After: if (userEmail) contextParts.push(`## User Identity\nEmail: ${userEmail}`);
// Add:
contextParts.push(`## Session Identifiers\nuserId: ${userId}`);
```

This makes `userId` available in CC's context so it can include it in tool call arguments.

---

### What to Fix (for Tony)

Three changes required before re-review:

1. **`harness-server.js` — Bedrock toolConfig:** Add `read_memory` and `write_memory` to `toolConfig.tools[]` with inputSchema. Then add dispatch branches in the tool-result loop (after the `list_workspace_files` branch) that POST to `/tools/read_memory` and `/tools/write_memory`, injecting `userId` from outer scope. Exact code above.

2. **`harness-server.js` — CC context:** Inject `userId` (GUID) into `contextParts[]` in the task mode path so CC can pass it to memory tool endpoints. One line: `contextParts.push(\`## Session Identifiers\\nuserId: ${userId}\`)`.

3. **`MemoryController.cs` — WriteTopic:** Wrap `WriteTopicAsync` call in a `try/catch (ArgumentException ex)` that returns `BadRequest(new { error = ex.Message })`.

These are all targeted fixes. The existing handler logic, auth, BUILTIN_TOOLS, and system prompt injections are correct.

---

### What Passed

- **Token auth null-safety** ✅ — `string.IsNullOrEmpty(configToken)` guard is correct; no blank-config bypass possible
- **read_memory not-found response** ✅ — Returns user-readable string, not null
- **write_memory best-effort** ✅ — Catch block returns `{ success: false }` at 200, never 500
- **BUILTIN_TOOLS completeness** ✅ — Both tools present in the Set
- **System prompt injection — both paths** ✅ — Identical guidance text in both `contextParts` and `systemParts`
- **[AllowAnonymous] on both actions** ✅ — ReadTopic and WriteTopic both decorated
- **Header key casing** ✅ — Case-insensitive in ASP.NET Core
- **title default** ✅ — Harness fallback `title || slug` and controller fallback are consistent

---

_Report written by Hawkeye. CC invocation: `cat /tmp/clint-review-brief-3188.md | claude --model sonnet --print --dangerously-skip-permissions` from `/home/fredw/projects/fip/fait`._
