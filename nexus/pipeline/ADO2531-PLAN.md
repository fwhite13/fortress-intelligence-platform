# Build Plan: ADO#2531 — Validate upgraded ArtifactGenSystem prompt via standalone Bedrock call

**WI:** https://dev.azure.com/FortressAffinityGroup/4f20ca74-10f3-4707-b00b-04cd4e147909/_workitems/edit/2531  
**Spec:** `/home/fredw/.openclaw/workspace/memory/projects/nexus-decomp-upgrade-spec-2026-04-27.md` §11 + §12  
**FORGE KB spec (Bedrock input):** `/home/fredw/.openclaw/workspace/memory/projects/forge-kb-mcp-server-spec-2026-04-27.md`  
**ADO comment prefix:** `**[Tony Stark — BUILD cycle 1]**`

---

## This Is NOT a Code Change

Tony's task is purely a **standalone Bedrock validation call**. No source files are modified. No git commit. No deploy.

The output is:
1. `/home/fredw/projects/fip/nexus/pipeline/ADO2531-BEDROCK-OUTPUT.json` — raw JSON array from Claude
2. `/home/fredw/projects/fip/nexus/pipeline/ADO2531-BUILD-REPORT.md` — validation report

Fred and Jarvis review these two files before ADO#2529 (prompt wire-in to appsettings.Production.json) is dispatched.

---

## Step 1 — Read the Spec

Read both:
- `/home/fredw/.openclaw/workspace/memory/projects/nexus-decomp-upgrade-spec-2026-04-27.md` — **§11 (the prompt)** and **§12 (engineering notes)**
- `/home/fredw/.openclaw/workspace/memory/projects/forge-kb-mcp-server-spec-2026-04-27.md` — this is the Bedrock input

Understand:
- §11 contains the full `ArtifactGenSystem` replacement prompt (the large JSON string)
- §12 documents why the prompt is structured the way it is
- The FORGE KB spec is the complex multi-Epic spec the prompt is designed to decompose
- The §11 validation section contains a pass/fail checklist Tony uses to score the output

---

## Step 2 — Make the Bedrock Call

Make a direct AWS Bedrock call using the Boto3 Python SDK. Use `bedrock-runtime`, profile `fortress-tools-deployer`, region `us-east-1`.

**Model:** `us.anthropic.claude-sonnet-4-5` (inference profile ID)  
**Max tokens:** 16384  
**anthropic_beta:** `["output-128k-2025-02-19"]`

### System prompt
The system prompt is the **exact text content** of `ArtifactGenSystem` from §11 of the spec — the large JSON string value, unescaped as actual text. Extract it carefully: everything between the outer quotes of `"ArtifactGenSystem": "..."` is the prompt value. Unescape `\n` → newlines, `\"` → `"`, `\\n` → `\n`. The result should be well-structured markdown text.

### User message
The user message is the **full text content** of the FORGE KB MCP Server spec file: `/home/fredw/.openclaw/workspace/memory/projects/forge-kb-mcp-server-spec-2026-04-27.md`

Prefix the spec content with:
```
Please decompose the following specification into Azure DevOps work items per your instructions:

---

```

### Call pattern

```python
import boto3, json

session = boto3.Session(profile_name='fortress-tools-deployer', region_name='us-east-1')
client = session.client('bedrock-runtime')

response = client.invoke_model(
    modelId='us.anthropic.claude-sonnet-4-5',
    body=json.dumps({
        "anthropic_version": "bedrock-2023-05-31",
        "anthropic_beta": ["output-128k-2025-02-19"],
        "max_tokens": 16384,
        "system": SYSTEM_PROMPT,   # extracted from §11
        "messages": [
            {"role": "user", "content": USER_MESSAGE}  # spec text
        ]
    }),
    contentType='application/json',
    accept='application/json'
)

result = json.loads(response['body'].read())
raw_text = result['content'][0]['text']
```

### Parse and save

1. Attempt `json.loads(raw_text)` to verify it is valid JSON
2. Write the parsed/pretty-printed JSON array to `/home/fredw/projects/fip/nexus/pipeline/ADO2531-BEDROCK-OUTPUT.json`
3. If `json.loads` fails, write the raw text anyway and note the parse failure in the report

---

## Step 3 — Score Against the §11 Pass/Fail Checklist

The §11 validation section ("Validation — FORGE KB Spec Decomposition Test") contains a pass/fail checklist. Score each item:

