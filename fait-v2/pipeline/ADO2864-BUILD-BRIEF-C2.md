# ADO#2864 — FAIT v2 In-App Feedback Submission — BUILD Cycle 2

## Context
Hawkeye reviewed C1 (commit `3a9bf2d`). NEEDS-CHANGES — 2 targeted fixes required. Both are one-liners. No scope creep.

## Working directory
`/home/fredw/projects/fip/fait-v2/`

## ADO DevOps comment (add after fix):
Project: Fortress, ID: 2864
```
**[Tony Stark — BUILD cycle 2]**
Commit {hash}: Fixed GUID format (ToString("N")[..32] → ToString()) and hardcoded callback token in DispatchToJarvisAsync. Build: SUCCEEDED.
```

## Fix 1 — FeedbackSubmission.Id uses wrong GUID format

**File:** `src/FortressAI.V2.Web/Data/Models/FeedbackSubmission.cs` line 5

```diff
- public string Id { get; set; } = Guid.NewGuid().ToString("N")[..32];
+ public string Id { get; set; } = Guid.NewGuid().ToString();
```

**Why:** Column is `varchar(36)`, every other model uses `.ToString()` (36-char hyphenated). `("N")[..32]` produces 32-char no-dash IDs — inconsistent with the schema and rest of codebase.

## Fix 2 — DispatchToJarvisAsync hardcodes callback token

**File:** `src/FortressAI.V2.Web/Program.cs` — in `DispatchToJarvisAsync`

The status endpoint at line ~372 already reads:
```csharp
var expectedToken = config["Feedback:InternalToken"] ?? "fait-v2-internal-feedback-token";
```

But the Jarvis dispatch message body (line ~449) hardcodes the literal:
```
with headers: X-Internal-Token: fait-v2-internal-feedback-token
```

**Fix:** Before building the payload in `DispatchToJarvisAsync`, resolve the token from config and interpolate it:
```csharp
var internalToken = config["Feedback:InternalToken"] ?? "fait-v2-internal-feedback-token";
// Then in the message body use the variable instead of the literal:
// ... X-Internal-Token: {internalToken} ...
```

Exact interpolation syntax depends on how the message is built — use string interpolation or format to inject the resolved value.

## Verification
- `dotnet build` — must be 0 errors, 0 warnings
- Confirm the two lines are changed

## MANDATORY: Use Claude Code CLI
```bash
CLAUDE_CODE_ENTRYPOINT=ado-pipeline \
CLAUDE_CODE_DISABLE_AUTO_MEMORY=1 \
CLAUDE_BASH_MAINTAIN_PROJECT_WORKING_DIR=1 \
CLAUDE_CODE_GLOB_TIMEOUT_SECONDS=30 \
cat << 'EOF' | claude --model sonnet --print --dangerously-skip-permissions
Fix ADO#2864 C2 in /home/fredw/projects/fip/fait-v2/:

Fix 1 — src/FortressAI.V2.Web/Data/Models/FeedbackSubmission.cs
Change: Guid.NewGuid().ToString("N")[..32]
To:     Guid.NewGuid().ToString()

Fix 2 — src/FortressAI.V2.Web/Program.cs — DispatchToJarvisAsync
The function already takes config as a parameter (or has access to config in scope).
Before building the Jarvis payload, resolve:
  var internalToken = config["Feedback:InternalToken"] ?? "fait-v2-internal-feedback-token";
Then replace the hardcoded literal "fait-v2-internal-feedback-token" in the callback instructions 
sent to Jarvis with the variable value.

After edits: run dotnet build in /home/fredw/projects/fip/fait-v2/ and confirm 0 errors.
Then git add -A && git commit -m "fix(ADO#2864): fix GUID format and resolve callback token from config"
Report the commit hash.
EOF
```
