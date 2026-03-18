# CC Brief: WI836 — Vendorply mailbox-wide search overrides DB match

## Working directory
`/home/fredw/projects/skunkworks/vendorply-email-triage/`

## The Bug

In `src/engine/classifier.ts` at the Layer 2 DB vendor lookup block (around line 133),
when `dbResult.handler.confidence >= 0.80` the code does a hard `return` immediately —
before consulting the mailbox for historical routing patterns:

```typescript
if (dbResult.handler.confidence >= 0.80) {
  return {              // ← BUG: skips mailbox check entirely
    destination: dbResult.handler.destination,
    teamMember: dbResult.handler.teamMember,
    confidence: dbResult.handler.confidence,
    auditTrail,
    matchedRule: "db_vendor_lookup",
    executedLayers,
  };
}
```

This means a DB vendor record always wins over 10+ historical emails in the mailbox
that were already routed to a different team member.

## The Fix — Three files

---

### 1. `src/services/graph-mail.ts` — add `searchMailbox()` after `searchSentItems()` (around line 582)

Add this method after the `searchSentItems` method:

```typescript
  /**
   * Searches the entire mailbox for messages matching a KQL query.
   * Uses /me/messages?$search for mailbox-wide search (not folder-scoped).
   *
   * @param query - KQL search string (e.g. vendor name or sender domain).
   * @param top   - Maximum number of results (default 25).
   * @returns Matching messages from any folder in the mailbox.
   */
  async searchMailbox(query: string, top = 25): Promise<GraphMessage[]> {
    const token = await this.authenticate();

    const response = await this.graphHttp.get<{ value: GraphMessage[] }>(
      `/me/messages`,
      {
        headers: {
          Authorization: `Bearer ${token}`,
          ConsistencyLevel: 'eventual',  // required for $search
        },
        params: {
          $search: `"${query}"`,
          $top: top,
        },
      },
    );

    return response.data?.value ?? [];
  }
```

---

### 2. `src/engine/folder-searcher.ts` — add `searchMailbox()` delegation at the bottom of the class (before the closing `}`)

The `FolderSearcher` class already holds a `this.graphMail` reference (type `GraphMailService`).
Add a pass-through method:

```typescript
  async searchMailbox(query: string, top = 25): Promise<GraphMessage[]> {
    return this.graphMail.searchMailbox(query, top);
  }
```

---

### 3. `src/engine/classifier.ts` — two changes

#### 3a. Replace the hard return at `dbResult.handler.confidence >= 0.80` block

Current code (around line 131):
```typescript
          if (dbResult.handler.confidence >= 0.80) {
            return {
              destination: dbResult.handler.destination,
              teamMember: dbResult.handler.teamMember,
              confidence: dbResult.handler.confidence,
              auditTrail,
              matchedRule: "db_vendor_lookup",
              executedLayers,
            };
          }
```

Replace with:
```typescript
          if (dbResult.handler.confidence >= 0.80) {
            // Before accepting DB match, check if existing emails concentrate on a different member
            if (this.folderSearcher && dbResult.vendorName) {
              try {
                const mailboxMessages = await this.folderSearcher.searchMailbox(dbResult.vendorName, 20);
                if (mailboxMessages.length >= 3) {
                  const folderConcentration = this.analyzeMailboxConcentration(mailboxMessages);
                  if (
                    folderConcentration.topMember &&
                    folderConcentration.topMember !== dbResult.handler.teamMember &&
                    folderConcentration.confidence >= 0.70 &&
                    folderConcentration.count >= 3
                  ) {
                    auditTrail.push(
                      `DB match (${dbResult.handler.teamMember}, ${dbResult.handler.confidence}) overridden by mailbox concentration: ` +
                      `${folderConcentration.count} existing emails route to ${folderConcentration.topMember} (confidence ${folderConcentration.confidence})`
                    );
                    return {
                      destination: folderConcentration.destination,
                      teamMember: folderConcentration.topMember,
                      confidence: folderConcentration.confidence,
                      auditTrail,
                      matchedRule: "mailbox_concentration_override",
                      executedLayers,
                    };
                  }
                }
              } catch (err) {
                auditTrail.push(`Mailbox search failed — using DB match: ${(err as Error).message}`);
              }
            }
            // DB match stands (no mailbox override)
            return {
              destination: dbResult.handler.destination,
              teamMember: dbResult.handler.teamMember,
              confidence: dbResult.handler.confidence,
              auditTrail,
              matchedRule: "db_vendor_lookup",
              executedLayers,
            };
          }
```

