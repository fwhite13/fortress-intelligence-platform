# QA Report: ADO#3204 — 5.5-A: Workspace Page

**Verdict: ✅ QA PASS**

**Date:** 2026-05-10  
**Tester:** Black Widow (Natasha Romanoff) — QA Analyst  
**Task Def:** `fred-dev:170`  
**Commit:** `5c761874`

---

## Tests Run

- **Service Health:** 2 — 2 passed / 0 failed
- **CloudWatch Logs:** 1 — 1 passed / 0 failed
- **Code-Level Verification:** 11 — 11 passed / 0 failed
- **Browser E2E:** 0 — blocked (pre-existing, see notes)

---

## Service Health

### ECS Service
| Check | Result |
|-------|--------|
| Status | ACTIVE ✅ |
| Task Definition | `fred-dev:170` ✅ |
| Desired count | 1 ✅ |
| Running count | 1 ✅ |

### CloudWatch Startup
| Check | Result |
|-------|--------|
| `ScheduledTaskBackgroundService starting` | ✅ Present — `poll interval: 60s` |
| Application started | ✅ `Now listening on: http://[::]:8080` |
| DB init `fail:` entries | ✅ Pre-existing EF DataProtectionKeys + idempotent schema migrations only — known-good pattern |
| Unexpected errors | ✅ None |

---

## Code-Level Verification

### WorkspaceFiles.razor — `src/FortressAI.Web/Components/Pages/WorkspaceFiles.razor`

| Check | Expected | Result |
|-------|----------|--------|
| File exists | ✅ | ✅ |
| Route declaration | `@page "/workspace"` | ✅ Present |
| Default tab | `_activeTab = 1` (Generated) | ✅ `private int _activeTab = 1;` |
| Files tab placeholder | Stub text | ✅ `"File manager coming soon."` |
| Generated tab — load method | `GetUserArtifactsAsync` | ✅ `WorkspaceFileSvc.GetUserArtifactsAsync(Session.UserId)` |
| Generated tab — group by conversation | Grouped display | ✅ Groups by `ConversationId`, ordered by latest `CreatedAt` |
| Title fallback — cycle 2 fix | `string.IsNullOrWhiteSpace` (not `??`) | ✅ `string.IsNullOrWhiteSpace(conv?.Title)` confirmed |
| Preview button | `.docx` only enabled | ✅ `bool previewSupported = artifact.MimeType == "application/vnd.openxmlformats-officedocument.wordprocessingml.document"` |
| Preview navigation | `/chat/{id}?previewArtifact={id}` | ✅ `NavigationManager.NavigateTo($"/chat/{artifact.ConversationId}?previewArtifact={artifact.Id}")` |
| Download | `GetPresignedDownloadUrlAsync(s3Key, 30)` → `window.open` | ✅ `GetPresignedDownloadUrlAsync(artifact.S3Key, expiryMinutes: 30)` + `JSRuntime.InvokeVoidAsync("open", url, "_blank")` |

### MainLayout.razor — Nav Entry

| Check | Expected | Result |
|-------|----------|--------|
| Workspace nav entry exists | Between Memory and Settings | ✅ Line 54: Memory, Line 55: Workspace (`/workspace`), Line 56: Settings — correct order |

### ChatView.razor — previewArtifact Query Param

| Check | Expected | Result |
|-------|----------|--------|
| `previewArtifact` param handling present | ✅ | ✅ Lines 512–527 |
| Runs after artifacts load | After `_conversationArtifacts` loaded | ✅ Guarded by `if (_conversationArtifacts.Any())` |
| Parses artifact GUID | Safe `Guid.TryParse` | ✅ |
| Opens preview panel | `LayoutState.OpenArtifactPreview(new ArtifactRef(...))` | ✅ |

---

## Browser E2E

⚠️ **BLOCKED — Pre-existing blocker, not a regression from this change.**

Browser E2E testing of `https://fait.dev.fortressam.ai` is blocked by:
1. **Cloudflare** — bot challenge on unauthenticated traffic
2. **TestAuth__Secret** — test-session bypass requires shared secret not available to headless browser profile in this environment

This is a known pre-existing condition. No browser testing was possible for this or any previous FAIT deployment cycle. Visual and functional verification requires manual sign-off by Fred.

---

## Git Commit Verification

```
5c761874 fix(ADO#3204): use IsNullOrWhiteSpace for conversation title fallback in WorkspaceFiles
192e40cb feat(fait#3204): workspace page, nav entry, previewArtifact query param
```

Both commits present and match the expected change set. Cycle 2 fix (`IsNullOrWhiteSpace`) is at the tip.

---

## Key Findings

1. All code-level acceptance criteria pass without exception.
2. `_activeTab = 1` sets Generated as the default tab — users land on the useful tab, not the stub.
3. Title fallback uses `string.IsNullOrWhiteSpace` (not `??`) — cycle 2 fix is confirmed in the deployed code.
4. Preview is correctly gated to `.docx` MIME type only.
5. Download uses a 30-minute presigned URL opened in a new tab — correct.
6. Workspace nav entry sits exactly between Memory and Settings in `MainLayout.razor`.
7. `previewArtifact` deep-link logic in `ChatView.razor` runs only after `_conversationArtifacts` is populated — no race condition.
8. ECS task def `fred-dev:170` running 1/1. `ScheduledTaskBackgroundService` confirmed starting.

---

## Issues Found

None.

---

## Test Duration

~4 minutes

---

## Recommendations

1. **Manual visual sign-off requested** — Fred should navigate to `/workspace` post-login and confirm: (a) Generated tab is active by default, (b) any existing artifacts appear grouped by conversation, (c) Preview button is disabled for non-.docx files, (d) Download works.
2. Browser E2E gate remains blocked by Cloudflare + TestAuth__Secret — this should be tracked as a separate improvement item if automated UI verification of FAIT is desired.

---

_Trust nothing. Verify everything. — Black Widow_
