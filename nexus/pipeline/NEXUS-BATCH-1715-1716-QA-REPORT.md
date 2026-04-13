# QA Report: NEXUS Batch — ADOs #1715 + #1716

### Verdict: ✅ PASS

### Environment
- **Method:** Code-verification against repo HEAD (`/home/fredw/projects/fip/nexus/src`)
- **Files Inspected:**
  - `Components/Shared/FileUploadZone.razor`
  - `Components/Shared/FileUploadZone.razor.css`
  - `Components/Nexus/DiscoveryStep.razor`
  - `Components/Pages/NewSpecWizard.razor`
- **Test Start:** 2026-04-13 15:40:56 EDT
- **Test Duration:** ~3 min

---

## ADO #1715 — FileUploadZone Drag-and-Drop Fix

### TC1: MudFileUpload `Hidden="false"` + CSS z-index rules ✅ PASS

**`Hidden="false"` on MudFileUpload:**
```razor
<MudFileUpload T="IReadOnlyList<IBrowserFile>"
               ...
               Hidden="false"
               InputClass="file-upload-input-overlay">
```
✅ Confirmed. `Hidden="false"` is present — the MudBlazor drag-drop overlay input is rendered and participates in layout/pointer events.

**CSS z-index on file list items (`FileUploadZone.razor.css`):**
```css
.file-upload-zone ::deep .file-upload-input-overlay {
    position: absolute;
    top: 0; left: 0;
    width: 100%; height: 100%;
    opacity: 0;
    cursor: pointer;
    z-index: 1;        /* drop zone overlay */
}

.file-upload-zone ::deep .file-upload-list-item {
    position: relative;
    z-index: 2;        /* list items sit above overlay so remove buttons are clickable */
    display: flex;
    align-items: center;
    gap: 8px;
    width: 100%;
}
```
✅ Confirmed. `::deep` scoped CSS rules present. Overlay at `z-index: 1`, list items at `z-index: 2`. List items are elevated so their remove (×) buttons remain clickable; drag-drop still captures on empty areas of the zone.

**Verdict — TC1: ✅ PASS**

---

## ADO #1716 — Discovery Polling Timeout + Pending Status Guard

### TC2: Polling timeout increased from 15s → 60s ✅ PASS

Located in `Components/Pages/NewSpecWizard.razor`:

```csharp
// Poll until QuestionsReady or timeout (60s — Bedrock inference can take 20–40s)
var deadline = DateTime.UtcNow.AddSeconds(60);
while (DateTime.UtcNow < deadline)
{
    await Task.Delay(1000);
    ...
}
```

Two polling sites both use `AddSeconds(60)` — initial discovery initiation (line 452) and session resume/reload (line 613). No 15s value found anywhere in the polling paths.

**Verdict — TC2: ✅ PASS**

---

### TC3: Error banner guards against `DiscoverySessionStatus.Pending` ✅ PASS

Located in `Components/Nexus/DiscoveryStep.razor`:

```razor
else if (Session == null || (Session.Status != DiscoverySessionStatus.Pending && !Session.Questions.Any()))
{
    <MudAlert Severity="Severity.Info" ...>
        Couldn't generate questions — continuing to spec generation.
    </MudAlert>
    ...
}
```

✅ Confirmed. Exact guard specified in the WI is present. When `Session.Status == DiscoverySessionStatus.Pending` (inference in-flight), the condition short-circuits and the "couldn't generate questions" banner is **suppressed**. Users see the loading skeleton instead of a false-negative error alert.

**Verdict — TC3: ✅ PASS**

---

## Test Summary

| TC | Description | Verdict |
|----|-------------|---------|
| TC1 | `Hidden="false"` on MudFileUpload + `::deep` z-index CSS rules (z-index 2 on list items) | ✅ PASS |
| TC2 | Polling deadline increased to 60s (was 15s) | ✅ PASS |
| TC3 | Error banner suppressed when `DiscoverySessionStatus.Pending` | ✅ PASS |

**Overall: ✅ PASS — all 3 test cases confirmed at code level. Both WIs correctly implemented.**

---

_— Natasha Romanoff (Black Widow / qa-analyst) | 2026-04-13 15:40 EDT_
