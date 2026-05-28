# QA Report: ADO#4249 — Ephemeral Chips: Contextual Detail

**Date:** 2026-05-27 13:39 EDT  
**QA Analyst:** Natasha Romanoff (Black Widow)  
**Environment:** fred-dev:290 / fait-v2-agent-harness:78 / fred-chat:efa0a41c  
**ECS Status:** ACTIVE, 1/1 running, HEALTHY  

---

## Verdict: ✅ PASS

---

## Tests Run

- **Smoke:** 3 — 3 passed
- **Targeted:** 5 acceptance criteria — all PASS
- **Code Verification:** PASS (chip logic extracted and unit-tested directly)
- **E2E Visual (Live UI):** ⚠️ BLOCKED — Cloudflare Access fingerprint enforcement blocks headless Playwright for new sessions (recurring infra blocker, not a deployment issue)

---

## Smoke Tests

| Test | Result | Detail |
|------|--------|--------|
| ECS Health | ✅ PASS | `fred-dev:290` ACTIVE, 1/1 running |
| `/health` endpoint | ✅ PASS | HTTP 200 `{"status":"healthy"}` in 62ms |
| App startup logs | ✅ PASS | `Application started`, `Hosting environment: Development`, no migration failures |
| Harness task def | ✅ PASS | `fait-v2-agent-harness:78` with image `fait-v2-agent-harness:efa0a41c` confirmed |
| Harness wiring | ✅ PASS | `fred-dev:290` env: `Fargate__TaskDefinition=fait-v2-agent-harness:78`, `FAIT_HARNESS_VERSION=78` |

---

## Deployment Integrity

| Check | Result |
|-------|--------|
| Commit `efa0a41c` deployed | ✅ Both `d1f81cc2` (feat) and `12378215` (fix cycle 1) are ancestors of deployed commit |
| Both chips commits in harness image | ✅ `fait-v2-agent-harness:efa0a41c` contains all ADO#4249 changes |
| Blazor image | ✅ `fred-chat:efa0a41c` with `ChatView.razor` `TruncChip()` + `GetToolLabel` changes |
| `ASPNETCORE_ENVIRONMENT=Development` | ✅ Confirmed in CloudWatch logs |

---

## Acceptance Criteria Verification

### AC1: All first-class tool chips include a context string ✅ PASS

**Harness** — `getBuiltinSummary()` in harness-server.js:328 handles all builtins:

| Tool | Input | Chip Output | Result |
|------|-------|-------------|--------|
| `read_memory` | `{ slug: 'dark-mode-preference' }` | `Reading memory: dark-mode-preference` | ✅ |
| `write_memory` | `{ title: 'My preference' }` | `Saving memory: My preference` | ✅ |
| `read_memory` | `{}` (no slug) | `Reading memory...` | ✅ graceful fallback |
| `write_memory` | `{}` (no title) | `Saving to memory...` | ✅ graceful fallback |
| `search_memory` | `{ query: 'dark mode' }` | `Searching memory: "dark mode"` | ✅ |
| `read_file` | `{ path: '/workspace/test.py' }` | `Reading: test.py` | ✅ |
| `write_file` | `{ path: '/workspace/test.py' }` | `Saving: test.py` | ✅ |
| `create_document` | `{ filename: 'report.docx' }` | `Creating: report.docx` | ✅ |
| `search_knowledge_base` | `{ query: 'insurance claims' }` | `Searching KB: "insurance claims"` | ✅ |

**Blazor** — `GetToolLabel()` in ChatView.razor:1517 prefers non-empty summary from harness:
```csharp
if (!string.IsNullOrWhiteSpace(summary))
    return summary;
```
When harness provides a summary, it is always used. Fallback strings are only used if harness sends no summary (legacy path).

---

### AC2: Task start chip includes working folder name ✅ PASS

Harness-server.js line 3102 emits folder context chip after `resolveTaskFolder()`:
```js
message: `Working in: /${chipTrunc(folder.name, 40)}`
```

Verified chip format: `"Working in: /project-alpha"` — correct prefix and path separator.

---

### AC3: CC sub-tool chips include brief description ✅ PASS

`resolveProgressLabel()` in harness-server.js:270 handles CC sub-tools:

| CC Tool | Sample Input | Chip Output |
|---------|-------------|-------------|
| `bash` | `{ command: 'python3 test.py' }` | `Running Python script...` |
| `bash` | `{ command: 'pip install pandas' }` | `Installing dependencies...` |
| `bash` | `{ command: 'ls /workspace' }` | `Reading files...` |
| `bash` (generic) | `{ command: 'echo hello' }` | `Running: echo hello` |
| `write_file` | `{ path: 'chip-test.md' }` | `Saving: chip-test.md` |
| `read_file` | `{ path: '/home/user/chip-test.md' }` | `Reading: chip-test.md` |
| `str_replace_based_edit_tool` | `{ path: 'main.py' }` | `Editing: main.py` |

