## Review Report — ADO#3108

### Verdict: NEEDS-CHANGES

**Commit:** `69fd41a8`  
**Files reviewed:**
- `Services/ContextEnvelopeService.cs`
- `Services/IContextEnvelopeService.cs`
- `Services/IUserAgentRuntime.cs`
- `Services/ICCExecutionService.cs`
- `Components/Chat/ChatView.razor`
**Also checked:** `Services/FargateCCExecutionService.cs`, `agent-harness/harness-server.js`  
**Build:** `dotnet build` ✅ 0 errors, 2 pre-existing warnings (unrelated)

---

### CC Review Summary

CC traced the full `UserEmail` data flow end-to-end and found a critical gap: the email is collected correctly at the UI layer, stored in `TurnRequest` and `CCContextEnvelope`, but **never rendered into the identity section that CC or Bedrock actually sees**. AC#3 — the primary deliverable of this WI — is unimplemented. All other criteria pass.

---

### Spec Compliance Check

**§7 Acceptance Criteria:**
- [x] AC#1: `TurnRequest` has `UserEmail` (`string?`, nullable, default null) ✅
- [x] AC#2: `ContextEnvelopeService.BuildEnvelopeAsync` accepts and uses `userEmail` ✅
- [ ] **AC#3: §1 identity section includes `Email: {userEmail}` or `Email: unknown`** ❌ **NOT MET**
- [x] AC#4: `ChatView.razor` reads email from `preferred_username` ?? `upn` claim ✅
- [x] AC#5: `ChatView.razor` passes email in `TurnRequest.UserEmail` ✅
- [ ] AC#6: No hardcoded CSS values ⚠️ Minor violations (see below)
- [x] AC#7: `dotnet build` 0 errors ✅

**Spec compliance verdict:** ❌ NON-COMPLIANT — AC#3 blocks PASS

---

### Consistency Audit

**Files cross-referenced:** ChatView.razor ↔ IUserAgentRuntime.cs ↔ ICCExecutionService.cs ↔ IContextEnvelopeService.cs ↔ ContextEnvelopeService.cs ↔ FargateCCExecutionService.cs ↔ harness-server.js

| File pair | Check | Result |
|---|---|---|
| ChatView.razor ↔ IContextEnvelopeService.cs | `BuildEnvelopeAsync` call site matches new 3-arg signature | ✅ |
| IContextEnvelopeService.cs ↔ ContextEnvelopeService.cs | Interface/implementation signature match | ✅ |
| ChatView.razor ↔ IUserAgentRuntime.cs | `TurnRequest` call site includes `UserEmail` | ✅ |
| CCContextEnvelope.UserEmail ↔ FargateCCExecutionService.BuildPrompt | `UserEmail` rendered into identity section | ❌ **MISSING** |
| TurnRequest.UserEmail ↔ harness-server.js rawBody extraction | `UserEmail`/`userEmail` extracted from body | ❌ **MISSING** |

**Undocumented issue:** `harness-server.js` never extracts `UserEmail` from `rawBody`, so neither the CC spawn path nor the Bedrock path can inject it into the system prompt, regardless of what `FargateCCExecutionService` does.

---

### Critical Issues [1]

#### C1: UserEmail not rendered into the §1 identity section
- **Files:** `Services/FargateCCExecutionService.cs` (BuildPrompt method, ~line 155) and `agent-harness/harness-server.js` (~lines 920, 996, 1126)
- **Category:** correctness / spec non-compliance
- **Issue:** `UserEmail` is correctly stored in `CCContextEnvelope.UserEmail` and `TurnRequest.UserEmail`, but `FargateCCExecutionService.BuildPrompt` never reads it — the `## Identity` section only outputs `User ID` and `User Name`. Additionally, the harness never extracts `UserEmail`/`userEmail` from `rawBody`, so neither the CC spawn path (contextParts injection) nor the Bedrock path (systemParts injection) has access to the email value at all.
- **Impact:** CC and Bedrock never see the user's email. AC#3 is completely unimplemented despite the data plumbing being correct up to the final render step.
- **Fix — two-part:**

