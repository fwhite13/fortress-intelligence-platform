# Build Report — FAIT WI #1669
## AI fabricates email address instead of calling m365 tool

**Date:** 2026-04-08  
**Engineer:** Tony Stark (software-engineer)  
**Commit:** `c4971f8`  
**Build:** ✅ 0 errors, 31 warnings (all pre-existing)

---

## Where the System Prompt Lives

The FAIT AI system prompt is assembled dynamically in `ChatView.razor` each time a message is sent. There is no single `.txt` file — the prompt is built in layers:

1. **Project instructions** — `BuildSystemPromptFromProject(project)` (~line 963)
2. **Personality prefix** — `AssistantConfigService.GetPersonalitySystemPrompt(config, displayName, email)` (~line 486)
3. **Artifact instructions** — `GetArtifactSystemPrompt()` (~line 1294)
4. **KB context** — injected when KB toggles are on
5. **Tool guidance** — injected per-tool-server when MCP tools are available:
   - DevOps guidance (~line 714)
   - **M365 guidance (~line 744)** ← primary injection point for email rules
   - Search guidance (~line 791)

---

## How `CURRENT_USER_EMAIL` Is Now Surfaced to the AI

**Before this fix:** `GetPersonalitySystemPrompt` only accepted `userDisplayName`. User email was never passed to the AI.

**After this fix:**

### `AssistantConfigService.cs` — signature change
```csharp
// BEFORE
public string GetPersonalitySystemPrompt(UserAssistantConfig config, string? userDisplayName = null)

// AFTER
public string GetPersonalitySystemPrompt(UserAssistantConfig config, string? userDisplayName = null, string? userEmail = null)
```

New injection (appended to personality prefix when email is non-null):
```csharp
if (!string.IsNullOrWhiteSpace(userEmail))
    prefix += $" The authenticated user's own email address is {userEmail}. Use this as the canonical source for the current user's email — do not look it up or guess it.";
```

### `ChatView.razor` — call site update (~line 486)
```csharp
// BEFORE
var personalityPrefix = ConfigSvc.GetPersonalitySystemPrompt(_assistantConfig, Session.CurrentUser?.DisplayName);

// AFTER
var personalityPrefix = ConfigSvc.GetPersonalitySystemPrompt(_assistantConfig, Session.CurrentUser?.DisplayName, Session.CurrentUser?.Email);
```

`Session.CurrentUser` is a `UserSessionService` holding the authenticated `AppUser` record. `AppUser.Email` is populated from Entra claims during login and stored in the database. This email appears in the system prompt on **every** chat request.

---

## Anti-Fabrication Instruction Added to M365 Guidance

The `m365Guidance` block in `ChatView.razor` (~line 744) was extended with:

```
**CRITICAL — Email addresses:**
- NEVER fabricate, guess, or infer email addresses. This is a strict rule with no exceptions.
- If you need a recipient's email address and do not have it from the conversation context, you MUST either:
  (a) Call `m365__list_emails` or search the inbox to look up the person, OR
  (b) Ask the user to provide the email address directly.
- The authenticated user's own email address is provided in your context (see system prompt). Use it as the canonical source — do not look it up.
- Never use personal email domains (gmail.com, outlook.com, hotmail.com, yahoo.com, etc.) for work contacts unless the user explicitly provides such an address.
```

This block is only injected when m365 tools are available for the session, which is the exact context where email address handling matters.

---

## Files Changed

| File | Change |
|------|--------|
| `src/FortressAI.Web/Services/AssistantConfigService.cs` | Added `userEmail` param; inject email sentence into personality prefix |
| `src/FortressAI.Web/Components/Chat/ChatView.razor` | Pass `Session.CurrentUser?.Email` to `GetPersonalitySystemPrompt`; extend m365 guidance with anti-fabrication block |

---

## Build Result

```
dotnet build ~/projects/fip/fait/
Build succeeded.
    0 Error(s)
    31 Warning(s) — all pre-existing
```

---

## Known Edge Cases / Things Clint Should Scrutinize

- The email injection is in the **personality prefix** (always-on system prompt), not just the m365 guidance block. This is intentional — the AI should know the user's email even if m365 tools aren't active (e.g., for drafting email text).
- `AppUser.Email` is populated from Entra claims (`preferred_username` / `upn`) during the MSAL login flow. For stub/test auth users it may be whatever was seeded. Non-Entra users have it set at account creation.
- No migration needed — `AppUser.Email` has always been stored.

---

## How to Test Locally

1. Log in as Rob Nethery (or any Entra user with m365 connected)
2. Enable m365 tools in the chat interface
3. Ask: "Send an email to me" — FAIT should use the injected email, not fabricate one
4. Ask: "Send an email to John Smith" — FAIT should call `m365__list_emails` to find John's address, not guess it
5. Ask FAIT what your email address is — it should report the correct address from context

---

## Cycle 2 — Gate "email is in context" on non-null email

**Date:** 2026-04-08
**Engineer:** Tony Stark (software-engineer)
**Commit:** `270f61f`
**Build:** ✅ 0 errors, 31 warnings (all pre-existing)

### Issue
The `m365Guidance` block unconditionally stated "The authenticated user's own email address is provided in your context" — but that's only true when `Session.CurrentUser?.Email` is non-null and was actually injected by `GetPersonalitySystemPrompt`. When the email claim is missing, the sentence was a lie.

### Fix Applied (`ChatView.razor`, lines 740–760)

Added `var userEmail = Session.CurrentUser?.Email;` before the `m365Guidance` string is built.

Added `var ownEmailBullet` conditional:
- **Email available:** `"- The authenticated user's own email address is provided in your context (see system prompt). Use it as the canonical source — do not look it up."`
- **Email null:** `"- The authenticated user's email address is not available in this session — use m365 tools to look up recipient addresses as needed."`

Changed `m365Guidance` from `@"..."` to `$@"..."` (interpolated verbatim string) and replaced the hardcoded bullet with `{ownEmailBullet}`.

### Files Changed

| File | Change |
|------|--------|
| `src/FortressAI.Web/Components/Chat/ChatView.razor` | Lines 740–760: gate own-email bullet on `Session.CurrentUser?.Email` nullability |

### Build Result
```
Build succeeded.
    0 Error(s)
    31 Warning(s) — all pre-existing
```

### Notes for Clint
- One-liner conditional — no logic risk
- No new variables escape the `hasM365Tools` scope
- The `GetPersonalitySystemPrompt` email injection (line 486) is unchanged — this fix only corrects the guidance text in `m365Guidance` to be truthful about what was actually injected
