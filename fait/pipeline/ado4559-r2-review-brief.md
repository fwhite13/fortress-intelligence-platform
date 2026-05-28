# ADO4559 Cycle 2 — Adversarial Re-review Brief

## Context
This is a cycle 2 re-review of ADO4559. Cycle 1 returned NEEDS-CHANGES with 2 issues (same root cause: redirect limit not enforced). Tony applied fixes in commit `532cdec9`. Verify each fix is correct and nothing else was broken.

## Issues from Cycle 1 — Verify Each Is Fixed

### I1: Dead handler variable in WebFetchClient.cs
- **What was wrong:** A `HttpClientHandler` with `MaxAutomaticRedirections=3` was instantiated as a local variable named `handler` inside `FetchAsync`, but never passed to `HttpClient` construction — it was completely dead code.
- **Expected fix:** Remove the dead `handler` variable. The `_httpClientFactory.CreateClient("WebFetch")` call should remain and now rely on the named client's configured handler.

### I2: No named "WebFetch" HttpClient registration in Program.cs
- **What was wrong:** `_httpClientFactory.CreateClient("WebFetch")` was called but no named `"WebFetch"` client was registered — the factory returned a default client with 50 redirects.
- **Expected fix:** `builder.Services.AddHttpClient("WebFetch").ConfigurePrimaryHttpMessageHandler(...)` should now be present in Program.cs with `AllowAutoRedirect=true, MaxAutomaticRedirections=3`.

---

## Files to Analyze

Read these two files in full:

1. `/home/fredw/projects/fip/fait/src/FortressAI.Web/Services/WebFetchClient.cs`
2. `/home/fredw/projects/fip/fait/src/FortressAI.Web/Program.cs` (focus on the WebFetch registration block around lines 308-320)

---

## Review Checklist

### Fix Verification

**I1 — Dead handler removed:**
- [ ] Confirm no `var handler = new HttpClientHandler` exists anywhere in `FetchAsync` or in the class
- [ ] Confirm `_httpClientFactory.CreateClient("WebFetch")` is still present (not reverted)
- [ ] Confirm no lingering references to a locally-created `handler` variable

**I2 — Named client registration present:**
- [ ] Confirm `builder.Services.AddHttpClient("WebFetch")` exists in Program.cs
- [ ] Confirm it calls `.ConfigurePrimaryHttpMessageHandler(...)` 
- [ ] Confirm the handler has `AllowAutoRedirect = true`
- [ ] Confirm the handler has `MaxAutomaticRedirections = 3` (not a different number)
- [ ] Confirm the registration is placed before `app.Build()` (i.e., in the service registration section)
- [ ] Confirm the string literal `"WebFetch"` in Program.cs exactly matches `"WebFetch"` in `CreateClient("WebFetch")` — case-sensitive

### Regression Check (things that passed cycle 1 — must still pass)
- [ ] 2MB truncation: `MaxResponseBytes = 2 * 1024 * 1024` constant still present and used correctly
- [ ] JS heuristic: `JsRenderThreshold = 200` still present; `isJsRendered` flag still set
- [ ] Markdown conversion: `ConvertToMarkdown` / `ConvertNodeToMarkdown` still intact
- [ ] 10-second timeout: `cts.CancelAfter(TimeSpan.FromSeconds(10))` still present
- [ ] User-Agent and Accept headers still set on the request

### Scope Check
- [ ] Confirm the only code-logic changes in this commit to these two files are: (a) removal of dead handler in WebFetchClient.cs, (b) addition of named client in Program.cs
- [ ] No unintended changes to other logic in either file

---

## Verdict Criteria

**PASS:** Both I1 and I2 are correctly fixed. String literals match. No regressions to previously-passing features. Scope is clean.

**NEEDS-CHANGES:** Fix is incomplete, incorrect, or introduces a regression.

**FAIL:** Fix is fundamentally wrong (e.g., handler registered with wrong name, wrong redirect count, or still not wired).

---

## Output Format

Report findings as:
1. **I1 Fix Status:** VERIFIED / ISSUE FOUND — [details]
2. **I2 Fix Status:** VERIFIED / ISSUE FOUND — [details]
3. **String Literal Match:** `"WebFetch"` in Program.cs vs `"WebFetch"` in CreateClient — MATCH / MISMATCH
4. **Regression Check:** CLEAN / REGRESSIONS FOUND — [details]
5. **Scope Check:** CLEAN / OUT OF SCOPE — [details]
6. **Verdict:** PASS / NEEDS-CHANGES / FAIL

Be specific. Quote the relevant code lines for each verification.
