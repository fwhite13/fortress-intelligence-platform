# Review Report — ADO#2500

**Task:** NexusArtifacts UI — WI tree, template badges, predecessor badges, external deps panel, test case grouping  
**Commit:** `5159377`  
**Review cycle:** 1 of 2  
**Reviewer:** Hawkeye (Clint Barton)

---

### Verdict: NEEDS-CHANGES

---

## CC Review Summary

CC was invoked with an adversarial brief covering all 10 review areas from the task spec. CC read `NexusArtifacts.razor`, `NexusArtifactsController.cs`, `SubmissionDetail.razor`, `WorkItemRecord.cs`, `ArtifactSet.cs`, and `Program.cs` before reporting findings.

CC found 2 Critical, 7 Important, 2 Nitpick issues. On my own cross-check I confirmed both Criticals directly (C1 via line-by-line read of `IsCrossEpicPredecessor`, C2 via `WorkItemRecord.cs` showing no `Description` property and line 52 of the razor showing `CopyToClipboard(extWi.Title)`). I dismissed I6 (dead controller) as Important not Critical — the spec calls for that endpoint even if the razor currently loads directly. I confirmed I7 (DbContext injection) and I8 (no ownership check) from the code.

**False positives dismissed:** None. All CC findings checked out on direct read.

---

## Spec Compliance Check

**Brief:** `memory/projects/nexus-decomp-upgrade-spec-2026-04-27.md`  
**Focus:** §8 — UI Specification

### §2 / §5 Codebase Map (files modified):
- `NexusArtifacts.razor` — ✅ created as specified
- `NexusArtifactsController.cs` — ✅ created as specified
- `SubmissionDetail.razor` — ✅ modified with nav button

### §9 Out of Scope:
- ✅ No out-of-scope changes detected

### §8 UI Acceptance Criteria:

**External Dependencies Panel:**
- [x] Panel shown only when `ExternalDependencyCount > 0` — ✅ Verified (line 35)
- [x] `ArtifactSet.ExternalDependencyCount` field exists — ✅ Confirmed in model
- [ ] Amber MudAlert with correct message including ⚠️ — ❌ Missing ⚠️ emoji in alert text (N1)
- [ ] Description preview (first 120 chars) shown per entry — ❌ `WorkItemRecord` has no `Description` property; preview is absent (C2/I1)
- [ ] Tag chips shown — ❌ Iterates `TestedByTitles` instead of a `Tags` field (I2)
- [ ] "Copy brief" copies **full description** — ❌ Copies `extWi.Title` instead; model has no Description field (C2)
- [x] Collapse/expand toggle — ✅ via MudExpansionPanel

**WI Template Badges:**
- [x] Infrastructure → teal/Info chip — ✅ Color correct
- [x] Migration → purple/Secondary chip — ✅ Color correct
- [x] Test Case → blue/Primary chip — ✅ Color correct
- [x] Standard → no badge — ✅
- [x] Badges on all WI types — ✅ Epic, Feature, Story, Task all get `RenderTemplateBadge`
- [ ] 🏗️ / 🔄 / 🧪 emojis in badge labels — ❌ Labels are "Infra", "Migration", "TC" without emojis (I3)

**Predecessor Badges:**
- [x] Same-Epic amber chip — ✅ `Color.Warning`, `Blocked by: [truncated]`
- [ ] Cross-Epic orange chip "⛓ Cross-Epic: [Epic] > [truncated title]" — ❌ Also uses `Color.Warning`; Epic name is missing from label (C1, I5)
- [x] Unresolved red chip — ✅ `Color.Error`
- [x] Badges inline after title — ✅
- [x] Null-safe (no predecessors = nothing rendered) — ✅ `if (wi.PredecessorTitles?.Any() != true) return;`
- [x] Cross-Epic detection logic — ✅ CORRECT. `IsCrossEpicPredecessor` walks `ParentTitle` chain to find Epic for both WIs, compares Epic titles. Logic is sound. BUT it discards the Epic name (C1).
- [x] Unresolved detection — ✅ CORRECT. `IsUnresolvedPredecessor` checks if predTitle matches any WI in `_workItems`.
- [ ] ⛓ emoji prefix on all badge labels — ❌ Absent on all three types (I4)

