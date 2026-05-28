# CC Fix Brief — ADO#4249 Review Cycle 1

## File to edit
`/home/fredw/projects/fip/fait/agent-harness/harness-server.js`

## Three targeted fixes — apply exactly as specified, no other changes

### Fix 1 — Line 361: `getBuiltinSummary` default case
**Current:**
```
        default: return `${toolName}...`;
```
**Replace with:**
```
        default: return 'Working...';
```

### Fix 2 — Line 4384: `ado_create_work_item` missing title fallback
**Current:**
```
                                ado_create_work_item: `Filing WI: ${chipTrunc(toolInput.title ?? '')}`,
```
**Replace with:**
```
                                ado_create_work_item: toolInput.title ? `Filing WI: ${chipTrunc(toolInput.title)}` : 'Filing WI...',
```

### Fix 3 — Line 4404: `web_search` missing query fallback
**Current:**
```
                            emitToolCall(res, 'brave', 'web_search', 'calling', `Searching: ${chipTrunc(toolInput.query ?? '', 50)}`);
```
**Replace with:**
```
                            emitToolCall(res, 'brave', 'web_search', 'calling', toolInput.query ? `Searching: ${chipTrunc(toolInput.query, 50)}` : 'Searching...');
```

## Constraints
- Touch ONLY these three lines. No formatting changes, no other edits.
- Do not add imports, exports, or other modifications.
- After making changes, output "DONE" so I know it completed.
