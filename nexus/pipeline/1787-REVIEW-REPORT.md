# Review Report — ADO #1787
## FileUploadZone.razor — MIME empty-string fallback to extension check

**Reviewer:** Hawkeye (code-reviewer)
**Commit:** `3faa75c`
**Cycle:** 1
**Date:** 2026-04-13

---

### Verdict: NEEDS-CHANGES

---

### Spec Compliance Check

**§ Codebase Map:**
- `FortressNexus.Web/Components/Shared/FileUploadZone.razor` — ✅ modified as specified

**§ Out of Scope:**
- ✅ No out-of-scope app code changes. Five pipeline doc files also in the commit (`firm/pipeline/`) are housekeeping carry-along, not app changes.

**§ Acceptance Criteria:**
- [x] `_allowedExtensions` HashSet added — ✅ present
- [x] Extension fallback logic implemented — ✅ present (but incorrectly — see Critical #1)
- [x] `AcceptedTypes` default unchanged — ✅ confirmed
- [x] User-friendly error message — ✅ present
- [ ] Fallback activates ONLY when MIME is empty/missing — ❌ NOT met (see Critical #1)

**Spec compliance verdict:** ❌ NON-COMPLIANT — fallback logic does not match stated intent

---

### Consistency Audit

**Files Cross-Referenced:**
- `FileUploadZone.razor` (MIME allowlist) ↔ `FileUploadZone.razor` (_allowedExtensions) — ⚠️ `image/jpg` present in AcceptedTypes but non-standard; dead entry
- Scope: single-file change, no cross-file contracts involved

**Undocumented Dependencies Found:**
- None — `_allowedExtensions` is private/static, no external consumers

---

### Critical Issues [1]

#### C1: Extension fallback bypasses MIME guard for non-allowlisted MIMEs

- **File:** `FileUploadZone.razor` (line 91)
- **Category:** correctness / security
- **Issue:** The rejection condition `!mimeOk && !extOk` rejects only when BOTH checks fail. When a file has a non-empty, non-allowlisted MIME (e.g. `application/octet-stream`) but a matching extension (e.g. `.md`), `extOk=true` and the file **passes**. This is the exact inverse of the stated intent: "if MIME is empty, fall back to extension."

**Logic table:**

| Scenario | mime | mimeOk | extOk | Result |
|---|---|---|---|---|
| Normal image/jpeg .jpg | `image/jpeg` | true | true | ✅ accepted |
| Empty MIME, .md | `""` | false | true | ✅ accepted (intended fallback) |
| `application/octet-stream`, renamed to .md | `application/octet-stream` | **false** | **true** | **❌ accepted — BUG** |
| `application/octet-stream`, .exe | `application/octet-stream` | false | false | ✅ rejected |

**Evidence:**
```csharp
// Current — INSECURE
var mimeOk = !string.IsNullOrEmpty(mime) && AcceptedTypes.Contains(mime);
var extOk = _allowedExtensions.Contains(ext);
if (!mimeOk && !extOk)   // ← only rejects when BOTH fail
{
    _errorMessage = ...;
    return;
}
```

**Impact:** Attacker renames `malware.exe` → `malware.md`. Browser reports `ContentType = "application/octet-stream"`. mimeOk = false, extOk = true → file accepted. This defeats the MIME guard entirely for the extension set.

**Fix:**
```diff
- if (!mimeOk && !extOk)
+ var valid = mimeOk || (string.IsNullOrEmpty(mime) && extOk);
+ if (!valid)
```

---

### Important Issues [1]

#### I1: `image/jpg` is a non-standard, dead MIME entry in AcceptedTypes

- **File:** `FileUploadZone.razor` (line 53)
- **Category:** correctness
- **Issue:** `image/jpg` is not a registered IANA MIME type. Browsers report JPEG files as `image/jpeg` only. Under the **fixed** logic (where MIME path is authoritative), `.jpg` files uploaded from a browser that reports `image/jpeg` will correctly match via `image/jpeg` in the allowlist. However, `image/jpg` will never be set by any browser, making it dead weight. Its presence implies a false belief that browsers use `image/jpg`, which could mislead future maintainers.
- **Fix:** Remove `"image/jpg"` from `AcceptedTypes`. `.jpg` files are covered by `image/jpeg` (MIME path) and `.jpg` (extension fallback for empty-MIME cases).

---

### Nitpicks [2]

- **N1:** `GetFileIcon(string contentType)` at line 113 calls `contentType.ToLowerInvariant()` directly. `IBrowserFile.ContentType` can return null in practice even though typed non-nullable in the interface. Call site at line 36 should pass `capturedFile.ContentType ?? ""`, or the method parameter should be `string?` with a null-guard. Not blocking (doesn't affect validation path), but could NRE at render time on malformed uploads.

- **N2:** `_hint` (line 73) and error message (line 93) use different wording and ordering for the same allowed-types list. Not blocking; minor UX polish.

---

### Positive Observations

- `Path.GetExtension(file.Name)?.ToLowerInvariant() ?? ""` — null safety is correct. `.NET 6+` `Path.GetExtension` accepts nullable input and returns null (no throw). The chain handles this correctly.
- `AcceptedTypes` default completeness confirmed: `text/markdown`, `text/x-markdown`, `application/json`, `text/plain` all present.
- `StringComparer.OrdinalIgnoreCase` on the HashSet is appropriate — extension comparison is case-insensitive.
- Scope is clean — only one app file changed.

---

### CC Review Summary

CC (Claude Code Sonnet, run 2026-04-13) confirmed:
- **C1 bypass confirmed** — logic table proves the exploit
- **Null safety on `Path.GetExtension` is sound** — no issue there
- **AcceptedTypes completeness confirmed** — all four required MIMEs present
- **`image/jpg` dead entry** flagged as important but non-blocking
- **`GetFileIcon` null risk** flagged as nitpick

No false positives dismissed.

---

### What Tony Needs to Fix (NEEDS-CHANGES)

**Required (blocking):**

1. **`FileUploadZone.razor` line 91 — fix the validation condition:**
   ```diff
   - if (!mimeOk && !extOk)
   + var valid = mimeOk || (string.IsNullOrEmpty(mime) && extOk);
   + if (!valid)
   ```

**Recommended (non-blocking, same cycle is fine):**

2. **Line 53 — remove `"image/jpg"` from `AcceptedTypes`:**
   ```diff
   - [Parameter] public string[] AcceptedTypes { get; set; } = ["text/html", "image/png", "image/jpeg", "image/jpg", ...];
   + [Parameter] public string[] AcceptedTypes { get; set; } = ["text/html", "image/png", "image/jpeg", ...];
   ```

3. **Line 36 — guard against null ContentType in GetFileIcon call:**
   ```diff
   - <MudIcon Icon="@GetFileIcon(capturedFile.ContentType)" .../>
   + <MudIcon Icon="@GetFileIcon(capturedFile.ContentType ?? "")" .../>
   ```

---

_Hawkeye — review complete. The bypass is real and must be fixed before merge._

---

## Cycle 2 Review

**Reviewer:** Hawkeye (code-reviewer)
**Commit:** `98c1500`
**Cycle:** 2
**Date:** 2026-04-13

---

### Verdict: PASS

---

### Spec Compliance Check

**Cycle 2 fixes from C1 review:**
- [x] C1 fix (`mimeOk || (string.IsNullOrEmpty(mime) && extOk)`) — ✅ applied verbatim
- [x] I1 fix (`"image/jpg"` removed) — ✅ confirmed removed; `"image/jpeg"` retained

**§ Out of Scope:**
- ✅ Only `FileUploadZone.razor` modified in commit `98c1500`

**Spec compliance verdict:** ✅ COMPLIANT

---

### CC Review Summary

CC (Claude Code Sonnet, run 2026-04-13) performed:
- Verbatim logic verification
- 4-scenario trace (A–D) through actual code
- Regression check on all surrounding logic
- Scope check via `git show 98c1500`

No false positives. All findings confirmed real.

---

### Validation Logic Verified (lines 89–92)

```csharp
var mimeOk = !string.IsNullOrEmpty(mime) && AcceptedTypes.Contains(mime);
var extOk = _allowedExtensions.Contains(ext);
var valid = mimeOk || (string.IsNullOrEmpty(mime) && extOk);
if (!valid)
```

Matches required spec exactly.

---

### Scenario Trace

| Scenario | mime | ext | mimeOk | extOk | IsNullOrEmpty(mime) | valid | Result |
|---|---|---|---|---|---|---|---|
| A — normal MIME | `image/jpeg` | `.jpg` | true | true | false | true | ✅ ACCEPT |
| B — empty MIME fallback | `""` | `.md` | false | true | true | true | ✅ ACCEPT |
| C — non-allowlisted MIME + allowlisted ext | `application/octet-stream` | `.md` | false | true | false | **false** | ✅ REJECT |
| D — both fail | `application/octet-stream` | `.exe` | false | false | false | false | ✅ REJECT |

Scenario C correctly REJECTs — the C1 bypass is closed.

---

### AcceptedTypes Default Array

```csharp
["text/html", "image/png", "image/jpeg", "image/webp", "application/pdf",
 "text/markdown", "text/x-markdown", "application/json", "text/plain"]
```

- `"image/jpg"` — ✅ absent
- `"image/jpeg"` — ✅ present

---

### Regression Checks

| Check | Status |
|---|---|
| `StringComparer.OrdinalIgnoreCase` on `_allowedExtensions` HashSet | ✅ PRESENT (line 66) |
| `Path.GetExtension(file.Name)?.ToLowerInvariant() ?? ""` | ✅ PRESENT (line 88) |
| `AcceptedTypes.Contains(mime)` | ✅ PRESENT (line 89) |
| `_errorMessage` + `return;` in rejection branch | ✅ PRESENT (lines 94–95) |

No regressions.

---

### Critical Issues [0]
None.

### Important Issues [0]
None.

### Nitpicks
- N1 (from C1, carried forward): `GetFileIcon` null-ContentType risk at render time. Non-blocking; not in scope for this cycle.
- N2 (from C1, carried forward): `_hint` and error message wording inconsistency. Non-blocking UX polish.

---

### Positive Observations
- Logic verbatim match to c1 required fix — no creative interpretation, clean execution
- Single-file commit scope maintained
- `_allowedExtensions` HashSet construction unchanged — OrdinalIgnoreCase retained

---

_Hawkeye — C1 issues resolved correctly. No regressions. PASS._
