# Build Report: WI836
**Vendorply triage: folder search overrides DB match when existing emails found**

---

## Summary

Fixed a hard `return` in `src/engine/classifier.ts` that caused the ≥0.80-confidence DB vendor
match to bypass all mailbox history checks. The classifier now performs a mailbox-wide search
before accepting the DB result, and overrides it when ≥3 existing emails concentrate on a
different team member with ≥0.70 confidence.

---

## CC Invocation

```bash
cd /home/fredw/projects/skunkworks/vendorply-email-triage
cat ~/projects/fait-for-excel/cc-brief-wi836.md | claude --model sonnet -p --dangerously-skip-permissions
```

Model: **CC Sonnet**

---

## Changes Made

### `src/services/graph-mail.ts`
- Added `searchMailbox(query: string, top = 25): Promise<GraphMessage[]>` method after `searchSentItems`
- Uses `/me/messages?$search` (mailbox-wide, not folder-scoped)
- Passes `ConsistencyLevel: eventual` header (required by Graph API for `$search`)
- Returns `response.data?.value ?? []` (safe empty-array fallback)

### `src/engine/folder-searcher.ts`
- Added `searchMailbox(query: string, top = 25): Promise<GraphMessage[]>` delegation method
- Passes through to `this.graphMail.searchMailbox(query, top)`

### `src/engine/classifier.ts`
- Added `GraphMessage` import
- Replaced hard `return` at `dbResult.handler.confidence >= 0.80` with mailbox concentration check:
  - Searches mailbox for `vendorName` (20 results)
  - If ≥3 messages found, calls `analyzeMailboxConcentration()`
  - Overrides DB match if: topMember ≠ dbResult.handler.teamMember AND confidence ≥ 0.70 AND count ≥ 3
  - Override pushes audit trail entry describing the DB match being superseded
  - Exception caught (best-effort) — falls back to DB match on failure
- Added `analyzeMailboxConcentration(messages: GraphMessage[])` private method:
  - Counts `toRecipients` hits per team member (by personal_email, display name, or address pattern)
  - Returns `{ topMember, destination, confidence, count }` 
  - Confidence = topCount / totalCount
  - Falls back to `{ topMember: null, … }` if no recipient-based matches found

---

## Gate Check Results

| Check | Result |
|-------|--------|
| `searchMailbox` in graph-mail.ts | ✅ Line 593 |
| `/me/messages` endpoint used | ✅ Line 597 |
| `ConsistencyLevel: eventual` header | ✅ Line 601 |
| `searchMailbox` delegation in folder-searcher.ts | ✅ Lines 161-162 |
| `mailboxMessages` / `folderConcentration` in classifier.ts | ✅ Lines 139-150 |
| `mailbox_concentration_override` matchedRule | ✅ Line ~153 |
| `db_vendor_lookup` still returned when no override | ✅ Lines ~166-172 |
| `analyzeMailboxConcentration` method exists | ✅ Line 462 |
| Mailbox search failure caught (best-effort) | ✅ Lines 162-163 |
| TypeScript compiles | ✅ Clean (no errors) |

---

## Commit

```
b74570d WI836: mailbox-wide folder search overrides DB match when existing emails concentrate on different member
```

---

## Self-Review Checklist

- [x] Bug root cause understood and addressed (hard return replaced)
- [x] `searchMailbox` is best-effort — exception caught, falls back to DB match
- [x] Override only triggers when count ≥ 3 AND confidence ≥ 0.70 (avoids noise from 1-2 stray messages)
- [x] Audit trail entry written on override (diagnostic visibility)
- [x] Audit trail entry written on mailbox search failure
- [x] `matchedRule` distinguishes override (`mailbox_concentration_override`) from normal DB match (`db_vendor_lookup`)
- [x] TypeScript compiles clean
- [x] No scope creep — only the three specified files were modified

---

## Status
**BUILD COMPLETE — Ready for Clint's review.**

## Cycle 2 Fix

**Commit:** 97605da
**Fix 1:** graph-mail.ts — `/me/messages` → `/messages`
**Fix 2:** classifier.ts — dead parentFolderId comment removed
**TS:** clean
