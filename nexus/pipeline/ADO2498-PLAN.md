# BUILD Assignment: ADO#2498

## Task
**Integrate IWiClassifier into ArtifactGenerationService**
**ADO WI:** https://dev.azure.com/FortressAffinityGroup/4f20ca74-10f3-4707-b00b-04cd4e147909/_workitems/edit/2498

## MANDATORY: Read the spec first
Read the full spec before starting ANY code changes:
`/home/fredw/.openclaw/workspace/memory/projects/nexus-decomp-upgrade-spec-2026-04-27.md`
Focus on **§5 — Component Map** (ArtifactGenerationService modifications) and **§6 — Service Layer Changes** (ArtifactGenerationService integration points and the AdoCreationService predecessor resolution code sample).

## Repo
`/home/fredw/projects/fip/nexus/`
Working directory: `src/FortressNexus.Web/`

## Prerequisites already deployed
- `Services/IWiClassifier.cs` — interface + `WiTemplateType` enum (ADO#2490)
- `Services/WiClassifierService.cs` — full implementation (ADO#2490)
- `Models/Entities/WorkItemRecord.cs` — `WiTemplate`, `IsExternalDependency`, `ExternalOwner`, `TestedByTitles`, `PredecessorTitles` all present (ADO#2497)
- `Models/Entities/ArtifactSet.cs` — `ExternalDependencyCount` present (ADO#2497)

## What to build

### Step 1: Understand ArtifactGenerationService first
Before writing any code, read `Services/ArtifactGenerationService.cs` fully. Understand:
- How the AI response is parsed into WI candidates (what type/model is used)
- Where in the parse loop each WI candidate is turned into a `WorkItemRecord`
- Where the `ArtifactSet` is constructed and saved
- What the existing WI candidate model looks like (fields available for classification: title, description, acceptanceCriteria)

### Step 2: Constructor injection
Add `IWiClassifier` to `ArtifactGenerationService`'s constructor parameters. It's already registered as scoped in DI (ADO#2490).

### Step 3: Classification call post-parse
After each WI candidate is parsed and a `WorkItemRecord` is being built, call the classifier:

```csharp
var template = _wiClassifier.ClassifyStory(candidate);
record.WiTemplate = template;
record.IsExternalDependency = _wiClassifier.IsExternalDependency(candidate);
record.ExternalOwner = _wiClassifier.ExtractExternalOwner(candidate);
```

Apply to ALL WI types (Epic, Feature, Story, Task) — not just Stories. The classifier short-circuits gracefully for non-story types.

### Step 4: Test Case generation loop
After all standard WIs are built, iterate over User Story records and generate Test Case WIs for qualifying stories:

```csharp
var testCases = new List<WorkItemRecord>();
foreach (var story in workItemRecords.Where(w => w.WiType == "User Story"))
{
    if (_wiClassifier.ShouldGenerateTestCases(candidateFor(story)))
    {
        var acItems = ParseAcItems(story.AcceptanceCriteria); // split on "- [ ]" or numbered lines
        var tcTitles = new List<string>();
        foreach (var acItem in acItems)
        {
            var tc = new WorkItemRecord
            {
                WiType = "Test Case",
                WiTemplate = WiTemplateType.TestCase,
                ParentTitle = story.Title,
                Title = $"TC: {acItem.Trim()}",
                // ... other fields as appropriate
            };
            testCases.Add(tc);
            tcTitles.Add(tc.Title);
        }
        story.TestedByTitles = tcTitles;
    }
}
workItemRecords.AddRange(testCases);
```

Key points:
- Map each AC item to one Test Case WI
- `ParentTitle` on the Test Case = the parent User Story's exact title
- `WiTemplate = WiTemplateType.TestCase` on Test Case records
- `WiType = "Test Case"` (string, matching existing WiType pattern)
- Populate `story.TestedByTitles` with the list of generated TC titles
- Look at how the AI response model carries `acceptanceCriteria` — it may be a string or a list. Parse accordingly.

AC parsing: split on lines matching `^\s*-\s*\[.\]\s*` (checkbox items) OR `^\s*\d+\.\s+` (numbered items). Each matched line = one Test Case. If neither pattern matches, fall back to splitting on newlines and filtering non-empty lines.

You'll need a way to get back to the original candidate object for a given `WorkItemRecord` during this loop — either keep a parallel list of `(candidate, record)` pairs during the initial parse, or re-derive the candidate from the record fields. Choose whichever is cleaner given the existing code structure.

### Step 5: Set ExternalDependencyCount before save
Just before the `ArtifactSet` is saved:

```csharp
artifactSet.ExternalDependencyCount = workItemRecords.Count(w => w.IsExternalDependency);
```

### Step 6: Verify existing behavior unchanged
The existing flow for standard WIs must produce identical output to before this change. No fields removed, no existing assignments overwritten (only the new fields are being set). `WiTemplate` defaults to `Standard` if classification returns `Standard`, which is the correct no-op for existing WIs.

## ADO Updates (MANDATORY)
After implementing, add a comment to ADO WI #2498:
```
mcporter call devops.add_comment project="FAIT" id=2498 text="**[Tony Stark — BUILD cycle 1]**
Commit {hash}: {summary}. Build: SUCCEEDED."
```

## Build Report required
Create `/home/fredw/projects/fip/nexus/pipeline/ADO2498-BUILD-REPORT.md` with:
- Files modified (with full paths)
- Commit hash
- Build result (`dotnet build` output)
- CC invocation command used
- Self-review checklist: all 7 AC items verified
- Note on how the existing candidate↔record mapping was handled

## Notify Maria when done
When completely finished, run:
openclaw system event --text "ADO2498 BUILD COMPLETE: IWiClassifier integrated into ArtifactGenerationService" --mode now