**Test Case Grouping:**
- [x] Test Cases excluded from main tree — ✅ `GetChildren` filters out `WorkItemType != "Test Case"`
- [x] MudExpansionPanel under each User Story — ✅
- [x] Collapsed by default — ✅ `Expanded="false"`
- [x] Matched via `ParentTitle == story.Title` — ✅
- [ ] 🧪 Test Cases (N) header — ❌ Header reads "Test Cases (@testCases.Count)" without emoji (I9)
- [ ] Each entry shows 🧪 chip — ❌ Shows "TC" chip without emoji (I9)

**Spec compliance verdict:** ❌ NON-COMPLIANT — blocks PASS.  
C2 (no Description field) is the most significant gap: the copy-brief feature is functionally broken and the description preview is completely absent.

---

## Consistency Audit

**Cross-file checks:**

| | Check | Result |
|--|--|--|
| `SubmissionDetail.razor` nav route `/nexus/{Id}/artifacts` | `NexusArtifacts.razor` @page `/nexus/{Id:int}/artifacts` | ✅ Match |
| `WorkItemRecord.PredecessorTitles` name | Razor usage `wi.PredecessorTitles` | ✅ Match |
| `WorkItemRecord.WiTemplate` name | Razor `wi.WiTemplate == WiTemplateType.Infrastructure` | ✅ Match |
| `WorkItemRecord.IsExternalDependency` name | Razor filter `.Where(w => w.IsExternalDependency)` | ✅ Match |
| `ArtifactSet.ExternalDependencyCount` name | Razor `_artifactSet.ExternalDependencyCount > 0` | ✅ Match |
| `WorkItemRecord.Description` | Razor CopyToClipboard | ❌ Field does not exist in model (C2) |
| Controller route `GET nexus/{id}/artifacts/external-dependencies` | Razor — never called | ⚠️ Controller exists but razor never invokes it (I6) |

**No undocumented dependencies found that are broken (besides Description above).**

---

## Critical Issues [2]

#### C1 — Cross-Epic chip label missing Epic name
- **File:** `NexusArtifacts.razor` (lines 322–335) and `IsCrossEpicPredecessor` (lines 226–236)
- **Category:** Spec non-compliance
- **Issue:** `IsCrossEpicPredecessor` correctly walks the parent chain to find each WI's Epic and compares them — the detection logic is right. But it only returns `bool`. The Epic title is computed as `predEpic` then discarded. The chip label is hardcoded as `$"Cross-Epic: {Truncate(predTitle, 30)}"` — the Epic name is never present.
- **Spec requires:** `"⛓ Cross-Epic: [Epic name] > [truncated title]"`
- **Impact:** User cannot tell which Epic the cross-Epic dependency lives in — the entire purpose of the cross-Epic label distinction.
- **Fix:**
  ```csharp
  // Change return type to return the predecessor Epic title when cross-Epic
  private string? GetCrossEpicName(WorkItemRecord wi, string predTitle)
  {
      var predWi = _workItems.FirstOrDefault(w => w.Title == predTitle);
      if (predWi is null) return null;
      var wiEpic = FindEpicFor(wi);
      var predEpic = FindEpicFor(predWi);
      if (wiEpic is null || predEpic is null || wiEpic.Title == predEpic.Title)
          return null;
      return predEpic.Title;
  }
  
  // In RenderPredecessorBadges:
  var crossEpicName = GetCrossEpicName(wi, predTitle);
  var isCrossEpic = crossEpicName is not null;
  // ...
  // Label:
  cb.AddContent(0, $"⛓ Cross-Epic: {Truncate(crossEpicName!, 20)} > {Truncate(predTitle, 20)}")
  ```

---

#### C2 — Copy brief copies Title not Description; WorkItemRecord has no Description field
- **File:** `NexusArtifacts.razor` line 52; `WorkItemRecord.cs`
- **Category:** Spec non-compliance + missing model field
- **Issue (a):** The "Copy brief" button calls `CopyToClipboard(extWi.Title)`. The spec explicitly requires copying the **full description** — "Copy brief" that copies the title is functionally useless.
- **Issue (b):** `WorkItemRecord` has no `Description` property at all. The description preview (first 120 chars) shown in the external dep panel cannot be rendered. Even fixing (a) would not compile without (b) resolved first.
- **Impact:** The "Copy brief" feature — the primary mechanism for actioning external dependencies — is completely broken. Rob never gets the CF brief text; the AWS IAM engineer never gets the permissions list.
- **Fix:**
  1. Add `Description` property to `WorkItemRecord.cs`:
     ```csharp
     public string? Description { get; set; }
     ```
  2. Confirm migration `AddDecompositionUpgradeFields_20260427` adds the column (or confirm it already exists in the DB schema).
  3. Fix the copy button:
     ```diff
     - OnClick="@(() => CopyToClipboard(extWi.Title))"
     + OnClick="@(() => CopyToClipboard(extWi.Description))"
     ```
  4. Add the description preview line to the card:
     ```razor
     @if (!string.IsNullOrEmpty(extWi.Description))
     {
         <MudText Typo="Typo.body2" Class="mt-1">
             @(extWi.Description.Length > 120 ? extWi.Description[..120] + "…" : extWi.Description)
         </MudText>
     }
     ```

