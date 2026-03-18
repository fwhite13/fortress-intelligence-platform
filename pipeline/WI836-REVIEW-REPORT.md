# Review Report: WI836
## Verdict: NEEDS-CHANGES
## Review Cycle: 1 of 2

---

## CC Invocation

```bash
cd /home/fredw/projects/skunkworks/vendorply-email-triage
cat ~/projects/fait-for-excel/review-brief-wi836.md | claude --model sonnet -p
```

First 20 lines of CC output:
```
I was denied the TypeScript check. I'll note that and proceed with the full review based on the code reads.

---

## WI836 Code Review — Cycle 1 of 2

**Reviewer: Hawkeye | Commit: b74570d**

---

### HIGH: `searchMailbox()` in graph-mail.ts

**1. Hits `/me/messages` (mailbox-wide)?**
**FAIL** — Line 597: `this.graphHttp.get<...>('/me/messages', ...)`.

The `graphHttp` axios instance is created at line 138 with `baseURL: 'https://graph.microsoft.com/v1.0/users/${this.config.mailboxId}'`. Appending `/me/messages` to that base produces the URL:

```

> Note: `npx tsc --noEmit` was run separately and confirmed clean (no output = no errors).

---

## Priority Checks

| Check | Result | Evidence |
|-------|--------|----------|
| searchMailbox hits /me/messages (not folder-scoped) | ❌ | graph-mail.ts:597 — `/me/messages` on a `users/{id}`-based axios instance produces invalid URL `…/users/{mailboxId}/me/messages` |
| ConsistencyLevel: eventual header present | ✅ | graph-mail.ts:601 — `ConsistencyLevel: 'eventual'` present in headers |
| $search query double-quoted for KQL | ✅ | graph-mail.ts:604 — `` $search: `"${query}"` `` correctly quotes the term |
| Returns GraphMessage[] | ✅ | graph-mail.ts:593 — `Promise<GraphMessage[]>` return type; line 610 returns `response.data?.value ?? []` |
| Override requires ≥3 messages | ✅ | classifier.ts:140 — `if (mailboxMessages.length >= 3)` |
| Override requires ≥0.70 concentration confidence | ✅ | classifier.ts:145 — `folderConcentration.confidence >= 0.70` |
| Override requires folderConcentration.count ≥3 | ✅ | classifier.ts:146 — `folderConcentration.count >= 3` |
| Override only fires for DIFFERENT member than DB match | ✅ | classifier.ts:144 — `folderConcentration.topMember !== dbResult.handler.teamMember` |
| DB match still wins when thresholds not met | ✅ | classifier.ts:167–174 — unconditional return after try/catch block |
| searchMailbox failure caught best-effort | ✅ | classifier.ts:162–163 — catch adds audit trail entry, no re-throw, falls through to DB match |
| analyzeMailboxConcentration returns correct shape | ✅ | classifier.ts:462–466 explicit return type; line 516 returns `{ topMember, destination, confidence, count }` |
| analyzeMailboxConcentration confidence ratio correct | ✅ | classifier.ts:513 — `topCount / totalCount` |
| analyzeMailboxConcentration handles empty messages | ✅ | classifier.ts:496–499 — `memberCounts.size === 0` guard returns null result |
| folder-searcher delegation is pure pass-through | ✅ | folder-searcher.ts:161–162 — single-line delegation, no added logic |
| TS strict compliance (no implicit any) | ✅ | No new untyped params; `(err as Error)` is explicit cast at classifier.ts:163 |
| npx tsc --noEmit passes clean | ✅ | Confirmed — zero output, zero errors |

---

## Issues Found

### Critical

**C1 — Wrong Graph API path in `searchMailbox()` — will 404 at runtime**
- **File:** `src/services/graph-mail.ts` line 597
- **Problem:** `graphHttp` has `baseURL: https://graph.microsoft.com/v1.0/users/{mailboxId}`. Calling `.get('/me/messages')` resolves to `…/users/{mailboxId}/me/messages` — an invalid Graph endpoint that will return 404 or unexpected results. `/me/messages` is a delegated-flow path only (requires user sign-in token). This service uses client_credentials (app-only) flow.
- **Fix:** Change the path from `/me/messages` to `/messages`. This produces `…/users/{mailboxId}/messages?$search=...`, which is the correct mailbox-wide search endpoint for app-only flows.

```ts
// Before (line 597):
const response = await this.graphHttp.get<{ value: GraphMessage[] }>(
  `/me/messages`,

// After:
const response = await this.graphHttp.get<{ value: GraphMessage[] }>(
  `/messages`,
```

---

### Important

**I1 — `analyzeMailboxConcentration` parentFolderId fallback comment is misleading dead code**
- **File:** `src/engine/classifier.ts` lines 492–499
- **Problem:** Comment says "fall back to parentFolderId heuristic" but the code immediately returns `{ topMember: null, ... }` with no folder-ID logic at all. This is a false promise — any future engineer will expect to find folder-ID matching logic here and be confused.
- **Fix:** Either implement the parentFolderId fallback, or remove the "fall back to parentFolderId heuristic" comment and replace with "cannot determine concentration without recipient match data — return no-match."

---

### Nitpick

**N1 — Defensive `?.value` on response.data is unnecessary (graph-mail.ts:610)**
- `response.data?.value ?? []` — axios throws on non-2xx, so `response.data` is always defined on success. The optional chain is harmless noise.

**N2 — Double-layer `top` default obscures intent (graph-mail.ts:593, classifier.ts:139)**
- `graphMail.searchMailbox` defaults `top = 25`, `folderSearcher.searchMailbox` also defaults `top = 25`, but classifier calls with explicit `top = 20`. Fine at runtime, but the layered defaults make the effective cap non-obvious.

---

## Verdict

**NEEDS-CHANGES — 1 Critical defect (C1)**

C1 is a runtime-breaking bug: `searchMailbox()` will call an invalid Graph endpoint (`/users/{id}/me/messages`) and fail in production with a 404. The entire mailbox override feature is non-functional until this is fixed. All other checks pass — thresholds, fallthrough logic, concentration math, delegation, error handling, and TypeScript compliance are all correct.

**Required fixes before cycle 2:**
1. ✅ Fix C1: Change `/me/messages` → `/messages` in `graph-mail.ts:597`
2. ✅ Fix I1: Remove or implement the parentFolderId fallback comment in `classifier.ts:492–494`

No scope creep. Fix only these two items.

---
## Cycle 2 Re-review (b74570d → 97605da)

| Fix | Result |
|-----|--------|
| Fix 1: /messages at call site (not /me/messages) | ✅ |
| Fix 1: ConsistencyLevel header still present | ✅ |
| Fix 2: parentFolderId dead code gone from analyzeMailboxConcentration | ✅ |
| Original thresholds (≥3 msgs, ≥0.70 confidence, ≥3 count) intact | ✅ |
| DB fallback still reachable | ✅ |
| TS clean | ✅ |

## Cycle 2 Verdict: PASS