#### 3b. Add `analyzeMailboxConcentration()` private method at the bottom of the `Classifier` class (after `resolveDestination`)

This method:
- Takes `GraphMessage[]`
- Uses `parentFolderId` on each message to identify which team member folder it belongs to
  (by building a map from `rulesEngine.getTeamMembers()` + `folderSearcher`'s folder list)
- Counts messages per team member
- Returns the top member, their destination (via `resolveDestination`), a confidence score,
  and the count

```typescript
  private analyzeMailboxConcentration(messages: GraphMessage[]): {
    topMember: string | null;
    destination: string;
    confidence: number;
    count: number;
  } {
    // Build a map of working_folder name -> team member
    // We use folder display-name matching (same as FolderSearcher.search)
    const teamMembers = this.rulesEngine.getTeamMembers();

    // Count messages per team member by checking toRecipients addresses.
    // Graph's /me/messages returns messages from the shared mailbox; recipients
    // indicate which team member the message was addressed to.
    // Fallback: count by team member name appearing in toRecipients display name.
    const memberCounts = new Map<string, number>();

    for (const msg of messages) {
      for (const recipient of msg.toRecipients ?? []) {
        const addr = (recipient.emailAddress?.address ?? '').toLowerCase();
        const displayName = (recipient.emailAddress?.name ?? '').toLowerCase();
        const match = teamMembers.find(
          (m) =>
            (m.personal_email && addr.includes(m.personal_email.toLowerCase())) ||
            displayName.includes(m.name.toLowerCase()) ||
            addr.includes(m.name.toLowerCase().replace(/\s+/g, '.'))
        );
        if (match) {
          memberCounts.set(match.name, (memberCounts.get(match.name) ?? 0) + 1);
        }
      }
    }

    // If toRecipients didn't yield clear results, fall back to parentFolderId heuristic.
    // FolderSearcher doesn't cache folder IDs, so we use the count map we have.
    if (memberCounts.size === 0) {
      // No recipient-based matches — cannot determine concentration
      return { topMember: null, destination: 'Needs Triage', confidence: 0, count: 0 };
    }

    // Find the member with the most messages
    let topMember: string | null = null;
    let topCount = 0;
    let totalCount = 0;
    for (const [member, count] of memberCounts) {
      totalCount += count;
      if (count > topCount) {
        topCount = count;
        topMember = member;
      }
    }

    const confidence = totalCount > 0 ? topCount / totalCount : 0;
    const destination = topMember ? this.resolveDestination(topMember) : 'Needs Triage';

    return { topMember, destination, confidence, count: topCount };
  }
```

Note on `personal_email`: The `TeamMember` interface in `rules-engine.ts` has `personal_email: string | null`.
Use it carefully (null check already included above).

---

## Key Interfaces for Reference

```typescript
// GraphMessage (src/services/graph-mail.ts)
interface GraphMessage {
  id: string;
  parentFolderId: string;          // ← folder the message lives in
  toRecipients: Array<{
    emailAddress: { name: string; address: string };
  }>;
  receivedDateTime: string;
  // ...
}

// TeamMember (src/engine/rules-engine.ts)
interface TeamMember {
  name: string;
  personal_email: string | null;
  working_folder: string;
  is_default: boolean;
  // ...
}
```

## Gate Checks (run after changes)
```bash
grep -n "searchMailbox\|/me/messages\|ConsistencyLevel" src/services/graph-mail.ts | head -6
grep -n "searchMailbox" src/engine/folder-searcher.ts | head -4
grep -n "mailbox_concentration_override\|mailboxMessages\|folderConcentration\|analyzeMailboxConcentration" src/engine/classifier.ts | head -8
grep -n "db_vendor_lookup\|matchedRule.*db" src/engine/classifier.ts | head -4
grep -n "analyzeMailboxConcentration" src/engine/classifier.ts | head -3
grep -n "catch.*err\|mailbox.*fail\|using DB match" src/engine/classifier.ts | head -4
npx tsc --noEmit 2>&1 | head -10
```

## Commit message
```
WI836: mailbox-wide folder search overrides DB match when existing emails concentrate on different member
```
