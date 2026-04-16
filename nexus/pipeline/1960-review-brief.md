# CC Review Brief — ADO #1960 (Adversarial)

## Task
Adversarial code review of commit `73a15a2` for ADO #1960.
Two surgical changes: (1) add `anthropic_beta` to `InvokeAsync`, (2) bump `SpecGen.MaxTokens` to 32768.

## Files to Read
- `/home/fredw/projects/fip/nexus/src/FortressNexus.Web/Services/BedrockService.cs`
- `/home/fredw/projects/fip/nexus/src/FortressNexus.Web/appsettings.json`

## Checks — Verify Each Exactly

### Check 1: InvokeAsync anthropic_beta type
In `InvokeAsync`, does `requestObj["anthropic_beta"]` use `new JsonArray { "output-128k-2025-02-19" }` (a proper JSON array), NOT a plain string?
Report PASS or FAIL with the exact line.

### Check 2: Field ordering in InvokeAsync requestObj
In `InvokeAsync`, does `anthropic_beta` appear AFTER `anthropic_version` and BEFORE `max_tokens`?
Report the exact field order as you see it in the source.

### Check 3: InvokeWithImageAsync unchanged
Does `InvokeWithImageAsync` already have `anthropic_beta`? Is it identical to the one in `InvokeAsync` — same type, same value? Was anything else changed in that method?

### Check 4: appsettings.json SpecGen.MaxTokens
What is the value of `Bedrock.SpecGen.MaxTokens` in appsettings.json? Should be 32768.

### Check 5: VisionMaxTokens not changed
What is the value of `Bedrock.SpecGen.VisionMaxTokens`? Should still be 2000.

### Check 6: Logic concerns
Any concerns about using `new JsonArray` inside a `JsonObject` initializer in .NET's System.Text.Json.Nodes? Is this idiomatic? Any risk of serialization issues?

### Check 7: Fallback path
`InvokeWithImageAsync` has a fallback: when `imageBytes` is null/empty it calls `InvokeAsync`. Does `InvokeAsync` now also have `anthropic_beta`? So both paths send the header. Is this correct?

### Check 8: Any other issues
Any hardcoded values, security concerns, logic errors, or drift between the two requestObj definitions (InvokeAsync vs InvokeWithImageAsync)?

## Verdict Criteria
- PASS: All checks pass, both methods have identical anthropic_beta, MaxTokens=32768, VisionMaxTokens=2000
- NEEDS-CHANGES: Any field is wrong type, wrong value, wrong order, or the two methods are inconsistent
- FAIL: Critical logic error or security issue

Be skeptical. Check the actual code, not just comments.
