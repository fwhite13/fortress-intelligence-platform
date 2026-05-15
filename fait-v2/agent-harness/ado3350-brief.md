# ADO#3350 Build Brief — FAIT Harness: Fix kbFlags fail-open

## Context

Two related fixes required. Both must be made.

---

## Part 1: harness-server.js — null kbFlags fail-CLOSED for personal/team KBs

File: `/home/fredw/projects/fip/fait-v2/agent-harness/harness-server.js`

### Current code (around line 1363):

```javascript
async function executeKbSearch(query, kbType, userId, kbAccess, kbFlags) {
    // ADO#3316 — Preference gate: check KbFlags BEFORE entitlement check
    if (kbFlags !== null && kbFlags !== undefined) {
        if (kbType === 'corp') {
            const corpEnabled = kbFlags.CorpKbEnabled ?? kbFlags.corpKbEnabled ?? null;
            if (corpEnabled === false) {
                console.warn(`[harness] executeKbSearch: corp KB disabled in user preferences for userId=${userId}`);
                return { text: 'Knowledge base access not authorized.', sources: [] };
            }
        }
        if (kbType === 'personal') {
            const personalEnabled = kbFlags.PersonalKbEnabled ?? kbFlags.personalKbEnabled ?? null;
            if (personalEnabled === false) {
                console.warn(`[harness] executeKbSearch: personal KB disabled in user preferences for userId=${userId}`);
                return { text: 'Knowledge base access not authorized.', sources: [] };
            }
        }
        if (kbType === 'team') {
            const teamEnabled = kbFlags.TeamKbEnabled ?? kbFlags.teamKbEnabled ?? null;
            if (teamEnabled === false) {
                console.warn(`[harness] executeKbSearch: team KB disabled in user preferences for userId=${userId}`);
                return { text: 'Knowledge base access not authorized.', sources: [] };
            }
        }
    } else {
        // kbFlags is null/undefined — fail-open, proceed to entitlement check only (no regression for callers that don't send KbFlags)
        console.debug(`[harness] executeKbSearch: kbFlags not provided for userId=${userId}, skipping preference gate`);
    }
```

### Problem:

