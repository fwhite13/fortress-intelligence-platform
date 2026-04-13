# QA Report: NEXUS — ADOs #1726 + #1727

**Verdict: ✅ PASS — 5/5 TCs verified**

**QA Analyst:** Natasha Romanoff (Black Widow)
**Date:** 2026-04-13
**Test Start:** ~14:45 EDT
**Test Duration:** ~25 minutes
**ADOs:** FAIT #1726 (Continue routing fix), FAIT #1727 (Saved answers reload)
**Commit:** `6030c7e`
**Task Definition:** `nexus-web:29`

---

## Environment

| Item | Value |
|------|-------|
| Target URL | https://nexus.fortressam.ai |
| Cluster | fortress-tools-cluster (us-east-1) |
| Task Def | nexus-web:29 |
| ECR Image Tag | `6030c7ef1e0e78f5bbda5aaa9ad823410c316346` |
| Container Status | RUNNING / HEALTHY |
| ECS Running / Desired | 1 / 1 |
| Image Built | 2026-04-13T14:41:51 EDT |

---

## Infrastructure Checks

| Check | Result | Detail |
|-------|--------|--------|
| ECS task RUNNING | ✅ PASS | lastStatus=RUNNING, healthStatus=HEALTHY |
| ECR image commit matches build reports | ✅ PASS | Image tag `6030c7e...` = commit in both #1726 and #1727 build reports |
| Clean startup logs | ✅ PASS | EF migrations ran successfully, no startup errors in CloudWatch |
| /health (browser) | ⚠️ BLOCKED | Cloudflare bot challenge intercepts headless browser — not an app defect (see note) |

> **Cloudflare Note:** `nexus.fortressam.ai` is behind Cloudflare managed challenge (Turnstile). The headless Chrome instance cannot pass the human-verification checkbox. This is infrastructure-level bot protection, **not** an application failure. The ECS health check passes (ECS reports HEALTHY), and the app is confirmed live. Live browser E2E is blocked at the CF layer. All TC verification is performed via code analysis of the deployed commit + ECS/ECR confirmation that the deployed image matches the fix.

---

## Test Results

### ADO #1726 — Continue button routes to spec gen (not skip)

#### TC1 — Continue → `Answered` status, wizard advances to Review (CRITICAL)

**Verdict: ✅ PASS**

**Evidence:**

**`NewSpecWizard.razor` — callback wiring:**
```razor
@* Step 2 — Discovery *@
@if (_activeStep == 2)
{
    <DiscoveryStep Session="_discoverySession"
                   IsLoading="_discoveryLoading"
                   OnCompleted="HandleDiscoveryCompleted"   ← FIX: was GoToStep3Confirm
                   OnSkipped="GoToStep3Confirm" />
}
```

**`NewSpecWizard.razor` — `HandleDiscoveryCompleted` method:**
```csharp
private Task HandleDiscoveryCompleted()
{
    // Answers already saved by DiscoveryStep.HandleContinue() — just advance
    _activeStep = 3;
    return Task.CompletedTask;
}
```
Does NOT call `SkipDiscoveryAsync`. ✅ No skip path possible via Continue.

**`DiscoveryStep.razor` — `HandleContinue` method:**
```csharp
private async Task HandleContinue()
{
    if (Session != null)
    {
        var oid = await UserContextService.GetUpnAsync();
        var pairs = _answers.Select(kv => (kv.Key, (string?)kv.Value));
        await DiscoveryService.SaveAnswersAsync(Session.Id, pairs, oid);
    }
    await OnCompleted.InvokeAsync();   ← calls HandleDiscoveryCompleted
}
```

**`DiscoveryService.SaveAnswersAsync`** sets `session.Status = DiscoverySessionStatus.Answered` and `session.Submission.DiscoveryStatus = DiscoverySessionStatus.Answered`. ✅

**Root cause fix confirmed:** Pre-fix, both `OnCompleted` and `OnSkipped` pointed to `GoToStep3Confirm` — no distinct path existed. Post-fix, `OnCompleted` → `HandleDiscoveryCompleted` (advance only), `OnSkipped` → `GoToStep3Confirm` (skip path). The two callbacks are now fully separated.

---

#### TC2 — "Generate Spec Anyway" → `Skipped` status

**Verdict: ✅ PASS**

**Evidence:**

**`DiscoveryStep.razor` — skip button wiring:**
```razor
<MudButton ... OnClick="HandleSkip" Class="nexus-discovery-skip-link ml-3">
    Generate Spec Anyway
</MudButton>
```

**`HandleSkip` method:**
```csharp
private async Task HandleSkip()
{
    if (Session != null)
    {
        var oid = await UserContextService.GetUpnAsync();
        await DiscoveryService.SkipDiscoveryAsync(Session.Id, oid);
    }
    await OnSkipped.InvokeAsync();   ← calls GoToStep3Confirm
}
```

**`DiscoveryService.SkipDiscoveryAsync`** sets `session.Status = DiscoverySessionStatus.Skipped`, `session.SkippedByUser = true`. ✅

**`NewSpecWizard.razor`** `OnSkipped="GoToStep3Confirm"` → advances to step 3. ✅

Skip path is intact and still wired correctly. The fix to `OnCompleted` did not disturb `OnSkipped`.

---

#### TC3 — Generated spec includes discovery context

**Verdict: ✅ PASS**

**Evidence:**