---

### AC4: Chip text is human-readable (no raw JSON, no underscores) ✅ PASS

- Default fallback for any unhandled tool: `'Working...'` — not `read_memory...` or `write_file...`
- `web_search` chip: `Searching: [query]` — not `web_search...`
- `read_memory` chip: `Reading memory: [slug]` — not `read_memory...`
- Blazor `GetToolLabel` switch: transforms all known raw tool names to human-readable strings; unknown tools return `"Working..."` not raw `toolName`
- Double safety: harness always sends a summary; Blazor also reformats the raw name in its fallback

Verified: `getBuiltinSummary('some_unknown_tool', {})` → `'Working...'` ✅

---

### AC5: Long context strings truncated gracefully (~60 chars with ellipsis) ✅ PASS

**Two-layer truncation:**

**Layer 1 — Harness** `chipTrunc(str, max=57)`:
- Truncates to 57 chars + `...` = 60 chars total
- Applied in both `getBuiltinSummary` and `resolveProgressLabel`
- Applied to web_search query, memory slug, memory title, filenames, ADO fields

**Layer 2 — Blazor** `TruncChip(s, max=60)`:
- Truncates to 57 chars + `...` = 60 chars total
- Applied at render time to all chip labels via `TruncChip(GetToolLabel(...))` line 191

Test results:
```
chipTrunc("this-is-a-very-long-memory-slug-that-definitely-exceeds-sixty-characters") 
  → "this-is-a-very-long-memory-slug-that-definitely-exceeds-s..." (60 chars) ✅

getBuiltinSummary('read_memory', { slug: <72-char slug> })
  → "Reading memory: this-is-a-very-long-memory-slug-that-definitely-exceeds-s..." ✅ (truncated)

getWebSearchChip({ query: <94-char query> })
  → "Searching: tell me all about the latest news regarding artifi..." (64 chars, truncated) ✅
```

---

## Code Paths Verified

| Component | File | Function | Lines |
|-----------|------|----------|-------|
| Harness truncation helper | `harness-server.js` | `chipTrunc()` | 263–268 |
| CC sub-tool labels | `harness-server.js` | `resolveProgressLabel()` | 270–315 |
| Builtin tool labels | `harness-server.js` | `getBuiltinSummary()` | 328–362 |
| Folder context chip | `harness-server.js` | inline emit | 3099–3105 |
| Web search chip | `harness-server.js` | inline emit | 4422 |
| ADO tool chips | `harness-server.js` | `adoSummaries` map | 4398–4413 |
| Blazor truncation helper | `ChatView.razor` | `TruncChip()` | 1513–1515 |
| Blazor label resolution | `ChatView.razor` | `GetToolLabel()` | 1517–1549 |
| Chip render | `ChatView.razor` | inline | 191 |
| task_progress handler | `ChatView.razor` | | 1140–1155 |
| tool_call handler | `ChatView.razor` | | 1112–1130 |

---

## Known Limitations

- **Test 3 (CC task chips)**: Could not trigger a live CC task due to CF Access blocking headless browser. Logic verified via code inspection — `resolveProgressLabel()` covers bash/write_file/read_file/str_replace patterns comprehensively. **Marked WARN (not FAIL)** per task instructions.
- **Live visual observation**: CF Access fingerprint enforcement blocks new headless sessions. Rob Nethery CF service token ticket remains open. Not a regression — this limitation predates ADO#4249.

---

## E2E Test Status

| Test | Status | Notes |
|------|--------|-------|
| Test 1 — Memory tool chips | ✅ CODE VERIFIED | `read_memory` → `Reading memory: [slug]`, `write_memory` → `Saving memory: [title]` |
| Test 2 — Web search chip | ✅ CODE VERIFIED | `web_search` → `Searching: [query]` (truncated if long) |
| Test 3 — CC task chips | ⚠️ CODE VERIFIED (E2E blocked) | `resolveProgressLabel()` covers bash/write_file/read_file; folder chip emits on resolution |
| Test 4 — Human-readable fallback | ✅ CODE VERIFIED | Unknown tools → `Working...`, not raw `some_unknown_tool...` |
| Test 5 — Truncation | ✅ CODE VERIFIED | `chipTrunc(57) + Blazor TruncChip(60)` — double-layer, functions correctly |

---

## Issues Found

**None.** All acceptance criteria pass. Code is correct. No regressions observed.

---

## Test Duration

~25 minutes (ECS verification + code extraction + direct unit testing of chip logic)

---

## Recommendations

1. ✅ **ADO#4249** — Mark Resolved. Implementation is solid.
2. Rob Nethery CF Access service token for `natasha-qa` on `fait.dev.fortressam.ai` — recurring blocker (tracked in `memory/entities/rob-nethery.md`). Would enable live E2E tests in future.

---

_Trust nothing. Verify everything. — Natasha_
