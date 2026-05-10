# Build Report — ADO#3202: WordDocumentGenerator (OpenXml SDK)

## What was built
Replaced `StubDocumentGeneratorService` with a real `WordDocumentGenerator` that produces properly-structured Word documents using the OpenXml SDK. Updated the `IDocumentGeneratorService` interface to use a `DocumentGenerationRequest` record pattern. Ported table keep-together rules from proposal-generator reference project.

## CC Invocation
```bash
cd /home/fredw/projects/fip/fait && cat /tmp/cc-brief-3202.md | claude --model opus -p --dangerously-skip-permissions --output-format text
```
**Model:** CC Opus (as specified — complex multi-file OpenXml implementation)

## Commit SHA
`36056b93`

## Files Changed
- `src/FortressAI.Web/Services/IDocumentGeneratorService.cs` — Added `DocumentGenerationRequest` record; updated interface signature from `(string type, string title, List<DocumentSection> sections)` to `(DocumentGenerationRequest request)`
- `src/FortressAI.Web/Services/StubDocumentGeneratorService.cs` — Updated to match new interface (still compiles, still functional as fallback)
- `src/FortressAI.Web/Services/WorkspaceController.cs` — `GenerateDocument` action now constructs `DocumentGenerationRequest` before calling `GenerateAsync`
- `src/FortressAI.Web/Services/WordDocumentGenerator.cs` — **New file** (261 lines): full OpenXml implementation
- `src/FortressAI.Web/Program.cs` — Registration changed from `AddScoped<StubDocumentGeneratorService>` → `AddSingleton<WordDocumentGenerator>`

## Parallelization Used
No — all tasks sequential (each file depends on updated interface from prior task).

## CC Sessions Run
1 CC Opus run. No notable deviations from spec — CC handled the OpenXml v3.x API correctly (used `AppendChild` instead of object initializer for Style children, which is the correct v3.x pattern).

## Acceptance Criteria Verification
- [x] `IDocumentGeneratorService` interface updated to `DocumentGenerationRequest` pattern
- [x] `StubDocumentGeneratorService` updated to match new interface (compiles)
- [x] `WorkspaceController.GenerateDocument` updated to use `DocumentGenerationRequest`
- [x] `WordDocumentGenerator` created
- [x] Cover page: Title style + timestamp + page break
- [x] TOC field: `TOC \h \z \u` + placeholder text
- [x] `w:updateFields` in settings.xml via `AddUpdateFieldsSetting`
- [x] Section headings: Heading1 style
- [x] Content: `\n` → paragraph split, `**text**` → bold, `*text*` → italic via `ParseInlineContent`
- [x] Footer: PAGE field, right-aligned
- [x] Table helper: `ApplyTableKeepTogether` exists (port of proposal-generator §3.3)
- [x] 4 styles embedded: Normal, Heading1, Heading2, Title
- [x] `WordDocumentGenerator` registered Singleton in Program.cs
- [x] Build: **0 errors, 0 warnings**

## Known Edge Cases / Things Clint Should Scrutinize
1. **Footer wiring order**: `AddPageNumberFooter` is called after `mainPart.Document.Save()`. This means the `SectionProperties` in the body already exists when we wire the footer ref. The method uses `GetFirstChild<SectionProperties>()` to find it — this is correct but verify the footer actually renders in a real Word open.
2. **`ApplyTableKeepTogether` is included but not wired** — it's a private static method with no callers yet (v1 has no table content in sections). When tables are added, callers need to invoke this before appending any `Table` to the body.
3. **Italic regex**: The regex `\*(.+?)\*` will match `*italic*` but also the inner `*` of `**bold**` if the bold pattern fails for any reason. Since bold is matched first (left-to-right) in the alternation, this should be fine in practice, but worth a quick test with mixed content.
4. **`SpaceProcessingModeValues.Preserve`** is set on all inline `Text` nodes to preserve leading/trailing spaces in parsed runs — correct behavior.

## How to Test Locally
```bash
# Build
cd /home/fredw/projects/fip/fait && dotnet build src/FortressAI.Web/FortressAI.Web.csproj

# Integration test via API (requires running service + internal token):
curl -X POST http://localhost:5000/api/workspace/generate-document \
  -H "Content-Type: application/json" \
  -H "X-Internal-Token: <INTERNAL_API_TOKEN>" \
  -d '{
    "type": "word",
    "title": "Test Document",
    "sections": [
      {"heading": "Introduction", "content": "This is **bold** and *italic* text.\nSecond paragraph."},
      {"heading": "Summary", "content": "Final section content."}
    ]
  }' --output test-output.docx

# Open test-output.docx in Word to verify:
# - Title page with "Test Document" in Title style
# - TOC field on page 2
# - Sections with Heading1 style
# - Bold/italic rendering
# - Page number in footer
```
