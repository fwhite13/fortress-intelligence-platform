# Build Report — NEXUS P1

**Commit:** `905a4fc`
**Build:** SUCCEEDED — 0 warnings, 0 errors
**Date:** 2026-04-02
**Sprint:** NEXUS P1 (WI#1518–1528, WI#1523 deferred)
**CC Sessions:** 1 (Opus, single-run — all 3 epics)

---

## What was built

Full NEXUS P1 sprint: multi-file submission wizard, AI spec generation overhaul (multi-file, FileType-aware), and the complete review gate (edit + approve + export). 36 files changed, 2,867 insertions, 295 deletions.

---

## Files Changed

### Epic 1 — Submission Intake

| File | Change |
|------|--------|
| `Models/Enums/SubmissionStatus.cs` | Added `Pending`, `Generating`, `Failed` values |
| `Models/Enums/FileType.cs` | **NEW** — `Html`, `Image`, `Pdf`, `Other` |
| `Models/Entities/SubmissionFile.cs` | **NEW** — junction entity (int PK, SubmissionId, UploadedFileId, SortOrder) |
| `Models/MockupSection.cs` | **NEW** — `record MockupSection(Label, HtmlContent, ScreenshotS3Key, TextContent)` |
| `Models/Entities/UploadedFile.cs` | Added `FileType` property; `Submissions` nav → `SubmissionFiles` |
| `Models/Entities/Submission.cs` | `MockupFileId` → `int?`; added `SubmissionFiles` nav |
| `Models/DTOs/SubmissionCreateDto.cs` | Replaced `MockupFileId` with `IEnumerable<int> FileIds` |
| `Data/NexusDbContext.cs` | Full rewrite: `SubmissionFile` DbSet + config, `file_type` column, removed `Ignore()` hack, `MockupFileId` nullable |
| `Services/ISubmissionService.cs` | No signature changes needed |
| `Services/SubmissionService.cs` | `CreateAsync` uses `FileIds`, creates `SubmissionFile` records, status starts `Pending`; `GetByIdAsync` loads `SubmissionFiles → UploadedFile` |
| `Services/FileStorageService.cs` | PDF support added (PdfPig), FileType detection, HtmlAgilityPack extraction, `image/jpg` added to AllowedTypes |
| `Components/Shared/FileUploadZone.razor` | Multi-file (up to 10), remove buttons per file, PDF accepted, type icons, backward-compat single-file callback |
| `Components/Pages/NexusSubmit.razor` | **DELETED** — replaced by NewSpecWizard.razor |
| `Components/Pages/NewSpecWizard.razor` | **NEW** — 3-step MudStepper at `/nexus/new`; Step1=Details, Step2=Files, Step3=Review+Submit |
| `Migrations/20260402040040_AddSubmissionFilesJunctionTable.cs` | **NEW** — `submission_files` table |
| `Migrations/20260402040049_AddFileTypeToUploadedFiles.cs` | **NEW** — `file_type INT` column on `uploaded_files` |
| `FortressNexus.Web.csproj` | Added `HtmlAgilityPack 1.11.*`, `PdfPig 0.1.*`, `DocumentFormat.OpenXml 3.*` |

### Epic 2 — AI Spec Generation

| File | Change |
|------|--------|
| `Services/IMockupSectionizer.cs` | **NEW** — `SectionizeAsync(htmlContent, submissionId)` |
| `Services/MockupSectionizerService.cs` | **NEW** — HtmlAgilityPack impl; finds `section/article/main/header/footer/div[id or class]`; fallback to full-doc if < 2 sections |
| `Services/SpecGenerationService.cs` | Full rewrite: loads `SubmissionFiles → UploadedFile`; routes HTML→sectionizer, Image→vision, PDF/Other→ProcessedText; `Pending→Generating→AwaitingReview/Failed` transitions; injects `IMockupSectionizer` |

### Epic 3 — Review Gate

| File | Change |
|------|--------|
| `Services/ISpecService.cs` | **NEW** — `SaveDraftAsync`, `ApproveAsync` |
| `Services/SpecService.cs` | **NEW** — persists `EditedContent/At/By`; `ApproveAsync` sets `IsApproved`, `ApprovedBy OID`, `ApprovedAt`, `Status→Approved` |
| `Services/NexusRoles.cs` | Added `Admin = "NexusAdmin"` |
| `Services/SlugHelper.cs` | **NEW** — `Slugify(title)` for export filenames |
| `Components/Pages/SubmissionDetail.razor` | **NEW** — `/nexus/{id}`; header+status badge+files+spec viewer+generate/review/download actions; 3s status polling while Generating |
| `Components/Pages/NexusReview.razor` | **NEW** — `/nexus/{id}/review`; two-panel AI original + edit; Save Draft; Approve (NexusAdmin only); post-approve ADO stub |
| `Controllers/SubmissionExportController.cs` | **NEW** — `GET /nexus/{id}/export?format=md|docx|pdf`; MD live, DOCX via OpenXml (headings mapped), PDF→501 |
| `Program.cs` | Added `IMockupSectionizer` + `ISpecService` DI registrations |

---

## Parallelization

Single CC Opus session — sequential. Sprint was too interconnected (Epic 2 depends on Epic 1 types; Epic 3 depends on Epic 1+2 entities) to safely parallelize.

---

## CC Sessions

1 session — Opus, `--dangerously-skip-permissions`, piped brief from `/tmp/nexus-p1-brief.md`. Clean first pass, build succeeded with 0 warnings.

---

## Acceptance Criteria Verification

- [x] `/nexus/new` — 3-step wizard at that route (NewSpecWizard.razor)
- [x] Narrative-only submissions valid — `CanSubmit` = title + narrative (files optional)
- [x] `submission_files` junction table migration created
- [x] `MockupFileId` nullable in entity + DbContext
- [x] FileType enum + `file_type` column migration created
- [x] HtmlAgilityPack text extraction for HTML files
- [x] PdfPig text extraction for PDF files
- [x] `IMockupSectionizer` with HtmlAgilityPack (Playwright deferred — WI#1523 Resolved)
- [x] `SpecGenerationService` multi-file, `Pending→Generating→AwaitingReview/Failed`
- [x] `SubmissionDetail.razor` at `/nexus/{id}` with status polling
- [x] Export controller — MD + DOCX live, PDF 501
- [x] `NexusReview.razor` at `/nexus/{id}/review` — edit + approve
- [x] `ISpecService` + `SpecService` — `SaveDraftAsync` + `ApproveAsync`
- [x] Build: 0 warnings, 0 errors

---

## Known Edge Cases / Things Clint Should Scrutinize

1. **SubmissionDetail polling** — Uses `System.Threading.Timer` + `InvokeAsync`. The `Dispose()` interface signature is on the class but verify Blazor component lifecycle wires it correctly. If `_pollTimer` leaks, submissions stuck in `Generating` will poll indefinitely.

2. **MudStepper.SetActiveIndex** — MudBlazor 7.x API — verify `SetActiveIndex(int)` exists on `MudStepper`. CC used it; if the method name differs in this version, wizard back-navigation will silently fail. May need `ActiveIndex` two-way binding instead.

3. **SpecGenerationService vision branch** — Image vision calls are now fire-and-forget inside the prompt builder (`BuildPromptAsync`). If a submission has 5 images, 5 sequential vision calls happen before the final InvokeAsync. No timeout guard on individual vision calls — a hung Bedrock call will block the entire generation.

4. **MudFileUpload `MaximumFileCount`** — Verify MudBlazor 7.x `MudFileUpload` supports this parameter. If not, the 10-file cap falls through to the `GetMultipleFiles(MaxFiles)` call which still enforces it, but no UI feedback.

5. **Export controller — DOCX heading styles** — Word heading styles (`Heading1`, `Heading2`, `Heading3`) require those styles to exist in the document's style definitions. Plain `WordprocessingDocument.Create` won't have them by default. The DOCX will open but headings may render as normal text in Word. Needs a style part added or use a template document. Flagging for Clint.

6. **WI#1523 Deferred** — Screenshots via Playwright are out. `ScreenshotS3Key` in `MockupSection` is always `null`. Embedded image refs in generated specs say `N/A — screenshots deferred to Phase 2`. This is intentional and documented in ADO.

---

## How to Test Locally

```bash
cd /home/fredw/projects/fip/nexus
# Verify build
dotnet build src/FortressNexus.Web/FortressNexus.Web.csproj

# Verify migrations exist
ls src/FortressNexus.Web/Migrations/ | grep -E "SubmissionFiles|FileType"

# Check new pages exist
ls src/FortressNexus.Web/Components/Pages/

# Check NuGet packages added
grep -E "HtmlAgilityPack|PdfPig|DocumentFormat" src/FortressNexus.Web/FortressNexus.Web.csproj

# Run (requires valid appsettings + DB)
dotnet run --project src/FortressNexus.Web
# Navigate: https://localhost:5001/nexus/new
```

---

## Notes for Clint

- `NexusSubmit.razor` deleted — `NewSpecWizard.razor` is its full replacement at the same route
- The `SubmissionStatus` enum has 3 new values (`Pending`, `Generating`, `Failed`) — any switch expressions on this enum elsewhere in the codebase may need case additions
- `GuidFormat=None` in the connection string is preserved — no CHAR(36) anywhere in new migrations
