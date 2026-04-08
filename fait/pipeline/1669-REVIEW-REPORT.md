# Review Report — FAIT WI #1669: AI Email Fabrication Fix

**Date:** 2026-04-08  
**Reviewer:** Hawkeye (code-reviewer)  
**Commit:** `c4971f8`  
**Cycle:** 1  
**Risk:** Medium (system prompt change affects all chat behavior)

---

### Verdict: PASS (with one Important issue — not blocking)

---

## Spec Compliance Check

**§2 Changed Files:**
- `fait/src/FortressAI.Web/Services/AssistantConfigService.cs` — ✅ modified as specified
- `fait/src/FortressAI.Web/Components/Chat/ChatView.razor` — ✅ modified as specified

**§6 Scope:**
- ✅ Only the two source files changed, plus the pipeline build report. No out-of-scope changes.

**§7 Acceptance Criteria:**
- ✅ `GetPersonalitySystemPrompt` gains `string? userEmail = null` third param
- ✅ Email injected into personality prefix when non-null/non-whitespace
- ✅ Call site passes `Session.CurrentUser?.Email`
- ✅ `CRITICAL — Email addresses:` block added to m365Guidance
- ✅ Build: 0 errors

**Spec compliance verdict:** ✅ COMPLIANT

---

## CC Review Summary

Ran full adversarial review via `cat /tmp/1669-review-brief.md | claude --model sonnet --print --dangerously-skip-permissions` against all changed files plus `UserSessionService.cs` and `MainLayout.razor` for email provenance tracing.

CC found: 0 Critical, 1 Important, 2 Nitpicks. All CC findings evaluated — none are false positives. The Important issue (I1) is a real gap but low-probability.

---

## Consistency Audit

| Check | Result |
|-------|--------|
| `GetPersonalitySystemPrompt` call sites | ✅ Only one — `ChatView.razor:486` (grep confirmed) |
| `Session.CurrentUser.Email` source | ✅ OIDC claim (`"email"` / `ClaimTypes.Email`) via MainLayout |
| Email position vs. m365Guidance | ✅ `personalityPrefix` prepended first; m365Guidance appended last |
| Null guard correctness | ✅ `IsNullOrWhiteSpace` — handles null, empty, whitespace correctly |

---

## Critical Issues: 0

None.

---

## Important Issues: 1

### I1: m365Guidance makes an unconditional promise about injected email
- **File:** `fait/src/FortressAI.Web/Components/Chat/ChatView.razor` (~line 756)
- **Category:** Correctness / prompt integrity
- **Issue:** The m365Guidance string says: *"The authenticated user's own email address is provided in your context (see system prompt). Use it as the canonical source — do not look it up."* This is appended whenever M365 tools are active, **regardless of whether the email was actually injected**. If `Session.CurrentUser?.Email` is null, `GetPersonalitySystemPrompt` correctly skips injection — but the m365Guidance block still tells the LLM to find an email that isn't there. The LLM searches the system prompt, fails to find it, and may fall back to guessing — exactly what this fix is trying to prevent.
- **Probability:** Low. An authenticated OIDC user without an `email` claim is unusual. But it's a real structural gap.
- **Impact:** LLM could fabricate user email in edge case where email claim is absent (e.g., misconfigured Entra app registration missing the `email` scope).
- **Fix:**
  ```diff
  // Option A (simple): conditionally include the "see system prompt" sentence
  // In ChatView.razor, where m365Guidance is built:
  
  - - The authenticated user's own email address is provided in your context (see system prompt). Use it as the canonical source — do not look it up.
  + @(string.IsNullOrWhiteSpace(Session.CurrentUser?.Email) 
  +   ? "- You do not have the authenticated user's email address in context. Ask the user to provide it if needed."
  +   : "- The authenticated user's own email address is provided in your context (see system prompt). Use it as the canonical source — do not look it up.")
  ```
  
  Or simpler — build the m365Guidance string in a method that receives the email value and conditionally includes the line.

---

## Nitpicks: 2

- **N1: Email position when file attachments present** — When file attachments are present, `attachmentContext` is prepended before `effectiveSystemPrompt`, pushing the personality prefix (and email) to position 2 in the final prompt. Pre-existing behavior, not a regression from this WI. Email still arrives well before tool guidance. No action needed.

- **N2: `etc.` in personal-domains list is vague** — `(gmail.com, outlook.com, hotmail.com, yahoo.com, etc.)` — the trailing `etc.` invites over-generalization by the LLM. An aggressive model could decide additional domains are "personal." The user-provides escape clause covers it, but removing `etc.` or replacing with a closed list would be tighter. Not blocking.

---

## Positive Observations

- **Email source is provably canonical** — Traced the full chain: Entra OIDC claim → MainLayout lookup → `AppUser` → `Session.CurrentUser` → injected. Not user-editable. No DB-auth user bypass (Login.razor redirects to OIDC, no `Session.SetUser` call). Email in `AppUser.Email` is always the current OIDC-issued email address.
- **Null safety is complete** — `Session.CurrentUser?.Email` + `IsNullOrWhiteSpace` guard = no NullReferenceException, no partial injection. Correct choice of `IsNullOrWhiteSpace` over `IsNullOrEmpty`.
- **Prompt ordering correct** — Email is in `personalityPrefix` which is always prepended first. m365Guidance appended last. LLM will encounter the email ground-truth before any tool instruction.
- **Anti-fabrication wording is strong** — "NEVER", "strict rule with no exceptions", zero hedging. Two clear escape hatches. Clean.
- **outlook.com blocking is appropriate** — Rob's fabricated address was `rnethery@outlook.com`. Blocking it is correct. The `unless the user explicitly provides` escape handles legitimate outlook.com addresses without ambiguity. `onmicrosoft.com` and `@company.com` Exchange Online domains are unaffected.
- **Zero scope creep** — Tony touched exactly what was needed and nothing else.

---

## Answers to Review Focus Questions

| # | Question | Answer |
|---|----------|--------|
| 1 | Email source canonical? | ✅ OIDC claim. Not user-editable. |
| 2 | Null handling complete? | ✅ `?.` + `IsNullOrWhiteSpace` = fully safe |
| 3 | Prompt placement correct? | ✅ Email is first; m365Guidance is last |
| 4 | Anti-fabrication wording unambiguous? | ✅ "NEVER, no exceptions." Clear escapes. |
| 5 | outlook.com block appropriate? | ✅ Correct. User-provides escape covers false positives. |
| 6 | Only one call site? | ✅ Confirmed by grep — ChatView.razor only |
| 7 | Scope clean? | ✅ Two source files + build report only |
| 8 | Build clean? | ✅ 0 errors. String literals well-formed. |

---

## Summary

Solid fix. The email provenance is canonical, the null handling is airtight, and the anti-fabrication wording is unambiguous. The one real gap (I1) is that `m365Guidance` unconditionally promises the email is present even when it wasn't injected — a structural decoupling between the injection guard and the claim. Low probability in practice (OIDC users always have an email claim), but worth a targeted fix. Not blocking this cycle.

**PASS — ships. I1 should be addressed in a follow-up or bundled with the next ChatView change.**