---

## Important Issues [7]

#### I1 — No description preview in external dep panel (corollary of C2)
Already covered under C2. Blocked by the missing Description field. Fix C2 to resolve.

#### I2 — Tag chips iterate TestedByTitles, not tags
- **File:** `NexusArtifacts.razor` lines 48–51
- **Issue:** `extWi.TestedByTitles` is the list of Test Case WI titles linked to a story — not tags. For external deps it will typically be null, rendering nothing. `WorkItemRecord` has no `Tags` property.
- **Fix:** Add a `Tags` property to `WorkItemRecord` (as `List<string>?`) and populate it in `ArtifactGenerationService`. Then fix the loop to iterate `extWi.Tags`. Alternatively, surface only the meaningful tags (`blocked-external`, `owner-...`) from `ExternalOwner` directly rather than a stored tag list.

#### I3 — Template badge emojis missing
- **File:** `NexusArtifacts.razor` lines 270, 281, 292 (ChildContent strings)
- **Issue:** All three badge labels are missing their specified emoji: "Infra" should be "🏗️ Infra", "Migration" should be "🔄 Migration", "TC" should be "🧪 TC" (or just the emoji per spec).
- **Fix:**
  ```csharp
  // Infrastructure
  b.AddContent(0, "🏗️ Infra")
  // Migration
  b.AddContent(0, "🔄 Migration")
  // TestCase
  b.AddContent(0, "🧪 TC")
  ```

#### I4 — ⛓ emoji missing from all predecessor chip labels
- **File:** `NexusArtifacts.razor` lines 317, 331, 344
- **Fix:**
  ```diff
  - $"[!] {Truncate(predTitle, 30)}"
  + $"⛓ [!] {Truncate(predTitle, 30)}"
  
  - $"Cross-Epic: ..."
  + $"⛓ Cross-Epic: ..."  // (also fix C1)
  
  - $"Blocked by: {Truncate(predTitle, 30)}"
  + $"⛓ Blocked by: {Truncate(predTitle, 30)}"
  ```

#### I5 — Cross-Epic and same-Epic chips visually identical
- **File:** `NexusArtifacts.razor` lines 329, 343
- **Issue:** Both use `Color.Warning`. Spec distinguishes: same-Epic = amber, cross-Epic = orange. User cannot differentiate at a glance.
- **Fix:** Add `Style="background-color: darkorange; color: white;"` on the cross-Epic chip (MudBlazor v7 has no built-in `Color.Orange`):
  ```csharp
  tb.AddAttribute(1, "Color", Color.Warning);
  tb.AddAttribute(5, "Style", "background-color: darkorange; color: white;");
  ```

#### I7 — DbContext injected directly (should use factory)
- **File:** `NexusArtifacts.razor` line 4
- **Issue:** `@inject NexusDbContext Db` creates a scoped DbContext tied to the SignalR circuit lifetime. Stale reads on reconnect, potential concurrency issues. `IDbContextFactory<NexusDbContext>` is already registered in Program.cs.
- **Fix:**
  ```diff
  - @inject NexusDbContext Db
  + @inject IDbContextFactory<NexusDbContext> DbFactory
  
  // In OnInitializedAsync:
  - var submission = await Db.Submissions
  + await using var db = await DbFactory.CreateDbContextAsync();
  + var submission = await db.Submissions
  // (and update all Db. → db. references)
  ```

#### I8 — No user ownership check on artifact access
- **File:** `NexusArtifacts.razor` lines 165–190; `NexusArtifactsController.cs`
- **Issue:** Any authenticated user can view any submission's WI tree by navigating to `/nexus/42/artifacts`. `SubmissionDetail.razor` enforces ownership via `SubmissionService.GetByIdAsync`. `NexusArtifacts.razor` queries the DB directly and only checks `[Authorize]`.
- **Fix:** After loading the submission, verify ownership:
  ```csharp
  var userId = ...; // from AuthenticationStateProvider or ClaimsPrincipal
  if (submission.CreatedBy != userId)
  {
      _error = "Access denied.";
      return;
  }
  ```
  Apply same check to the controller.

