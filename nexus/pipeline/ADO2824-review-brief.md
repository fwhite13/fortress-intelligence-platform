# ADO#2824 — Hawkeye Code Review Brief (CC Adversarial)

## Context
This WI removes `"bedrock-agent-runtime"` from `ExternalDependencySignals` in `WiClassifierService.cs`.
The signal was incorrectly treating internal Bedrock SDK terminology as an external dependency,
causing false-positive G2 classification failures. The fix also mirrors the change in
`run_v7_validation.py` (Python sync) and adds a targeted recheck script `run_v7_g2_recheck.py`.

## Files to Review

1. `/home/fredw/projects/fip/nexus/src/FortressNexus.Web/Services/WiClassifierService.cs`
   - The `ExternalDependencySignals` array (around line 28)
   - The `IsExternalDependency` method
   - The `ExtractExternalOwner` method (especially the `bedrock-agent-runtime` reference within it)

2. `/home/fredw/projects/fip/nexus/pipeline/run_v7_validation.py`
   - The `EXT_DEP_SIGNALS` list (around line 127)
   - The `extract_external_owner` function which also contains a `bedrock-agent-runtime` reference

3. `/home/fredw/projects/fip/nexus/pipeline/run_v7_g2_recheck.py`
   - The new script — does it read from cached JSON (no live Bedrock call)?
   - Is the path to `ADO2808-BEDROCK-OUTPUT.json` correct and does the file exist?
   - Does it correctly implement G2 scoring logic (blocked-external + owner-* tag check)?
   - Does it exit(0) on PASS and exit(1) on FAIL?

## Adversarial Checks — Do ALL of these

### Check 1: Signal array completeness in C#
Read `ExternalDependencySignals` in `WiClassifierService.cs`. Verify it contains EXACTLY:
- "rob", "rob nethery", "cloudflare", "cf config", "cf route"
- "azure access", "iam request", "iam permissions"
- "secrets manager access", "ado pat", "pat token"
And does NOT contain "bedrock-agent-runtime". Count = 11 signals.

### Check 2: Signal array completeness in Python (run_v7_validation.py)
Read `EXT_DEP_SIGNALS` in `run_v7_validation.py`. Verify it matches the C# array exactly (same 11 signals, no bedrock-agent-runtime). Flag any discrepancy as CRITICAL.

### Check 3: Signal array completeness in Python (run_v7_g2_recheck.py)
Read `EXT_DEP_SIGNALS` in `run_v7_g2_recheck.py`. Verify it also matches the C# array exactly (same 11 signals). This is a three-way sync — ALL THREE must match.

### Check 4: ExtractExternalOwner — bedrock-agent-runtime in owner detection
In `WiClassifierService.cs`, `ExtractExternalOwner` has a check:
```csharp
if (ContainsAny(text, new[] { "iam", "bedrock-agent-runtime" }))
    return "AWS IAM";
```
This is NOT in `ExternalDependencySignals` — it's in the owner *detection* branch, which is only reached AFTER `IsExternalDependency()` returns true. Verify this logic is sound:
- A WI containing only "bedrock-agent-runtime" (no other ext dep signals) would fail `IsExternalDependency()` and return null from `ExtractExternalOwner()` — correct behavior
- The `bedrock-agent-runtime` in owner detection is now dead code (can never be triggered unless another ext dep signal also matches) — note this as informational, not critical

### Check 5: Same analysis for run_v7_validation.py ExtractExternalOwner mirror
`extract_external_owner` in the Python script also contains `bedrock-agent-runtime` in the IAM owner branch. Same dead code analysis — is it harmless?

### Check 6: G2 recheck script — no live Bedrock calls
Read `run_v7_g2_recheck.py` in full. Confirm:
- It opens `ADO2808-BEDROCK-OUTPUT.json` from the hardcoded path
- It does NOT import boto3, bedrock, or any AWS SDK
- It does NOT make any HTTP/API calls
- It re-applies classification logic locally using the updated signal list

### Check 7: G2 scoring logic in recheck script
The script scores G2 by checking `blocked-external` and `owner-*` tags. Verify:
- It correctly honors `blocked-external` tag to force `isExternalDependency = True` (same as validation script)
- It checks for `blocked-external` AND `owner-*` presence (AND, not OR)
- It exits 1 on failures (not 0)

### Check 8: Tony's claim about residual G2 failures
Tony said 2 residual G2 failures remain — legitimate ext dep WIs missing tags (pre-existing prompt gap, not this WI's scope).
Verify: does this WI's scope (signal list changes) address the root cause described in ADO#2824?
Does the recheck script demonstrate the fix (the failing WIs should no longer be caused by bedrock-agent-runtime false positives)?

## Pass Criteria
- All three signal arrays (C#, validation.py, g2_recheck.py) match exactly — 11 signals, no bedrock-agent-runtime
- ExtractExternalOwner retains bedrock-agent-runtime (dead but harmless, not wrong)
- Recheck script reads cached JSON only (no live Bedrock call)
- Build compiles clean
- Logic is internally consistent

## Output
Report findings in this format:
- CRITICAL: breaks things, must fix
- IMPORTANT: should fix
- NITPICK: optional  
- INFORMATIONAL: noted but not blocking

Give a final verdict: PASS or NEEDS-CHANGES
