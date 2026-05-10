# Review Report — ADO#3159

### Verdict: NEEDS-CHANGES

---

### Spec Compliance Check

**Feature:** Epic 2, Feature 2.1 — Assistant Settings Page (partial)  
**WI scope:** 5 fields (AssistantName, PreferredName, CommunicationStyle, ResponseFormat, ColorHex) + sidebar nav entry. Avatar upload deferred.

**Files modified:**
- `src/FortressAI.Web/Components/Pages/AssistantSettings.razor` ✅ created as specified
- `src/FortressAI.Web/Components/Layout/SidebarContent.razor` ✅ nav entry added

**Out of scope:**
- ✅ No out-of-scope changes detected. Avatar upload absent as expected for this WI.

**Acceptance criteria:**
- [x] `/assistant-settings` page created ✅
- [x] All 5 fields present ✅
- [x] Auth guard present ✅
- [x] Pre-populated from GetOrCreateConfigAsync ✅ — with one critical failure (see C1 below)
- [x] Save via DbFactory upsert ✅
- [x] Snackbar on save ✅
- [x] Sidebar nav entry ✅
- [x] 0 build errors ✅

**Spec compliance verdict:** ❌ NON-COMPLIANT — C1 blocks pass

---

### Consistency Audit

**Files cross-referenced:**
- `AssistantSettings.razor` ↔ `AssistantSetup.razor` — ❌ **CASE MISMATCH** (see C1)
- `AssistantSettings.razor` ↔ `UserAssistantConfig.cs` — ✅ all properties exist
- `AssistantSettings.razor` ↔ `AppDbContext.cs` (EF mappings) — ✅ all columns verified
- `SidebarContent.razor` ↔ existing nav items — ✅ no regressions

---

### Critical Issues [1]

#### C1: CommunicationStyle / ResponseFormat case mismatch with AssistantSetup wizard

- **File:** `src/FortressAI.Web/Components/Pages/AssistantSettings.razor` (lines 142–143)
- **Category:** Consistency (cross-file contract)
- **Issue:** `AssistantSetup.razor` writes PascalCase values to the DB: `"Balanced"`, `"Concise"`, `"Detailed"` for CommunicationStyle; `"Mixed"`, `"Prose"`, `"Structured"`, `"Bullets"` for ResponseFormat. `AssistantSettings.razor` assigns the raw DB value directly to `_communicationStyle` / `_responseFormat`, but all `MudSelectItem` values are lowercase (`"balanced"`, `"mixed"`, etc.). MudSelect won't match `"Balanced"` to `"balanced"` — both dropdowns render **blank** for any user who completed onboarding.

  Additionally, `AssistantSetup` has a `"Structured"` option that has no equivalent in `AssistantSettings` (which has `"technical"` instead). A user who chose `"Structured"` during setup sees a blank dropdown and silently **loses that setting** on next save (it saves as `""` or whatever lowercase misfit is selected next).

- **Evidence (AssistantSetup.razor):**
  ```
  line 88:  <option value="Concise">
  line 89:  <option value="Balanced">
  line 218: private string _communicationStyle = "Balanced";
  line 361: config.CommunicationStyle = _communicationStyle;
  ```
- **Evidence (AssistantSettings.razor):**
  ```csharp
  // line 142
  _communicationStyle = !string.IsNullOrWhiteSpace(config.CommunicationStyle) 
      ? config.CommunicationStyle   // "Balanced" from DB — no match in MudSelect
      : "balanced";
  
  // MudSelectItem values: "concise", "balanced", "detailed" — all lowercase
  ```
- **Impact:** Every user who completed onboarding has broken dropdowns. On save, their preference is written as the default lowercase value, silently overwriting their actual setting.
- **Fix:**
  ```diff
  - _communicationStyle = !string.IsNullOrWhiteSpace(config.CommunicationStyle) ? config.CommunicationStyle : "balanced";
  - _responseFormat = !string.IsNullOrWhiteSpace(config.ResponseFormat) ? config.ResponseFormat : "mixed";
  + _communicationStyle = !string.IsNullOrWhiteSpace(config.CommunicationStyle) ? config.CommunicationStyle.ToLowerInvariant() : "balanced";
  + _responseFormat = !string.IsNullOrWhiteSpace(config.ResponseFormat) ? config.ResponseFormat.ToLowerInvariant() : "mixed";
  ```
  Also decide on `"Structured"`: either add it as an option in AssistantSettings or map it explicitly on normalize:
  ```csharp
  // Option B: map "structured" → "prose" (closest equivalent) or add it as a 5th option
  if (_responseFormat == "structured") _responseFormat = "prose";
  ```

---

### Important Issues [0]

None.

---

### Nitpick Issues [1]

#### N1: CSS variable compliance — swatch rendering

- **File:** `AssistantSettings.razor` (swatch `<div>` inline style, ~line 91)
- **Category:** Quality / CSS convention
- **Issue:** Hardcoded px/rem/color values in inline style:
  - `width: 36px`, `height: 36px` — should be CSS variables
  - `border: 3px solid ...` — `3px` hardcoded
  - `box-shadow: 0 0 0 2px {hex}` — dimensions hardcoded
  - `transition: transform 0.1s` — should use `var(--transition-fast)`
  - `color: white` on check icon — hardcoded
  - `font-size: 1.1rem` on check icon — hardcoded
- **Impact:** Cosmetic only — will diverge from design system if tokens change.
- **Fix:** Refactor swatch rendering into a CSS class with variables. Not blocking.

---

### What to fix (NEEDS-CHANGES)

**Tony — one change required before this ships:**

In `AssistantSettings.razor`, `OnInitializedAsync`, lines 142–143:

```diff
- _communicationStyle = !string.IsNullOrWhiteSpace(config.CommunicationStyle) ? config.CommunicationStyle : "balanced";
- _responseFormat = !string.IsNullOrWhiteSpace(config.ResponseFormat) ? config.ResponseFormat : "mixed";
+ _communicationStyle = !string.IsNullOrWhiteSpace(config.CommunicationStyle) ? config.CommunicationStyle.ToLowerInvariant() : "balanced";
+ _responseFormat = !string.IsNullOrWhiteSpace(config.ResponseFormat) ? config.ResponseFormat.ToLowerInvariant() : "mixed";
```

Also, `AssistantSetup` has a `"Structured"` ResponseFormat option. That value doesn't exist as a MudSelectItem in AssistantSettings. After `.ToLowerInvariant()`, it would become `"structured"` which still matches nothing. Pick one:
- **Option A:** Add `<MudSelectItem Value="@("structured")">Structured — headers and sections</MudSelectItem>` to the ResponseFormat MudSelect
- **Option B:** Map `"structured"` → `"prose"` after normalization (lossy, not ideal)

Recommendation: **Option A** — keep parity with the wizard's option set.

CSS swatch variables (N1) can be a follow-up task.

---

### CC Review Summary

CC Sonnet reviewed all files against the brief. One blocking consistency issue found (C1) — case mismatch confirmed by direct inspection of `AssistantSetup.razor`. All other checklist items PASS. DB schema audit clean — all five SaveSettings() properties exist on entity and have valid EF mappings. Build: 0 errors, 32 pre-existing warnings.

---

_Hawkeye — review cycle 1 of ADO#3159_
