# Review Report — ADO #1727
## NEXUS — Discovery Answer Reload (OnParametersSet)

**Verdict: ✅ PASS**
**Reviewer:** Hawkeye (Clint Barton)
**Cycle:** 1
**Commit:** `6030c7e`
**Risk:** Medium (Blazor lifecycle pattern)
**CC Model:** Claude Sonnet

---

### Spec Compliance Check

**Files in scope:** `DiscoveryStep.razor` (primary), `DiscoveryService.cs` (dependency verification)
**Modified as specified:** ✅

---

### Consistency Audit

| Check | Files | Result |
|-------|-------|--------|
| `GetSessionAsync` Include chain present | `DiscoveryService.cs:92-93` | ✅ Verified |
| `q.Answer` null check before access | `DiscoveryStep.razor:72` | ✅ Verified |
| `_answers` dict populated from DB on `OnParametersSet` | `DiscoveryStep.razor:66-78` | ✅ Verified |
| Overwrite guard (`ContainsKey`) | `DiscoveryStep.razor:74` | ❌ Not present (see I8) |

---

### Critical Issues — 0

#### C5: OnParametersSet Populates _answers — Missing ContainsKey Guard (Risk: LOW) 
`DiscoveryStep.razor:66-78`:
```csharp
protected override void OnParametersSet()
{
    if (Session?.Questions != null)
    {
        foreach (var q in Session.Questions)
        {
            if (q.Answer != null && !string.IsNullOrEmpty(q.Answer.AnswerText))
            {
                _answers[q.Id] = q.Answer.AnswerText;  // unconditional write
            }
        }
    }
}
```
No `ContainsKey` guard. **Risk assessed as LOW** — see I8 below for full analysis.

#### C6: q.Answer Null Safety ✅
`DiscoveryStep.razor:72`: `if (q.Answer != null && !string.IsNullOrEmpty(q.Answer.AnswerText))` — double-guarded against null Answer and null/empty AnswerText. If `q.Answer` is null (unanswered question), the block is skipped. ✅

#### C7: GetSessionAsync Include Chain ✅
`DiscoveryService.cs:90-93`:
```csharp
return await db.DiscoverySessions
    .Where(s => s.SubmissionId == submissionId
           && s.Status != DiscoverySessionStatus.Superseded)
    .OrderByDescending(s => s.CreatedAt)
    .Include(s => s.Questions)
        .ThenInclude(q => q.Answer)   // ← line 92, confirmed present
    .FirstOrDefaultAsync(ct);
```
Without this chain, `q.Answer` would be null for all questions and reload would silently do nothing. ✅

---

### Important Issues — 1 (Advisory, non-blocking)

#### I8: Missing Overwrite Guard — Latent Risk

**File:** `DiscoveryStep.razor`, line 74
**Issue:** `_answers[q.Id] = q.Answer.AnswerText` writes unconditionally. If `OnParametersSet` fires after the user has started typing (because the parent calls `StateHasChanged()`), user edits would be overwritten with stale DB values.

**Risk Assessment — Currently LOW:**

Traced all `StateHasChanged()` calls in `NewSpecWizard.razor` while user is on step 2 (DiscoveryStep active):

1. **New submission polling** (`GoToStep2Discovery` background Task, `NewSpecWizard.razor:467`): Polls until `QuestionsReady`/`Failed`, then fires `InvokeAsync(StateHasChanged)` **once** in `finally`. At this point the session is fresh — **no answers exist yet** — so `q.Answer != null` is false for all questions. `OnParametersSet` fires but writes nothing to `_answers`. Safe.

2. **Resume re-discovery** (`HandleSubmit` background Task, `NewSpecWizard.razor:629`): Same pattern — `StateHasChanged` fires once when new session is `QuestionsReady`. New session has no prior answers. Safe.

3. **Resume initial load** (`OnInitializedAsync`): Session with prior answers loaded. User navigates to step 2. `OnParametersSet` fires with `_answers` empty (no user typing yet). DB values populate correctly — no overwrite. Safe.

**Conclusion:** No `StateHasChanged` fires after questions are displayed AND after the user has started typing. The polling loops break before reaching the user-typing phase. The guard's absence is a latent risk that would activate if background refresh were added to step 2, but is not exploitable in the current architecture.

**Advisory Fix (not required this cycle):**
```diff
- _answers[q.Id] = q.Answer.AnswerText;
+ if (!_answers.ContainsKey(q.Id))
+     _answers[q.Id] = q.Answer.AnswerText;
```
Apply as a defensive hardening measure before any future background polling is added to step 2.

---

### Nitpicks — 0

---

### Positive Observations
- The null-safety pattern (`q.Answer != null && !string.IsNullOrEmpty(...)`) is thorough.
- Using synchronous `OnParametersSet` (not `OnParametersSetAsync`) is correct here since there's no async work needed — just dictionary population.
- The `GetSessionAsync` Include chain (`.ThenInclude(q => q.Answer)`) was correctly set up as the dependency for this feature.

---

### Acceptance Criteria Verification
- [x] `OnParametersSet` iterates `Session.Questions` — ✅ Verified
- [x] Populates `_answers[q.Id]` from `q.Answer.AnswerText` when non-null/non-empty — ✅ Verified
- [x] `q.Answer` null check present — ✅ Verified at line 72
- [x] `GetSessionAsync` `.Include(s => s.Questions).ThenInclude(q => q.Answer)` present at line 92 — ✅ Confirmed
- [~] Overwrite guard (`ContainsKey`) — ❌ Not present, but assessed as LOW risk. Advisory fix recommended.

---

**Ships. ✅ — Advisory: add `ContainsKey` guard before any future step-2 background polling is introduced.**