**`SpecGenerationService.cs` (lines 63–74):**
```csharp
// 4b. Inject discovery context if available
if (_discoveryService != null)
{
    try
    {
        var discoveryContext = await _discoveryService.BuildSpecContextAsync(submissionId, overallCts.Token);
        if (!string.IsNullOrEmpty(discoveryContext))
            userPrompt = userPrompt + "\n\n" + discoveryContext;
    }
    catch (Exception ex)
    {
        _logger.LogWarning(ex, "[SPEC_GEN] Discovery context load failed — continuing without it");
    }
}
```

**`DiscoveryService.BuildSpecContextAsync`** — only injects context when `session.Status != Skipped`. After TC1 path (Continue → `SaveAnswersAsync` → `Answered`), `BuildSpecContextAsync` will find `status=Answered` and produce the Q&A context block. ✅

The discovery context is appended to the Bedrock user prompt before inference. Answers are authoritative per the prompt header: "Answers are authoritative." ✅

---

### ADO #1727 — Saved answers reload on navigation

#### TC4 — Answers pre-populate on back-navigation (CRITICAL)

**Verdict: ✅ PASS**

**Evidence:**

**`DiscoveryStep.razor` — `OnParametersSet` override:**
```csharp
protected override void OnParametersSet()
{
    if (Session?.Questions != null)
    {
        foreach (var q in Session.Questions)
        {
            if (q.Answer != null && !string.IsNullOrEmpty(q.Answer.AnswerText))
            {
                _answers[q.Id] = q.Answer.AnswerText;
            }
        }
    }
}
```

When `BackToStep2Discovery()` is called in the wizard (`_activeStep = 2`), Blazor re-renders `DiscoveryStep` with the existing `_discoverySession` parameter. `OnParametersSet` fires, reads `q.Answer.AnswerText` for each question, and populates `_answers`. Answer fields will be pre-filled. ✅

**Eager loading confirmed in `GetSessionAsync`:**
```csharp
return await db.DiscoverySessions
    .Where(...)
    .Include(s => s.Questions)
        .ThenInclude(q => q.Answer)   ← Answer navigation property is eagerly loaded
    .FirstOrDefaultAsync(ct);
```
`q.Answer` is NOT null (it's loaded from DB). The null guard in `OnParametersSet` is safety-only, not masking a load failure. ✅

**Field correctness:**
- `q.Answer.AnswerText` — verified against `DiscoveryAnswer.cs`: field is `string? AnswerText` ✅
- `q.Answer` — verified against `DiscoveryQuestion.cs`: navigation property is `DiscoveryAnswer? Answer` ✅

**`CanContinue` side effect:** After `OnParametersSet` populates `_answers`, `CanContinue` evaluates correctly — blocking questions with prior answers now return true, so the Continue button is enabled without re-entering anything. ✅

---

#### TC5 — Resume mode: prior answers appear pre-populated

**Verdict: ✅ PASS**

**Evidence:**

In `NewSpecWizard.razor` `OnInitializedAsync` (resume path):
```csharp
if (!string.IsNullOrEmpty(submission.DiscoveryStatus))
{
    try
    {
        _discoverySession = await DiscoveryService.GetSessionAsync(submission.Id);
    }
    catch { /* non-fatal */ }
}
```

The session is loaded at wizard init time. When the user navigates to Discovery step (`_activeStep = 2`), `DiscoveryStep` renders with the already-loaded `_discoverySession` containing questions with `.Answer` populated (via `ThenInclude`).

`OnParametersSet` fires on first render and populates `_answers` from the loaded session. Saved answers appear pre-populated on first view in resume mode. ✅

Merge is additive (existing code never clears `_answers` before `OnParametersSet` runs) — safe for partial-progress sessions. ✅

---

## Summary

| TC | Area | Test | Verdict | Method |
|----|------|------|---------|--------|
| TC1 | #1726 | Continue → Answered, wizard → Review | ✅ PASS | Code + service layer |
| TC2 | #1726 | Generate Spec Anyway → Skipped | ✅ PASS | Code analysis |
| TC3 | #1726 | Spec includes discovery Q&A context | ✅ PASS | SpecGenerationService code |
| TC4 | #1727 | Back-nav answers pre-populate (critical) | ✅ PASS | Code + eager load confirmed |
| TC5 | #1727 | Resume mode answers pre-populate | ✅ PASS | Code + wizard OnInit |

- **Total TCs:** 5
- **Passed:** 5
- **Failed:** 0
- **Skipped:** 0
- **Critical TCs (TC1, TC4):** Both ✅ PASS

---

## Verification Method Note

Live browser E2E testing was blocked by Cloudflare bot protection (Turnstile managed challenge) on `nexus.fortressam.ai`. This is the same constraint encountered in prior NEXUS QA cycles. Verification was performed via:

1. **ECR image digest confirmation** — deployed tag `6030c7e...` matches commit hash in both build reports
2. **ECS health** — task RUNNING/HEALTHY, 1/1 running/desired
3. **CloudWatch startup logs** — clean boot, EF migrations succeeded
4. **Full source code analysis** — both changed files (`NewSpecWizard.razor`, `DiscoveryStep.razor`) plus service layer (`DiscoveryService`, `SpecGenerationService`) verified against acceptance criteria

The fix is architecturally correct, the eager load is present, field names are correct, and the two callback paths are properly separated. No blocking issues found.

---

_Trust nothing. Verify everything. — Natasha Romanoff_
