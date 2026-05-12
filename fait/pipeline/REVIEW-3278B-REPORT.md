# REVIEW REPORT — ADO#3278 Issue B (KB Auth Scoping)
**Commit:** `0ca05fe8`
**Reviewer:** Clint (pipeline agent)
**Date:** 2026-05-11
**Verdict: NEEDS-CHANGES**

---

## CC Invocation
This review was performed by Clint directly (no separate CC subprocess required for this analysis cycle).

---

## Files Reviewed
- `fait/src/FortressAI.Web/Services/IUserAgentRuntime.cs` — KbFlags record
- `fait/src/FortressAI.Web/Components/Chat/ChatView.razor` — KbFlags construction
- `fait-v2/agent-harness/harness-server.js` — `retrieveFromKbFiltered` + `doKbRetrieval`

---

## Review Findings by Focus Area

### 1. KbFlags Construction in ChatView — PASS
- `Session.UserId` is a `Guid` struct (non-nullable value type). `.ToString()` is always safe; no null risk.
- `PersonalKbUserId` is conditionally set: `hasPersonalKb ? Session.UserId.ToString() : null` (line 915). Correct — only populated when personal KB is actually enabled.
- `TeamIds` is set: `hasTeamKb && _selectedTeamIds?.Any() == true ? _selectedTeamIds.ToList() : null` (line 916). Correct — null when no teams selected.

### 2. `retrieveFromKbFiltered` Filter Structure — PASS (conditional)
The Bedrock `RetrieveCommand` filter structure (lines 165-170):
```js
filter: {
    equals: {
        key: filterKey,
        value: filterValue.toString()
    }
}
```
This matches the AWS Bedrock Knowledge Base metadata filter API shape. The field names (`ownerId`, `teamId`) need to match what was used during document ingestion — review assumes they do.

**Minor concern:** `filterValue.toString()` coerces integer `teamId` values to strings. If team KB documents were indexed with `teamId` as a numeric metadata type, string comparison will return zero results. If indexed as strings (common convention), this is fine. The indexing pipeline should be verified to confirm.

### 3. Fail-Closed Behavior — PASS
- Personal KB: lines 1950-1954 — if `!personalKbUserId`, logs a warning and skips. No unfiltered fallback. Correctly fail-closed.
- Team KB: lines 1958-1965 — if `effectiveTeamIds` is null/empty, logs a warning and skips. No unfiltered fallback. Correctly fail-closed.
- Both paths require positive identity evidence to proceed. Security posture is correct.

### 4. Corp KB Unchanged — PASS
Line 1946: `doKbRetrieval(process.env.CORP_KB_ID, 'Corp KB', message, null, null)`
`null, null` for filterKey/filterValue causes `retrieveFromKbFiltered` to skip the filter block entirely. Corp KB retrieval is unfiltered — as intended. No regression here.

### 5. `_selectedTeamIds` Scope — PASS
- Declared at line 417: `private HashSet<int> _selectedTeamIds = new();`
- Populated from conversation DB data at line 549 (`conversation.TeamKbs.Select(t => t.TeamId).ToHashSet()`)
- Reset to empty on new conversation at line 565
- Consistently maintained through toggle operations (lines 1158-1169)
- Used at line 916 (KbFlags construction) — always initialized, never unset. Correct.

---

## BLOCKER — JavaScript Syntax Errors in harness-server.js

**Severity: BLOCKER — will prevent harness-server.js from loading**

Three lines use single `/` instead of `//` for comments:

| Line | Content |
|------|---------|
| 1945 | `/ Corp KB: no per-user filter — entire KB is team-scoped structurally` |
| 1949 | `/ Personal KB: filter by ownerId = user's GUID` |
| 1957 | `/ Team KB: one retrieval per team ID, each filtered by teamId` |

In JavaScript, a bare `/` at the start of a statement begins a **regex literal**. The parser will attempt to read until a closing `/` delimiter. Since none exists on these lines, the parser hits end-of-line inside an unterminated regex literal and throws a **SyntaxError**. Node.js parses the entire file at startup — this SyntaxError would crash the process on boot, preventing any requests from being served.

**Fix required:** Change all three `/` to `//`.

---

## Summary

| Area | Result |
|------|--------|
| KbFlags construction | PASS |
| retrieveFromKbFiltered filter structure | PASS (verify teamId indexing type) |
| Fail-closed behavior | PASS |
| Corp KB unchanged | PASS |
| `_selectedTeamIds` scope | PASS |
| JS syntax (single-slash comments) | **BLOCKER** |

**Verdict: NEEDS-CHANGES**

One blocker must be fixed before this commit is safe to deploy: the three single-slash pseudo-comments on lines 1945, 1949, 1957 of `harness-server.js` must be corrected to `//`. The security logic itself (filtering, fail-closed, identity scoping) is well-implemented.