#### I9 — 🧪 emoji missing from Test Cases panel header and per-entry chip
- **File:** `NexusArtifacts.razor` lines 127, 134
- **Issue:** Panel header: `"Test Cases (@testCases.Count)"` → should be `"🧪 Test Cases (@testCases.Count)"`. Per-entry chip: `"TC"` → should be `"🧪"` or `"🧪 TC"`.
- **Fix:**
  ```diff
  - <MudText Typo="Typo.body2">Test Cases (@testCases.Count)</MudText>
  + <MudText Typo="Typo.body2">🧪 Test Cases (@testCases.Count)</MudText>
  
  - <MudChip T="string" Color="Color.Primary" Size="Size.Small">TC</MudChip>
  + <MudChip T="string" Color="Color.Primary" Size="Size.Small">🧪 TC</MudChip>
  ```

---

## Nitpicks [2]

- **N1:** Missing ⚠️ emoji in external deps alert. Fix: `"⚠️ @_artifactSet.ExternalDependencyCount external dependencies require…"`
- **N2:** Redundant `w.WorkItemType != "Test Case"` in `GetChildren` (line 221) — logically subsumed by the `wiType` match. Harmless.

---

## Positive Observations

- **Cross-Epic detection logic is solid.** `FindEpicFor` traverses the parent chain with a visited set to prevent infinite loops. `IsUnresolvedPredecessor` correctly checks against the full loaded WI set. Tony got the hardest logic right — the bugs are in the label text, not the detection.
- **Test Case grouping is structurally correct.** Excluded from main tree, `MudExpansionPanel` collapsed by default with `Expanded="false"`, matched to parent Story by `ParentTitle`. The grouping works — just needs the emoji.
- **Controller is well-structured.** Auth present, query uses `OrderByDescending(a => a.CreatedAt)` for latest-set semantics, correct 404 handling for missing submission/spec/artifact set. The ownership gap applies here too, but the query structure is correct.
- **SubmissionDetail nav button** is clean — correct route, no regressions.
- **MudBlazor v7 compatibility** is clean — `Expanded="false"` used (not `IsInitiallyExpanded`), `Icons.Material.Outlined.ContentCopy` used for the copy button (not the non-existent `OutboxRounded`).
- **ArtifactSet.ExternalDependencyCount** exists in the model. Data loading query uses correct Include + OrderByDescending pattern.

---

## What to Fix (NEEDS-CHANGES)

Tony can fix all of these in one pass. Priority order:

**1. Add `Description` field to `WorkItemRecord` (C2 root cause)**
```csharp
// WorkItemRecord.cs — add after ExternalOwner:
public string? Description { get; set; }
```
Confirm migration adds the column, or add it if missing.

**2. Fix Copy brief button and add description preview (C2)**
- Change `CopyToClipboard(extWi.Title)` → `CopyToClipboard(extWi.Description)`
- Add 120-char preview text block above the Copy button

**3. Fix Cross-Epic chip to include Epic name (C1)**
- Change `IsCrossEpicPredecessor` to return the predecessor Epic title (or null for same-Epic/unresolved)
- Update chip label: `"⛓ Cross-Epic: {epicName} > {truncatedTitle}"`

**4. Fix all missing emojis:**
- Badge labels: `"🏗️ Infra"`, `"🔄 Migration"`, `"🧪 TC"` (I3)
- Predecessor chips: add ⛓ prefix to all three types (I4)
- Test Case panel header and per-entry chip: add 🧪 (I9)
- External deps alert: add ⚠️ (N1)

**5. Fix Cross-Epic chip color to be visually distinct from same-Epic (I5)**
- Add `Style="background-color: darkorange; color: white;"` to cross-Epic chip

**6. Fix tag chips — iterate a Tags field, not TestedByTitles (I2)**
- Add `Tags` property to `WorkItemRecord` or derive visible tags from `ExternalOwner`

**7. Switch to IDbContextFactory (I7)** — lower urgency but correct pattern for Blazor Server.

**8. Add ownership check (I8)** — security gap; verify submission belongs to current user.

---

_Hawkeye — CC-assisted adversarial review. CC read all files; I confirmed both Criticals directly._