**Part A: FargateCCExecutionService.cs, BuildPrompt**
```diff
 ## Identity
 User ID: {envelope.UserId}
 User Name: {envelope.UserDisplayName}
+Email: {envelope.UserEmail ?? "unknown"}
```

**Part B: harness-server.js, rawBody destructuring (~line 923)**
```diff
 const pluginAgentId = rawBody.PluginAgentId ?? rawBody.pluginAgentId ?? null;
+const userEmail = rawBody.UserEmail ?? rawBody.userEmail ?? null;
```
Then inject into contextParts (CC path, ~line 999) and systemParts (Bedrock path, ~line 1126) — e.g., append to `systemPrompt` injection point or add a dedicated `## Current User` section.

---

### Important Issues [1]

#### I1: Dead code — preference detection functions never called
- **File:** `agent-harness/harness-server.js` (lines 876-906)
- **Category:** correctness / code hygiene
- **Issue:** `PREFERENCE_PATTERNS`, `hasPreferenceSignal()`, and `firePreferenceWrite()` were added in this commit (ADO#3093 scope), but the call site in the Bedrock path was removed by the subsequent ADO#3105 commit. All three are now unreachable dead code.
- **Impact:** No functional regression. But these functions were bundled into this commit out-of-scope and are now orphaned.
- **Fix:** Remove them from harness-server.js (or confirm they're intentionally staged for a future re-enable).

---

### Minor Issues

| Severity | File | Lines | Issue |
|----------|------|-------|-------|
| Nitpick | ChatView.razor | 414-427 | Hardcoded CSS fallback values inside `var()`: `#444`, `#999`, `#7c83ff`, bare `6px`, `4px`, `12px`. If AC#6 means strictly zero new hardcoded values, these fail. If `var()` fallbacks are permitted, only the bare pixel values at lines 416-417/422 are violations. |
| Nitpick | MainLayout.razor | 36 | `MiniWidth="56px"` — hardcoded MudBlazor component prop. |

---

### Spec Fidelity

5 of 7 AC pass. AC#3 (the WI's entire point — surfacing user email in the CC context) is not implemented. The data pipeline is correctly wired all the way through to the envelope, but the final render step is missing in both `FargateCCExecutionService.BuildPrompt` and `harness-server.js`.

---

### What to fix

**Required before PASS:**

**1. Add Email line to FargateCCExecutionService.BuildPrompt** (`Services/FargateCCExecutionService.cs`, ~line 161)
```diff
 User ID: {envelope.UserId}
 User Name: {envelope.UserDisplayName}
+Email: {envelope.UserEmail ?? "unknown"}
```

**2. Extract UserEmail in harness-server.js** (~line 923, after `pluginAgentId` line)
```diff
 const pluginAgentId = rawBody.PluginAgentId ?? rawBody.pluginAgentId ?? null;
+const userEmail = rawBody.UserEmail ?? rawBody.userEmail ?? null;
```
Then in the CC spawn path contextParts section (after `if (systemPrompt) contextParts.push(systemPrompt);`, ~line 999):
```diff
+if (userEmail) contextParts.push(`## Current User\nEmail: ${userEmail}`);
```
And in the Bedrock path systemParts section (after `if (systemPrompt) systemParts.push(systemPrompt);`, ~line 1126):
```diff
+if (userEmail) systemParts.push(`## Current User\nEmail: ${userEmail}`);
```

**3. Clean up dead preference detection code** (harness-server.js lines 876-906)
Remove `PREFERENCE_PATTERNS`, `hasPreferenceSignal`, and `firePreferenceWrite` — they have no call site after ADO#3105.

---

_Review by Hawkeye (Clint Barton) — 2026-05-09_
