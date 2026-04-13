# QA Report: NEXUS — ADO #1787 — FileUploadZone Extension Fallback

**Verdict: ✅ PASS — 3/3 TCs verified**

**QA Analyst:** Natasha Romanoff (Black Widow)
**Date:** 2026-04-13
**Test Start:** ~15:37 EDT
**Test Duration:** ~5 minutes
**ADO:** FAIT #1787
**Commit:** `98c1500` — `fix(nexus#1787): extension fallback only when MIME empty; remove non-standard image/jpg`
**Task Definition:** `nexus-web:29` (current, unchanged — force-new-deploy reuses tag)

---

## Environment

| Item | Value |
|------|-------|
| Target URL | https://nexus.fortressam.ai |
| Cluster | fortress-tools-cluster (us-east-1) |
| Task Def | nexus-web:29 |
| ECR Image Pushed | 2026-04-13T15:32:01 EDT |
| Container Status | RUNNING / HEALTHY |
| ECS Running / Desired | 1 / 1 |
| Changed File | `src/FortressNexus.Web/Components/Shared/FileUploadZone.razor` |

---

## Infrastructure Smoke Check

| Check | Result | Detail |
|-------|--------|--------|
| ECS task RUNNING | ✅ PASS | lastStatus=RUNNING, rolloutState=COMPLETED |
| ECR image freshness | ✅ PASS | Latest image pushed 15:32:01 EDT — after build trigger ~15:31:03 EDT |
| Deployed commit matches | ✅ PASS | Build report Cycle 2 commit = `98c1500`; image pushed after build succeeded |
| Service stable | ✅ PASS | 1 running, 0 pending, rollout COMPLETED |

> **Cloudflare note (carry-forward from prior QA cycles):** `nexus.fortressam.ai` is behind Cloudflare Turnstile (bot protection). Headless Chrome cannot pass the managed challenge. This blocks live browser E2E on this domain. All functional verification is performed via source code analysis of the deployed commit + ECS/ECR confirmation that the deployed image matches the fix — consistent with the verification method used on all prior NEXUS QA cycles.

---

## Test Cases

---

### TC1 (CRITICAL) — `.md` files are accepted

**Verdict: ✅ PASS**

**What the fix does:**

The `HandleFilesChanged` method now computes:

```csharp
var mime = file.ContentType?.ToLowerInvariant() ?? "";
var ext = Path.GetExtension(file.Name)?.ToLowerInvariant() ?? "";
var mimeOk = !string.IsNullOrEmpty(mime) && AcceptedTypes.Contains(mime);
var extOk = _allowedExtensions.Contains(ext);
var valid = mimeOk || (string.IsNullOrEmpty(mime) && extOk);
```

**For a `.md` file (browser reports empty `ContentType`):**

| Variable | Value | Reasoning |
|----------|-------|-----------|
| `mime` | `""` | Browsers report empty/null ContentType for `.md` |
| `ext` | `".md"` | `Path.GetExtension("readme.md")` → `".md"` |
| `mimeOk` | `false` | `!string.IsNullOrEmpty("")` = `false` |
| `extOk` | `true` | `".md"` is in `_allowedExtensions` (StringComparer.OrdinalIgnoreCase) |
| `valid` | **`true`** | `false \|\| (string.IsNullOrEmpty("") && true)` = `false \|\| (true && true)` = `true` |

Result: file passes validation, no error message set, file appears in `_selectedFiles`, `OnFilesSelected` fires. ✅

**Git diff confirmation** (commit `3faa75c` → `98c1500`):

```diff
-            if (!mimeOk && !extOk)
+            var valid = mimeOk || (string.IsNullOrEmpty(mime) && extOk);
+            if (!valid)
```

The old `if (!mimeOk && !extOk)` was inverted logic — it only rejected if BOTH checks failed. A `.md` file with `mimeOk=false` and `extOk=true` would have had `!false && !true` = `true && false` = `false` — meaning it would NOT enter the error block.

> Wait — re-evaluating old logic: old code was `if (!mimeOk && !extOk)` → reject only when BOTH fail. For `.md`: `mimeOk=false, extOk=true` → `!false && !true` → `true && false` → `false` → skip reject → file passes under old code too.
>
> **Correction:** The `.md` acceptance bug was present in the FIRST version of the fix (commit `3faa75c` — `mimeOk || extOk`) which was **too permissive** (security gap). The current commit `98c1500` is a security tightening cycle. For `.md` specifically:
>
> - `3faa75c` (cycle 1): `valid = mimeOk || extOk` → `.md` passes ✅
> - `98c1500` (cycle 2): `valid = mimeOk || (string.IsNullOrEmpty(mime) && extOk)` → `.md` passes ✅ (MIME is empty, so fallback fires)
>
> TC1 behavior is preserved through the security fix. `.md` files still accepted. ✅

---

### TC2 (IMPORTANT/SECURITY) — Renamed non-allowed file is rejected

**Verdict: ✅ PASS**

**Code verification at commit `98c1500`:**

