# FAIT WI #1669 — Build Report
**Date:** 2026-04-08
**Issue:** AI fabricated `rnethery@outlook.com` instead of calling m365 tool

---

## Where the System Prompt Lives

The system prompt is assembled in `ChatView.razor` (~line 480–752) via:

1. `BuildSystemPromptFromProject(project)` — project-level instructions
2. `AssistantConfigService.GetPersonalitySystemPrompt(...)` — personality prefix (now includes user email)
3. Artifact instructions
4. KB context
5. m365 guidance block (if m365 tools available)

---

## How `CURRENT_USER_EMAIL` Is Now Surfaced to the AI

`Session.CurrentUser` (type `AppUser`) exposes an `Email` string property.
`ChatView.razor` now passes `Session.CurrentUser?.Email` as the third argument to `GetPersonalitySystemPrompt`.
The method appends the following sentence to the personality prefix when the value is non-null:

> "The authenticated user's own email address is {userEmail}. Use this as the canonical source for the current user's email — do not look it up or guess it."

This appears early in the system prompt, before any tool guidance.

---

## Changes Made

### Fix 1 — `AssistantConfigService.cs`

**File:** `src/FortressAI.Web/Services/AssistantConfigService.cs`

```diff
- public string GetPersonalitySystemPrompt(UserAssistantConfig config, string? userDisplayName = null)
+ public string GetPersonalitySystemPrompt(UserAssistantConfig config, string? userDisplayName = null, string? userEmail = null)
  {
      ...
      if (!string.IsNullOrWhiteSpace(userDisplayName))
          prefix += $" The user's name is {userDisplayName}. Address them by name occasionally to personalize responses.";
+
+     if (!string.IsNullOrWhiteSpace(userEmail))
+         prefix += $" The authenticated user's own email address is {userEmail}. Use this as the canonical source for the current user's email — do not look it up or guess it.";
```

### Fix 2 — `ChatView.razor` — Call site update

**File:** `src/FortressAI.Web/Components/Chat/ChatView.razor` (~line 486)

```diff
- var personalityPrefix = ConfigSvc.GetPersonalitySystemPrompt(_assistantConfig, Session.CurrentUser?.DisplayName);
+ var personalityPrefix = ConfigSvc.GetPersonalitySystemPrompt(_assistantConfig, Session.CurrentUser?.DisplayName, Session.CurrentUser?.Email);
```

### Fix 3 — `ChatView.razor` — Anti-fabrication guard in m365 guidance

**File:** `src/FortressAI.Web/Components/Chat/ChatView.razor` (~line 740)

Added to the end of the `m365Guidance` string:

```diff
  Use these tools proactively when the user asks about their email, inbox, calendar, meetings, or scheduling. Always confirm before sending emails or creating events.
+
+ **CRITICAL — Email addresses:**
+ - NEVER fabricate, guess, or infer email addresses. This is a strict rule with no exceptions.
+ - If you need a recipient's email address and do not have it from the conversation context, you MUST either:
+   (a) Call `m365__list_emails` or search the inbox to look up the person, OR
+   (b) Ask the user to provide the email address directly.
+ - The authenticated user's own email address is provided in your context (see system prompt). Use it as the canonical source — do not look it up.
+ - Never use personal email domains (gmail.com, outlook.com, hotmail.com, yahoo.com, etc.) for work contacts unless the user explicitly provides such an address.
```

---

## Build Result

```
Build succeeded.
  31 Warning(s)
  0 Error(s)
Time Elapsed 00:00:05.85
```

All 31 warnings are pre-existing (CS1998, CS8602, CS8604, MUD0002). No new warnings introduced.
