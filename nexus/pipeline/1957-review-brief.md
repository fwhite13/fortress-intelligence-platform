# CC Adversarial Review Brief — ADO #1957

You are performing an adversarial code review for commit 50dafcf on the NEXUS project.
Commit message: `fix(ADO#1957): fix vision prompt leakage, question count, image logging, duplicate logs`

## Files Changed (3 total)
1. `nexus/src/FortressNexus.Web/Services/Discovery/DiscoveryService.cs`
2. `nexus/src/FortressNexus.Web/appsettings.json`
3. `nexus/src/FortressNexus.Web/Program.cs`

## Your Mission
Read the following files and verify each check. Be adversarial — don't trust the commit message.

---

## CHECK 1 — Vision Prompt (DiscoveryService.cs, lines ~391-407)

Read `nexus/src/FortressNexus.Web/Services/Discovery/DiscoveryService.cs` around lines 385-430.

Verify:
a. In the `if (!string.IsNullOrWhiteSpace(file.UserDescription))` branch: the visionUserPrompt does NOT contain `submission.Title` anywhere.
b. In the `else` branch: the visionUserPrompt does NOT contain `submission.Title` anywhere, and is a string literal (not interpolated with `$`).
c. BOTH branches contain the text "Do not generate questions or recommendations."
d. The `if` branch still includes `file.UserDescription` (it should say something like "Submitter note: {file.UserDescription}")
e. The system prompt string passed as first argument to `InvokeWithImageAsync(...)` is unchanged from "You are a business analyst assistant. Describe the contents of this image concisely for the purpose of generating discovery questions about a software feature." — it must NOT have been modified.

Also check: Is there any OTHER location in DiscoveryService.cs where `submission.Title` is used in a vision-related context? (grep for `submission.Title` in the file)

---

## CHECK 2 — Question Count (appsettings.json)

Read `nexus/src/FortressNexus.Web/appsettings.json` and find the `DiscoveryQuestionGen` key.

Verify:
a. The prompt text says "up to 10 questions" (not "3-7 questions" and not any other number)
b. The rest of the DiscoveryQuestionGen prompt is identical to what you'd expect — no other modifications. Specifically, the categories list should still be "Users & Access|Scope|Conflict|Assumption|Edge Case" and the JSON structure should still include id, text, category, blocking, rationale fields.
c. Check the other AI prompts in the file (DiscoverySystem, SpecGenSystem, ArtifactGenSystem) — confirm they were NOT changed.

---

## CHECK 3 — Image Logging (DiscoveryService.cs, lines ~407-410)

Read the logging call added after `imageDescription = visionResult.Text;`:

Verify:
a. The log call uses `_logger.LogInformation(...)` (not LogDebug, LogWarning, etc.)
b. The log format template is: `"[DISCOVERY_GEN] Image description for {FileName} (attempt {Attempt}): {Description}"`
c. Arguments match placeholders in ORDER: `file.OriginalFileName`, `attempt`, `imageDescription`
d. The log call comes AFTER `imageDescription = visionResult.Text;` and BEFORE the `break;` statement
e. The log call is inside the `try` block, not in a `catch` block

---

## CHECK 4 — Duplicate Log Fix (Program.cs)

Read `nexus/src/FortressNexus.Web/Program.cs` around lines 17-25.

Verify:
a. `builder.Logging.ClearProviders();` appears BEFORE `builder.Host.UseSerilog(...)`
b. The `UseSerilog` lambda itself is unchanged — it should still call `.ReadFrom.Configuration(ctx.Configuration)` and `.Enrich.FromLogContext()`
c. No other changes were made to Program.cs

---

## CHECK 5 — Scope Containment

Verify:
a. `nexus/src/FortressNexus.Web/Services/SpecGenerationService.cs` was NOT modified (it also has InvokeWithImageAsync calls with `submission.Title` — those should be untouched)
b. No other files were changed beyond the 3 listed above

---

## CHECK 6 — Logic/Edge Cases

In DiscoveryService.cs, check:
a. The `imageDescription` variable assignment + log placement — if `visionResult.Text` is null or empty, does the log still fire? Is that acceptable?
b. Are there any other places in DiscoveryService where `submission.Title` leaks into a vision or AI prompt? (grep the whole file for submission.Title usage)
c. Does the image description log contain PII risk? (it logs the full image description text to CloudWatch — is this a concern given the discovery context?)

---

## Verdict Criteria
- PASS: All checks verified, no issues found
- NEEDS-CHANGES: One or more checks fail but the fix is straightforward
- FAIL: Logic is broken, wrong files were changed, or spec was not followed

Report findings for each check. Flag anything suspicious even if it doesn't block.