```csharp
var valid = mimeOk || (string.IsNullOrEmpty(mime) && extOk);
```

**Confirmed:** This is `mimeOk || (string.IsNullOrEmpty(mime) && extOk)` — NOT `mimeOk || extOk`. ✅

**Security scenario — `malware.exe` renamed to `malware.md`:**

| Variable | Value | Reasoning |
|----------|-------|-----------|
| `mime` | `"application/octet-stream"` | Browser reports non-empty, non-allowlisted MIME for .exe content |
| `mimeOk` | `false` | `"application/octet-stream"` not in `AcceptedTypes` |
| `extOk` | `true` | `".md"` is in `_allowedExtensions` |
| `string.IsNullOrEmpty(mime)` | `false` | MIME is `"application/octet-stream"` — not empty |
| `valid` | **`false`** | `false \|\| (false && true)` = `false \|\| false` = `false` |

Result: file is **rejected** with error message. The extension match alone is not sufficient — MIME must be empty for the extension fallback to fire. ✅

**Contrast with cycle 1 security gap (`3faa75c` — `mimeOk || extOk`):**

| Variable | Value |
|----------|-------|
| `valid` (old) | `false \|\| true` = `true` ← **SECURITY HOLE — would have accepted** |
| `valid` (new) | `false \|\| (false && true)` = `false` ← **CORRECTLY REJECTED** ✅ |

The security fix closes the gap precisely. The condition `string.IsNullOrEmpty(mime)` gates the extension fallback — it only activates when the browser provides no MIME information at all, which is the specific case for `.md` files (not for binary files posing as `.md`). ✅

**`"image/jpg"` removal confirmed:**

```diff
-    [Parameter] public string[] AcceptedTypes { get; set; } = ["text/html", "image/png", "image/jpeg", "image/jpg", "image/webp", ...];
+    [Parameter] public string[] AcceptedTypes { get; set; } = ["text/html", "image/png", "image/jpeg", "image/webp", ...];
```

`"image/jpg"` removed. Browsers always report JPEG as `"image/jpeg"`. Legitimate JPEG files still accepted via `"image/jpeg"` in `AcceptedTypes` → `mimeOk = true`. ✅

---

### TC3 — Other allowed types still work

**Verdict: ✅ PASS**

**`.pdf`, `.txt`, `.json` files — MIME-based acceptance path:**

Browsers report well-known MIME types for these formats. The `mimeOk` path fires, bypassing the extension fallback entirely.

| File | Browser MIME | `mimeOk` | `valid` | Result |
|------|-------------|---------|--------|--------|
| `doc.pdf` | `application/pdf` | `true` | `true` | ✅ Accepted |
| `notes.txt` | `text/plain` | `true` | `true` | ✅ Accepted |
| `config.json` | `application/json` | `true` | `true` | ✅ Accepted |

All three are in `AcceptedTypes` default:
```csharp
["text/html", "image/png", "image/jpeg", "image/webp", "application/pdf",
 "text/markdown", "text/x-markdown", "application/json", "text/plain"]
```

`valid = true || (...)` short-circuits to `true` regardless of `string.IsNullOrEmpty(mime)`. These types are unaffected by the security tightening. ✅

---

## TC Summary

| TC | Priority | Test | Verdict | Method |
|----|----------|------|---------|--------|
| TC1 | 🔴 CRITICAL | `.md` file accepted | ✅ PASS | Code logic trace + git diff |
| TC2 | 🟠 IMPORTANT (security) | Condition is `mimeOk \|\| (IsNullOrEmpty(mime) && extOk)` — not `mimeOk \|\| extOk` | ✅ PASS | Direct code read at commit `98c1500` + security scenario trace |
| TC3 | 🔵 INFORMATIONAL | `.pdf`, `.txt`, `.json` still accepted | ✅ PASS | AcceptedTypes verification + logic trace |

- **Total TCs:** 3
- **Passed:** 3
- **Failed:** 0
- **Skipped:** 0
- **Critical TCs:** TC1 ✅, TC2 ✅

---

## Verification Method

Live browser E2E testing is blocked by Cloudflare Turnstile on `nexus.fortressam.ai` — this is a known constraint carried across all NEXUS QA cycles. Verification performed via:

1. **ECR image freshness** — latest push at 15:32:01 EDT aligns with build trigger ~15:31 EDT on 2026-04-13
2. **ECS health** — task RUNNING/HEALTHY, 1/1 running/desired, rollout COMPLETED
3. **Git diff `3faa75c`→`98c1500`** — exact diff confirms both changes (condition fix + `image/jpg` removal)
4. **Source code read at HEAD** — `FileUploadZone.razor` line 91 confirmed: `var valid = mimeOk || (string.IsNullOrEmpty(mime) && extOk);`
5. **Logic trace** — All three TC scenarios traced through the validation logic with concrete values

The logic fix is mechanically correct and surgically scoped. No regressions to other file types. Security gap from cycle 1 (`mimeOk || extOk`) is closed.

---

_Trust nothing. Verify everything. — Natasha Romanoff_
