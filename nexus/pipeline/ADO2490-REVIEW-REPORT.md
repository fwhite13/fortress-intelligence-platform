# Review Report — ADO#2490

**Task:** Implement IWiClassifier interface and WiClassifierService  
**Review Cycle:** 1 of 2  
**Reviewer:** Hawkeye (Clint Barton)  
**Date:** 2026-04-27

---

### Verdict: ✅ PASS

---

## CC Invocation Used

```bash
cd /home/fredw/projects/fip/nexus && cat /tmp/clint-brief-ado2490.md | claude --model sonnet --print --dangerously-skip-permissions
```

Brief written to `/tmp/clint-brief-ado2490.md` — adversarial spec, 11 targeted checks.

---

## Spec Compliance Check

**Spec:** `memory/projects/nexus-decomp-upgrade-spec-2026-04-27.md` — §6 Service Layer Changes

**§2 / Component Map:**
- `src/FortressNexus.Web/Services/IWiClassifier.cs` — ✅ Created
- `src/FortressNexus.Web/Services/WiClassifierService.cs` — ✅ Created
- `src/FortressNexus.Web/Program.cs` — ✅ Modified (DI registration confirmed)

**§9 Out of Scope:**
- ✅ No out-of-scope files modified

**§6 Acceptance Criteria:**
- [x] `IWiClassifier` defines 4 methods exactly as spec: ✅
- [x] `WiTemplateType` enum with 4 values in same file: ✅
- [x] All 11 infrastructure signals present: ✅
- [x] All 7 migration signals present: ✅
- [x] All 14 auth/scoping signals present: ✅
- [x] All 12 external dependency signals present: ✅
- [x] `ClassifyStory` checks Infrastructure → Migration → Standard (correct precedence): ✅
- [x] `ShouldGenerateTestCases` short-circuits false for non-Standard: ✅
- [x] `ShouldGenerateTestCases` returns true for Standard + (auth signal OR ≥4 AC): ✅
- [x] AC counting handles `- [ ]` and numbered list items: ✅
- [x] `ExtractExternalOwner` priority order matches spec exactly: ✅
- [x] No constructor-injected dependencies: ✅
- [x] All signal comparisons are case-insensitive: ✅
- [x] DI registration: `AddScoped<IWiClassifier, WiClassifierService>()` present: ✅

**Spec compliance verdict:** ✅ COMPLIANT

---

## Consistency Audit

**Files Cross-Referenced:**
- `IWiClassifier.cs` ↔ `WiClassifierService.cs` — ✅ All 4 method signatures match exactly
- `IWiClassifier.cs` ↔ `Program.cs` — ✅ Registration references correct types
- `WiClassifierService.cs` signal arrays ↔ §6 spec signal lists — ✅ All 46 signals verified (11+7+14+12+2 from ExtractExternalOwner)

**Undocumented dependencies:** None. `WiClassifierService` has no constructor, no field dependencies, pure static helpers.

---

## Critical Issues: 0

None.

---

## Important Issues: 0

None.

---

## Nitpicks: 1

**N1: Parameter type uses `AdoWorkItemDto` instead of spec's `WorkItemCandidate`** — not blocking.

The spec (§6) declares the interface using `WorkItemCandidate` as the parameter type. Tony used `AdoWorkItemDto` throughout (interface + implementation), citing an adjustment to match the actual codebase. The build passes, all 4 method signatures are internally consistent, and there is no `WorkItemCandidate` type in the codebase. This was the correct pragmatic call.

**Recommendation:** Update §6 of the spec to reflect `AdoWorkItemDto` as the canonical type. No code change needed.

---

## Positive Observations

- Signal arrays are clean, readable static readonly arrays — easy to audit and extend.
- `CombineTitleAndDescription` centralizes the title+description combination so it can't be missed on any check.
- `AcItemPattern` regex (`@"^(\s*- \[ \]|\s*\d+[\.\)])"` with `Multiline`) correctly handles both AC formats in one pass.
- `ExtractExternalOwner` correctly returns `null` (not "External Owner") when `IsExternalDependency` is false — important for consumers distinguishing "no match" from "external but unrecognized."
- Zero constructor injection in a pure string-matching service is correct design.
- DI placement is logical — grouped with other service registrations.

---

## Signal Completeness Summary

| Signal List | Expected | Found | Match |
|---|---|---|---|
| Infrastructure | 11 | 11 | ✅ |
| Migration | 7 | 7 | ✅ |
| Auth/Scoping (ShouldGenerateTestCases) | 14 | 14 | ✅ |
| External Dependency (IsExternalDependency) | 12 | 12 | ✅ |

---

## What Ships

All three files are clean. This is a scoped, well-implemented service with no functional defects. Ships as-is. The spec notation for `WorkItemCandidate` should be updated as a housekeeping note.

---

_Hawkeye — you see what others miss._