```
- [ ] All 4 infrastructure WIs in the scaffold feature carry wi_template = 'infrastructure' and 🏗️ badges
- [ ] Rob's CF task has is_external_dependency = true, external_owner = 'Rob Nethery', tags include blocked-external and owner-rob-nethery
- [ ] IAM permissions WI has is_external_dependency = true, external_owner = 'AWS IAM'
- [ ] search_kb story generates ≥ 4 Test Case WIs covering scoping enforcement scenarios
- [ ] add_to_kb story generates ≥ 2 Test Case WIs covering write entitlement and metadata validation
- [ ] get_job_status story generates ≥ 1 Test Case WI covering the polling contract
- [ ] FAIT v2 DB stories carry predecessorTitles referencing the forge-kb tool group feature (cross-Epic link)
- [ ] ExternalDependencyCount = 3 on the generated ArtifactSet
- [ ] External Dependencies panel renders above the WI tree with all 3 entries
- [ ] FIRM migration task (migrate StartIngestionJob to add_to_kb) carries wi_template = 'migration' with Before/After/Validation sections
```

For each item: PASS, FAIL, or NOT_FOUND (if the item wasn't generated at all). Count totals.

Additional checks:
- All User Stories have a `specReference` field (not null, not empty)
- All Test Case WIs have a `rationale` field (not null, not empty)
- The JSON is directly parseable by `json.loads()` with no preamble or markdown fences
- Total WI count (Epic/Feature/Story/Task/TestCase breakdown)

---

## Step 4 — Write Build Report

Write `/home/fredw/projects/fip/nexus/pipeline/ADO2531-BUILD-REPORT.md`:

```markdown
# Build Report: ADO#2531 — ArtifactGenSystem Prompt Validation

**Date:** [date]
**Tony Stark — Standalone Bedrock Validation**

## Bedrock Call Details
- Model: us.anthropic.claude-sonnet-4-5
- Max tokens: 16384
- anthropic_beta: output-128k-2025-02-19
- System prompt: extracted from spec §11 (ArtifactGenSystem)
- User input: FORGE KB MCP Server spec (full text)
- Response tokens used: [from response metadata]
- Parse result: VALID JSON / PARSE FAILURE

## WI Count Summary
| Type | Count |
|------|-------|
| Epic | N |
| Feature | N |
| User Story | N |
| Task | N |
| Test Case | N |
| **Total** | **N** |

## §11 Checklist Results

| # | Criterion | Result | Notes |
|---|-----------|--------|-------|
| 1 | Infrastructure WIs have wi_template='infrastructure' | ✅ PASS / ❌ FAIL | ... |
| 2 | Rob's CF task: is_external_dependency=true, owner='Rob Nethery' | ✅ PASS / ❌ FAIL | ... |
| 3 | IAM WI: is_external_dependency=true, owner='AWS IAM' | ✅ PASS / ❌ FAIL | ... |
| 4 | search_kb: ≥4 Test Cases for scoping enforcement | ✅ PASS / ❌ FAIL | ... |
| 5 | add_to_kb: ≥2 Test Cases for write entitlement + metadata | ✅ PASS / ❌ FAIL | ... |
| 6 | get_job_status: ≥1 Test Case for polling contract | ✅ PASS / ❌ FAIL | ... |
| 7 | FAIT v2 DB stories have cross-Epic predecessorTitles | ✅ PASS / ❌ FAIL | ... |
| 8 | ExternalDependencyCount = 3 in ArtifactSet context | ✅ PASS / ❌ FAIL | ... |
| 9 | External Dependencies panel entries = 3 | ✅ PASS / ❌ FAIL | ... |
| 10 | FIRM migration WI: wi_template='migration', Before/After/Validation present | ✅ PASS / ❌ FAIL | ... |

**Checklist score: N/10**

## Additional Checks
- All User Stories have specReference: ✅ PASS / ❌ FAIL ([N] missing)
- All Test Cases have rationale: ✅ PASS / ❌ FAIL ([N] missing)
- JSON directly parseable: ✅ PASS / ❌ FAIL

## Overall Verdict
READY FOR WIRE-IN / NEEDS PROMPT REFINEMENT

## Issues Found
[List any FAIL items with specific WI titles, what was found vs expected]

## Output File
/home/fredw/projects/fip/nexus/pipeline/ADO2531-BEDROCK-OUTPUT.json
```

---

## Step 5 — Post ADO Comment

```
mcporter call devops.add_comment project="FAIT" id=2531 text="**[Tony Stark — BUILD cycle 1]**
Bedrock validation complete. Model: us.anthropic.claude-sonnet-4-5. Checklist: N/10.
Output: pipeline/ADO2531-BEDROCK-OUTPUT.json. Report: pipeline/ADO2531-BUILD-REPORT.md.
Verdict: READY FOR WIRE-IN / NEEDS PROMPT REFINEMENT."
```

---

## Deliverables

1. `/home/fredw/projects/fip/nexus/pipeline/ADO2531-BEDROCK-OUTPUT.json` — raw Bedrock JSON output
2. `/home/fredw/projects/fip/nexus/pipeline/ADO2531-BUILD-REPORT.md` — validation report with checklist scores

**Tony stops here.** No commit. No deploy. Fred and Jarvis review before next step.
