# Review Report — ADO #1839
## Discovery answers not persisting to DB

**Commit:** b01ba37 | **Cycle:** 1 | **Reviewer:** Hawkeye  
**Date:** 2026-04-14

---

### Verdict: NEEDS-CHANGES

One critical blocker: the try/catch in `HandleContinue` silently swallows save failures and still advances the wizard. No user feedback. No `ISnackbar` injected. Fix required before this ships.

---

## Spec Compliance Check

No developer brief with formal §2/§6/§7 sections provided — reviewed against task criteria from assignment.

**Files touched:**
- `Components/Nexus/DiscoveryStep.razor` — ✅ modified as expected
- `Services/Discovery/DiscoveryService.cs` — ✅ modified as expected

**Claimed changes verified:**
- `ContainsKey` guard in `OnParametersSet` — ✅ present
- try/catch in `HandleContinue` — ✅ present (but broken — see C1)
- `.ToList()` materialization — ✅ present
- `@key="question.Id"` on `DiscoveryQuestionCard` — ✅ present, correct placement
- Answer count logging in `SaveAnswersAsync` — ✅ present

**Spec compliance verdict:** ⚠️ CONDITIONAL — all changes present, but C1 blocker means the feature is not safe to ship.

---

## Consistency Audit

No cross-file constant mismatches. `IDiscoveryService.SaveAnswersAsync` signature matches the call site. `DiscoverySessionStatus` enum values used consistently. `ISnackbar` is a MudBlazor standard injection — not present here (see C1).

---

## Critical Issues [1]

### C1: Silent save failure — wizard advances on exception with no user feedback

- **File:** `Components/Nexus/DiscoveryStep.razor`
- **Method:** `HandleContinue`
- **Category:** Correctness / UX
- **Severity:** CRITICAL — NEEDS-CHANGES blocker

**Issue:**  
`OnCompleted.InvokeAsync()` is placed **outside and after** the `if (Session != null)` block entirely. It executes unconditionally — on save success, on exception, and even when Session is null. When `SaveAnswersAsync` throws, the catch block logs to `Console.Error` (invisible to users in production) and then execution falls through to `OnCompleted.InvokeAsync()`, advancing the wizard. The user's answers are lost silently.

Additionally, `ISnackbar` is not injected — the catch block has no mechanism to surface any feedback even if code were added.

**Evidence:**
```csharp
private async Task HandleContinue()
{
    if (Session != null)
    {
        try
        {
            var oid = await UserContextService.GetUpnAsync();
            var pairs = _answers.Select(kv => (kv.Key, (string?)kv.Value)).ToList();
            await DiscoveryService.SaveAnswersAsync(Session.Id, pairs, oid);
        }
        catch (Exception ex)
        {
            // Only logs to Console.Error — invisible to user
            Console.Error.WriteLine($"[DISCOVERY] Failed to save answers for session {Session.Id}: {ex.Message}");
        }
        // ← catch exits here, execution continues...
    }
    await OnCompleted.InvokeAsync();  // ← ALWAYS called — wizard always advances
}
```

**Impact:** On any DB failure (timeout, connection drop, EF exception), user believes answers were saved but the session remains un-answered. Downstream spec generation gets no discovery context. No error visible to user.

**Fix:**

Step 1 — Add `@inject ISnackbar Snackbar` to the top of the file (with the other `@inject` directives):
```razor
@inject IDiscoveryService DiscoveryService
@inject UserContextService UserContextService
@inject ISnackbar Snackbar
```

Step 2 — Update the catch block to show error and `return` without advancing:
```csharp
catch (Exception ex)
{
    Console.Error.WriteLine($"[DISCOVERY] Failed to save answers for session {Session.Id}: {ex.Message}");
    Snackbar.Add("Could not save your answers. Please try again.", Severity.Error);
    return;  // DO NOT advance wizard
}
```

The `return` is the critical piece. Without it the fix is incomplete.

---

## Important Issues [0]

No important issues found.

---

## Nitpicks [2]

**N1: Snapshot `_answers` before first await in `HandleContinue`**  
`GetUpnAsync()` is awaited before `pairs` is computed. During that suspension, a concurrent UI event could theoretically mutate `_answers`. Blazor Server's synchronization context makes this practically safe, but the defensive pattern is to snapshot first:
```csharp
// Snapshot before any await
var pairs = _answers.Select(kv => (kv.Key, (string?)kv.Value)).ToList();
var oid = await UserContextService.GetUpnAsync();
await DiscoveryService.SaveAnswersAsync(Session.Id, pairs, oid);
```
Not a blocker. Worth fixing if Tony is already in the method.

