# Review Report — ADO#3115

### Verdict: NEEDS-CHANGES

---

### CC Review Summary

CC confirmed 9 findings across the two repos. Dismissed Finding #4 (AC field names stale — both callers work correctly) and Finding #7 (endpoint URL in spec stale — all code consistent) as spec-documentation issues only, not code bugs. All other findings confirmed as real.

Key confirmed issues:
- `FaitV2IntegrationService` is dead code (registered but never called)
- fait-v2 `appsettings.json` is missing the `FirmIntegration` placeholder section
- Config key name asymmetry between FIRM and fait-v2 needs ops documentation
- Inject endpoint appends to conversation, not RAG — intent needs confirmation

---

### Consistency Audit

**Cross-file sync points checked:**

| FIRM Caller | fait-v2 Receiver | Value | Status |
|-------------|-----------------|-------|--------|
| `FaitV2IntegrationService.cs` → `FaitV2:SharedSecret` | `Program.cs:486` → `FirmIntegration:SharedSecret` | Config key names differ | ⚠️ Asymmetric — runtime OK if ops sets both, but naming is a trap |
| `MeetingDetail.razor` → `FaitV2:SharedSecret` | `Program.cs:486` → `FirmIntegration:SharedSecret` | Same asymmetry | ⚠️ Same |
| `FaitV2IntegrationService.cs:37` → `/api/assistant/inject` | `Program.cs:475` → `/api/assistant/inject` | URL matches | ✅ |
| `MeetingDetail.razor:561` → `/api/assistant/inject` | `Program.cs:475` → `/api/assistant/inject` | URL matches | ✅ |
| `FaitV2IntegrationService.cs:40-46` → `{ entraOid, content, sourceType, sourceId, title }` | `AssistantInjectRequest` record `Program.cs:843-849` | Fields match (case-insensitive deserializer) | ✅ |
| `MeetingDetail.razor:546-553` → `{ EntraOid, Content, SourceType, SourceId, Title }` | `AssistantInjectRequest` record | Fields match | ✅ |

**IFaitV2IntegrationService usage:**
- Registered: `FIRM Program.cs:95` ✅
- Called by MeetingDetail.razor: ❌ Never — direct HTTP call instead

---

### Issues Found

| Severity | Repo | File | Line | Issue |
|----------|------|------|------|-------|
| Critical | FIRM | `Services/FaitV2IntegrationService.cs` | all | Service is dead code — MeetingDetail bypasses it |
| Critical | fait-v2 | `src/FortressAI.V2.Web/appsettings.json` | N/A | No `FirmIntegration` section — ops has no record of required config key |
| Critical | Both | Config layer | N/A | Key name asymmetry: FIRM uses `FaitV2:SharedSecret`, fait-v2 uses `FirmIntegration:SharedSecret` |
| Important | fait-v2 | `src/FortressAI.V2.Web/Program.cs` | 502-504 | Injects to conversation, not RAG — AC #5 says WriteFactAsync |
| Important | FIRM | `Components/Pages/MeetingDetail.razor` | 557-559 | Sends request without auth header if secret not configured (opaque 401) |
| Important | Both | Both callers | — | PascalCase vs camelCase inconsistency between FIRM's two inject callers |
| Nitpick | fait-v2 | `src/FortressAI.V2.Web/Program.cs` | 507 | `AllowAnonymous` should have comment explaining why it's safe here |

---

### Spec Fidelity

**AC #1 — `/api/firm/inject` endpoint exists:** PASS with caveat. The endpoint is at `/api/assistant/inject`, not `/api/firm/inject`. Both FIRM callers use the correct actual URL. Spec AC URL is stale — update it.

**AC #2 — Reads `config["FirmIntegration:SharedSecret"]`:** PASS. `Program.cs:486` confirmed.

**AC #3 — Validates header, fails closed (401):** PASS. Both missing-secret (LogError + 401) and mismatch (LogWarning + 401) branches present. `Program.cs:487-496`.

**AC #4 — Accepts `{ userId, meetingId, transcript, source }`:** PASS functionally, FAIL on field names. Actual request shape is `{ EntraOid, Content, SourceType, SourceId, Title }`. Both FIRM callers send this correctly. AC was written with wrong field names — update the spec.

