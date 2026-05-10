# QA Report: ADO#3203 — 5.4-A: Artifact Preview Panel

**Agent:** Black Widow (Natasha Romanoff)  
**Date:** 2026-05-10  
**Task Def Verified:** `fred-dev:169`  
**Commit:** `af48e1ee`

---

## Verdict: ✅ QA PASS

---

## Tests Run

- Smoke: 2 — 2 passed
- Code-level: 9 checks — 9 passed
- Regression: 1 (ScheduledTaskBackgroundService) — passed

---

## Service Health

| Check | Result |
|-------|--------|
| ECS service status | ✅ ACTIVE |
| Task definition | ✅ `fred-dev:169` |
| Desired / Running | ✅ 1 / 1 |
| Startup exceptions / DI errors | ✅ None |
| `ScheduledTaskBackgroundService starting` | ✅ Confirmed in logs |
| Application listening | ✅ `http://[::]:8080` |

**CloudWatch log stream:** `ecs/fred/e0e2599a93b84a9d85840558f33e36ed`

Startup sequence clean. The `fail:` lines in logs are all idempotent "column already exists" migration guards — pre-existing pattern, not new errors.

---

## Code-Level Verification

### ChatLayoutState.cs ✅

- `ArtifactRef` record: ✅ `public record ArtifactRef(string S3Key, string Filename, string MimeType)`
- `OpenArtifactPreview(ArtifactRef artifact)`: ✅ Present, sets `ArtifactPanelOpen = true`, assigns `CurrentArtifact`, invokes `OnChange`
- `CloseArtifactPreview()`: ✅ Present, sets `ArtifactPanelOpen = false`, nulls `CurrentArtifact`, invokes `OnChange`
- `OnChange` event: ✅ `public event Action? OnChange`

### Program.cs ✅

- `ChatLayoutState` registered as **Scoped**: ✅ Line 113: `builder.Services.AddScoped<ChatLayoutState>();`
- `ScheduledTaskBackgroundService` registered: ✅ Line 109: `builder.Services.AddHostedService<ScheduledTaskBackgroundService>()`

### ArtifactPreviewPanel.razor ✅

- **iframe present**: ✅ `<iframe src="@_presignedUrl" style="width: 100%; height: 100%; border: none;" ...>`
- **Loading spinner**: ✅ `<MudProgressCircular Indeterminate="true" />` shown while `_loading == true`
- **Error state**: ✅ `MudIcon ErrorOutline` + error text + Retry button shown on `_error != null`
- **Close button**: ✅ `MudIconButton` with `OnClick="Close"` → calls `LayoutState.CloseArtifactPreview()`
- **`_presignedUrl` = Office Online URL (not raw S3 key)**: ✅
  ```csharp
  var url = await WorkspaceFileSvc.GetPresignedDownloadUrlAsync(artifact.S3Key, expiryMinutes: 30);
  var encoded = Uri.EscapeDataString(url);
  _presignedUrl = $"https://view.officeapps.live.com/op/embed.aspx?src={encoded}";
  ```
- **`Uri.EscapeDataString` used**: ✅ Confirmed above
- **`expiryMinutes: 30`**: ✅ Confirmed in `GetPresignedDownloadUrlAsync` call

### ArtifactCard.razor ✅

- **Preview enabled for docx MIME only**: ✅
  ```csharp
  bool previewSupported = Artifact.MimeType == "application/vnd.openxmlformats-officedocument.wordprocessingml.document";
  ```
- **Disabled tooltip text**: ✅ `"Preview not yet supported for this file type"` (exact match)
- **Tooltip disabled when preview supported**: ✅ `Disabled="@previewSupported"` on `MudTooltip`
- **Button disabled when not supported**: ✅ `Disabled="@(!previewSupported)"`

### ChatView.razor ✅

- **Flex row dual-pane wrapper**: ✅
  ```html
  <div style="display: flex; height: 100%; overflow: hidden; transition: all 0.2s ease;">
  ```
- **`min-width: 0` on chat div**: ✅
  ```html
  <div style="flex: 1; min-width: 0; overflow: hidden; display: flex; flex-direction: column; ...">
  ```
- **Panel conditional render**: ✅
  ```razor
  @if (LayoutState.ArtifactPanelOpen)
  {
      <div style="width: 40%; min-width: 320px; max-width: 600px; ...">
          <ArtifactPreviewPanel />
      </div>
  }
  ```
- **`ChatLayoutState` wired**: ✅ `@inject ChatLayoutState LayoutState` + `LayoutState.OnChange += _layoutStateChangeHandler` in `OnInitializedAsync`

---

## Pre-existing Blockers (Documented)

**Browser E2E:** Not attempted. This is a pre-existing blocker: Cloudflare + `TestAuth__Secret` requirement blocks headless browser access to the `fred-dev` environment. The spec acknowledges this. Code-level and service-level verification has been completed in full.

---

## Issues Found

None.

---

## Notes

All acceptance criteria verified. Implementation is correct and consistent — the Office Online iframe URL is properly constructed (`Uri.EscapeDataString` on the presigned URL, `expiryMinutes: 30`, Office Online embed endpoint), the scoped service registration is clean, and the dual-pane layout uses correct flex CSS (`min-width: 0` on the chat column prevents content overflow). The panel width (`width: 40%; min-width: 320px; max-width: 600px`) is reasonable for document preview.

---

## Test Duration

~4 minutes

---

_Trust nothing. Verify everything. — Black Widow_