**N2: `Session.Questions?.Any()` null guard in Razor render condition**  
The template has:
```razor
else if (Session == null || (Session.Status != DiscoverySessionStatus.Pending && !Session.Questions.Any()))
```
If `Session` is non-null but `Session.Questions` is null (navigation property not loaded), this throws a `NullReferenceException` at render time. `OnParametersSet` guards this path correctly but the render branch doesn't. Low probability given the `Include(s => s.Questions)` in `GetSessionAsync`, but defensive coding:
```razor
else if (Session == null || (Session.Status != DiscoverySessionStatus.Pending && !(Session.Questions?.Any() ?? false)))
```
Not a blocker.

---

## Positive Observations

- **ContainsKey guard is correct** — both for first load (empty dict, restores from DB) and re-renders (key present, skips restore). Null guard on `q.Answer` is present and correct. This is the right fix for the primary bug.
- **`@key="question.Id"` placement is correct** — on the `<DiscoveryQuestionCard>` usage element in the foreach, exactly where it should be.
- **`.ToList()` materialization is clean** — called before the await, no deferred evaluation risk.
- **`SaveAnswersAsync` double-enumeration fixed cleanly** — `answers.ToList()` called once into `answerList`, reused for count log and foreach. No double enumeration.
- **`_answers` never reset** — field initializer only, no lifecycle method blows it away. User-typed values survive all re-renders.
- **`DiscoveryService.cs` changes are solid** — answer count logging is clear and useful for CloudWatch debugging.

---

## Acceptance Criteria Verification

| Criterion | Status | Notes |
|-----------|--------|-------|
| `ContainsKey` guard in `OnParametersSet` | ✅ | Correct for both fresh load and resume |
| try/catch in `HandleContinue` | ❌ | Present but swallows silently and still advances |
| `.ToList()` before await | ✅ | Correct |
| `@key="question.Id"` on DiscoveryQuestionCard | ✅ | Correct placement |
| Answer count logging in `SaveAnswersAsync` | ✅ | Clean |

---

## What Tony Needs to Fix

**One change, two lines:**

1. Add to `@inject` block (line 5–6 area):
   ```razor
   @inject ISnackbar Snackbar
   ```

2. In `HandleContinue`, replace the catch block:
   ```csharp
   catch (Exception ex)
   {
       Console.Error.WriteLine($"[DISCOVERY] Failed to save answers for session {Session.Id}: {ex.Message}");
       Snackbar.Add("Could not save your answers. Please try again.", Severity.Error);
       return;
   }
   ```

That's it. No other blockers. Everything else in the build is solid.

---

_Review by Hawkeye — Clint Barton (code-reviewer) — ADO #1839 cycle 1_

---

## Cycle 2 Review — Commit 9fdee11
**Date:** 2026-04-14 | **Reviewer:** Hawkeye | **Verdict:** PASS

### What Tony Fixed
- `@inject ISnackbar Snackbar` added to directives block
- `HandleContinue` catch: `Snackbar.Add("Could not save...", Severity.Error)` + `return` — wizard no longer advances on save failure

### CC Review Summary
All three checks passed. No false positives. CC read the live file (not just the diff) and confirmed current state.

### Check 1: `@inject ISnackbar Snackbar` — ✅ PASS
- Present at line 7, correctly positioned with other `@inject` directives

```razor
5  @inject IDiscoveryService DiscoveryService
6  @inject UserContextService UserContextService
7  @inject ISnackbar Snackbar
```

### Check 2: catch block — Snackbar.Add + return — ✅ PASS
- `Snackbar.Add("Could not save your answers. Please try again.", Severity.Error)` at line 101
- `return;` at line 102, immediately after — no fall-through path to `OnCompleted.InvokeAsync()`

```csharp
catch (Exception ex)
{
    Console.Error.WriteLine($"[DISCOVERY] Failed to save answers for session {Session.Id}: {ex.Message}");
    Snackbar.Add("Could not save your answers. Please try again.", Severity.Error);
    return; // do NOT advance — user must retry
}
```

### Check 3: `OnCompleted.InvokeAsync()` placement — ✅ PASS
- Appears exactly **once** at line 105, after the closing brace of the try/catch block
- Not inside the `try` block, not duplicated

```csharp
    }         // end catch
}             // end if (Session != null)
await OnCompleted.InvokeAsync(); // only reached on success or null Session
}             // end HandleContinue
```

### Verdict: PASS

Fix is clean. Both required elements present in catch (`Snackbar.Add(Severity.Error)` + `return`), injection confirmed, `OnCompleted` only reachable on success. Ships.

---

_Review by Hawkeye — Clint Barton (code-reviewer) — ADO #1839 cycle 2_
