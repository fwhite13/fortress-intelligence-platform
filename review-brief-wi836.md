# Review Brief: WI836 — Vendorply triage: folder search overrides DB match

You are Hawkeye (Clint Barton), code reviewer. Review cycle 1 of 2.

## What Was Built
WI836 — mailbox-wide folder search override for DB vendor matches. Commit `b74570d`.
3 files modified: `src/services/graph-mail.ts`, `src/engine/folder-searcher.ts`, `src/engine/classifier.ts`

## Your Task
Carefully analyze the code in the repo at `/home/fredw/projects/skunkworks/vendorply-email-triage/`.
Read the three modified files and answer every check below with precise line-number evidence.

## Priority Checks

### HIGH: searchMailbox() in graph-mail.ts

Read `src/services/graph-mail.ts`. Find the `searchMailbox()` method. Verify:
1. Does it hit `/me/messages` (mailbox-wide), NOT `/mailFolders/{id}/messages`?
2. Is `ConsistencyLevel: 'eventual'` header present (required by Graph API for `$search`)?
3. Is the query wrapped correctly: `$search: '"${query}"'` (quoted for KQL)?
4. Does it return `GraphMessage[]` (same type as `searchFolder`)?

### HIGH: Override thresholds in classifier.ts

Read `src/engine/classifier.ts`. Find the mailbox override logic. Verify exact thresholds:
1. Does the mailbox search only trigger override logic when `mailboxMessages.length >= 3`?
2. Is `folderConcentration.confidence >= 0.70` threshold present?
3. Is `folderConcentration.count >= 3` threshold present (not just message count)?
4. Does the override only fire when `folderConcentration.topMember !== dbResult.handler.teamMember`?

### HIGH: DB match fallthrough logic in classifier.ts

Verify the complete logic flow:
1. If `mailboxMessages.length < 3` → does it fall through to DB match return?
2. If override thresholds not met → does it fall through to DB match return?
3. If `searchMailbox` throws → is there a catch block that adds audit trail entry and falls through to DB match?
4. Is the original `db_vendor_lookup` `matchedRule` return still present and reachable?

### HIGH: analyzeMailboxConcentration() correctness

Read the `analyzeMailboxConcentration` method in classifier.ts. Verify:
1. Does it count messages per team member — by `toRecipients` email address matching known member emails, OR by `parentFolderId` mapping to member folders?
2. Is `confidence` calculated as `topCount / totalMessages` (or equivalent ratio)?
3. Does it return `{ topMember, destination, confidence, count }` with correct types?
4. Does it handle edge cases: empty messages, all-same-member (100% concentration), tied members?

### MEDIUM: folder-searcher.ts delegation

Read `src/engine/folder-searcher.ts`. Find `searchMailbox()`. Verify:
1. Does it call `this.graphMail.searchMailbox(query, top)` directly?
2. Is it a pure pass-through with NO logic added?

### MEDIUM: Mailbox search failure handling (classifier.ts)

Verify:
1. Is the `searchMailbox` call inside classifier in a try/catch?
2. On exception: is an audit trail entry added with the error message?
3. On exception: does DB match return still execute? (no re-throw, no undefined return)

### LOW: TypeScript strict compliance

Verify:
1. Does `analyzeMailboxConcentration` have an explicit return type annotation?
2. Are there no implicit `any` types introduced?
3. Run `npx tsc --noEmit` in `/home/fredw/projects/skunkworks/vendorply-email-triage/` and report the result.

## Output Format
For each check, state: PASS or FAIL, with exact line number evidence.
Give a final verdict: PASS / NEEDS-CHANGES / FAIL.
List any issues found, categorized as Critical / Important / Nitpick.