When `kbFlags` is null/undefined, the current code fails open (the else branch just logs and falls through). The fix:
- kbFlags null/undefined → personal KB: **BLOCKED** (return empty / error)
- kbFlags null/undefined → team KB: **BLOCKED** (return empty / error)
- kbFlags null/undefined → corp KB: **ALLOWED** (fail-open is acceptable for corp, it's a shared resource)

### Required change:

Replace the `else` branch with logic that:
1. For `kbType === 'personal'`: block — `return { text: 'Knowledge base access not authorized.', sources: [] }` with a warn log
2. For `kbType === 'team'`: block — `return { text: 'Knowledge base access not authorized.', sources: [] }` with a warn log
3. For `kbType === 'corp'`: allow (fall through to the entitlement check as before)

The new else block should look like:
```javascript
    } else {
        // kbFlags is null/undefined — ADO#3350: fail-CLOSED for personal/team, fail-open for corp
        if (kbType === 'personal') {
            console.warn(`[harness] executeKbSearch: kbFlags null — blocking personal KB for userId=${userId}`);
            return { text: 'Knowledge base access not authorized.', sources: [] };
        }
        if (kbType === 'team') {
            console.warn(`[harness] executeKbSearch: kbFlags null — blocking team KB for userId=${userId}`);
            return { text: 'Knowledge base access not authorized.', sources: [] };
        }
        // corp: fail-open acceptable — shared resource, no per-user filter
        console.debug(`[harness] executeKbSearch: kbFlags null — allowing corp KB for userId=${userId}`);
    }
```

Make ONLY this change in `executeKbSearch`. Do NOT touch anything else in the file.

---

## Part 2: Blazor — Add KbFlags to TurnRequest and always serialize it

### Step A: Add KbFlags record and property to TurnRequest

File: `/home/fredw/projects/fip/fait-v2/src/FortressAI.V2.Web/Services/IUserAgentRuntime.cs`

#### Current TurnRequest (around line 45):
```csharp
public record TurnRequest(
    string UserId,
    string Message,
    string? SystemPrompt = null,
    string? SessionId = null,
    bool TaskMode = false,
    bool ForceTaskMode = false,
    List<ChatHistoryEntry>? History = null,
    string? PluginAgentId = null,        // §6.1 — active specialist agent
    string? UserEmail = null,            // §G1 — Entra UPN for CC identity context
    bool IsScheduledTask = false,        // §G7 — signals harness to use async-safe approval path
    bool KbWriteAllowed = true           // §G3 — KB write permission for plugin agents
);
```

#### Required change:

1. Add a new `KbFlags` record just before `TurnRequest`:
```csharp
/// <summary>ADO#3350 — KB toggle flags sent to harness on every turn. Never null — use all-false when user has no KB preferences.</summary>
public record TurnKbFlags(
    bool PersonalKbEnabled = false,
    bool TeamKbEnabled = false,
    bool CorpKbEnabled = false
);
```

2. Add `TurnKbFlags? KbFlags = null` as a parameter to `TurnRequest`:
```csharp
public record TurnRequest(
    string UserId,
    string Message,
    string? SystemPrompt = null,
    string? SessionId = null,
    bool TaskMode = false,
    bool ForceTaskMode = false,
    List<ChatHistoryEntry>? History = null,
    string? PluginAgentId = null,        // §6.1 — active specialist agent
    string? UserEmail = null,            // §G1 — Entra UPN for CC identity context
    bool IsScheduledTask = false,        // §G7 — signals harness to use async-safe approval path
    bool KbWriteAllowed = true,          // §G3 — KB write permission for plugin agents
    TurnKbFlags? KbFlags = null          // ADO#3350 — KB toggle flags; never null in practice
);
```

### Step B: Populate KbFlags in ChatView.razor

File: `/home/fredw/projects/fip/fait-v2/src/FortressAI.V2.Web/Components/Chat/ChatView.razor`

#### Current TurnRequest construction (around line 916):
```csharp
            var request = new TurnRequest(
                UserId: _userId,
                Message: BuildContextualMessage(userMessage),
                SystemPrompt: BuildSystemPrompt(),
                TaskMode: false,
                ForceTaskMode: _forceTaskMode,
                History: contextHistory,
                PluginAgentId: _activePluginAgentId,   // §6.1
                UserEmail: _userEmail,                  // §G1 — Entra UPN
                KbWriteAllowed: activePlugin?.AllowKbWrite ?? true  // §G3
            );
```

#### Required change:

Add `KbFlags` as a non-null parameter using the existing toggle state variables `_fortressKbEnabled` (corp) and `_personalKbEnabled` (personal):

```csharp
            var request = new TurnRequest(
                UserId: _userId,
                Message: BuildContextualMessage(userMessage),
                SystemPrompt: BuildSystemPrompt(),
                TaskMode: false,
                ForceTaskMode: _forceTaskMode,
                History: contextHistory,
                PluginAgentId: _activePluginAgentId,   // §6.1
                UserEmail: _userEmail,                  // §G1 — Entra UPN
                KbWriteAllowed: activePlugin?.AllowKbWrite ?? true,  // §G3
                KbFlags: new TurnKbFlags(               // ADO#3350 — always non-null
                    PersonalKbEnabled: _personalKbEnabled,
                    TeamKbEnabled: false,               // team KB UI not yet implemented
                    CorpKbEnabled: _fortressKbEnabled
                )
            );
```

Note: `_fortressKbEnabled` corresponds to the corp/Fortress KB. `_personalKbEnabled` is the personal KB. Team KB has no UI toggle yet so it's always false.

---

## Commit Instructions

After making all changes:

1. In `/home/fredw/projects/fip/fait-v2/agent-harness/`:
   - Stage and commit harness-server.js change
   - Commit message: `fix(harness): null kbFlags fail-CLOSED for personal/team KB (ADO#3350)`

2. In `/home/fredw/projects/fip/fait-v2/`:  
   - Stage and commit IUserAgentRuntime.cs and ChatView.razor changes
   - Commit message: `feat(blazor): add TurnKbFlags to TurnRequest, always serialize KB flags (ADO#3350)`

3. Push both commits to origin/main

Do NOT modify any other files. Do NOT change any build configurations, CSS, or other unrelated files.