**AC #5 — Calls `IRAGWriteService.WriteFactAsync`:** FAIL. The inject endpoint calls `convService.AppendMessageAsync` (injects as a system message into the active chat session), not `ragWriteService.WriteFactAsync`. Whether this is intentional needs PM/Tony confirmation. If the intent is persistent RAG searchability, this is wrong. If the intent is session-scoped context injection, this is correct.

**AC #6 — `FaitV2IntegrationService.cs` exists:** PASS.

**AC #7 — `IFaitV2IntegrationService` interface defined:** PASS.

**AC #8 — Reads `FaitV2:BaseUrl` and `FaitV2:SharedSecret`:** PASS. `FaitV2IntegrationService.cs:27-28`.

**AC #9 — Sends `X-Firm-Secret` header:** PASS. `FaitV2IntegrationService.cs:48`.

**AC #10 — Fails gracefully if config not set:** PASS. `FaitV2IntegrationService.cs:29-33` checks and returns with LogWarning.

**AC #11 — Registered in FIRM `Program.cs`:** PASS. Line 95.

**AC #12 — `appsettings.json` has placeholder keys:** PARTIAL. `FaitV2:BaseUrl` and `FaitV2:SharedSecret` are present in FIRM ✅. fait-v2's `appsettings.json` has NO `FirmIntegration` section ❌.

**AC #13 — dotnet build passes in FIRM:** PASS. Build clean, 20 warnings (all pre-existing), 0 errors.

---

### What to Fix (NEEDS-CHANGES)

#### Fix 1 — Wire MeetingDetail to use `IFaitV2IntegrationService` (Critical)

`FaitV2IntegrationService` exists precisely to encapsulate this logic. MeetingDetail.razor reimplements it inline. Wire it up:

```diff
// MeetingDetail.razor — inject the service
+@inject IFaitV2IntegrationService FaitV2Service

// Replace the inline HTTP block (lines ~523-588) with:
-    var payload = new { ... };
-    var faitV2BaseUrl = ...;
-    var client = HttpClientFactory.CreateClient();
-    var firmSecret = Configuration["FaitV2:SharedSecret"];
-    if (!string.IsNullOrEmpty(firmSecret))
-        client.DefaultRequestHeaders.Add("X-Firm-Secret", firmSecret);
-    var response = await client.PostAsJsonAsync($"{faitV2BaseUrl}/api/assistant/inject", payload);
+    await FaitV2Service.SendTranscriptAsync(
+        entraOid: _user?.EntraOid ?? "",
+        meetingId: _meeting.Id.ToString(),
+        transcript: content,
+        title: _meeting.Title ?? $"Meeting {_meeting.Id}"
+    );
```

This makes the service do its job and removes dead code.

#### Fix 2 — Add `FirmIntegration` placeholder to fait-v2 `appsettings.json` (Critical)

```json
"FirmIntegration": {
  "SharedSecret": ""
}
```

This documents the expected config key for ops. Without it, the config structure is invisible.

#### Fix 3 — Confirm RAG vs. conversation injection intent (Important, blocks for PM)

AC #5 says `WriteFactAsync`. The implementation appends to the active chat conversation. These are different behaviors:
- **Conversation injection:** Available only in the current session. Gets included in next CC turn. Ephemeral.
- **RAG write:** Persists to vector memory. Searchable in future sessions. Permanent.

If meeting context should persist beyond the current session for future KB queries, the inject endpoint needs to also call `ragWriteService.WriteFactAsync`. If session-only is fine, update AC #5 to reflect actual behavior.

---

### Security Quick-Check

- `X-Firm-Secret` validation: ✅ fails closed, both branches return 401
- `.AllowAnonymous()`: ✅ intentional (FIRM can't pass user cookies), guarded by shared secret
- No hardcoded secrets: ✅
- Input validation: ✅ — `EntraOid` null check at `Program.cs:499`, user lookup before proceeding
- Secret not in logs: ✅ — only logged that it was missing/rejected, not the value

---

### FIRM `dotnet build` Result

```
Build succeeded.
    20 Warning(s)
    0 Error(s)
```
All 20 warnings are pre-existing (nullable CS8669, CS8602, CS8604, MUD0002). Zero new warnings from ADO#3115 changes.

---

_Hawkeye — ADO#3115 review complete. NEEDS-CHANGES on 3 blocking items (dead service, missing fait-v2 appsettings placeholder, RAG-vs-conversation intent). Build is functionally wired but the service layer is being bypassed. Fix #1 and #2 before deploy; confirm #3 with Fred/PM._
