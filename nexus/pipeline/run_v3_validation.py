import boto3, json, re, sys
from botocore.config import Config

# Increase read timeout for large output generation
bedrock_config = Config(read_timeout=300, retries={"max_attempts": 0})

# --- Load v3 prompt ---
with open("/home/fredw/.openclaw/workspace/memory/projects/nexus-prompt-v3-candidate.md") as f:
    candidate_text = f.read()

# Extract the code block content in the "Full prompt text" section
match = re.search(r"## Full prompt text.*?```\n(.*?)```", candidate_text, re.DOTALL)
if not match:
    print("ERROR: Could not find Full prompt text code block", file=sys.stderr)
    sys.exit(1)

raw_string = match.group(1).strip()
# Strip outer quotes if present
if raw_string.startswith('"') and raw_string.endswith('"'):
    raw_string = raw_string[1:-1]

# Unescape the JSON string encoding
# First handle \\n (literal backslash-n) by replacing with a placeholder
raw_string = raw_string.replace("\\\\n", "\x00LITERAL_NEWLINE\x00")
# Then handle \n (newline)
raw_string = raw_string.replace("\\n", "\n")
# Then handle \" (escaped quote)
raw_string = raw_string.replace('\\"', '"')
# Restore literal \n
raw_string = raw_string.replace("\x00LITERAL_NEWLINE\x00", "\\n")

system_prompt = raw_string

print(f"System prompt extracted: {len(system_prompt)} chars", flush=True)
print(f"Starts with: {system_prompt[:60]}...", flush=True)

# --- Load FORGE KB spec ---
with open("/home/fredw/.openclaw/workspace/memory/projects/forge-kb-mcp-server-spec-2026-04-27.md") as f:
    forge_spec = f.read()

user_message = "Please decompose the following specification into Azure DevOps work items per your instructions:\n\n---\n\n" + forge_spec

# --- Call Bedrock ---
session = boto3.Session(profile_name="fortress-tools-deployer", region_name="us-east-1")
client = session.client("bedrock-runtime", config=bedrock_config)

print("Calling Bedrock with v3 prompt...", flush=True)

model_id = "us.anthropic.claude-sonnet-4-20250514-v1:0"
fallback_model = "us.anthropic.claude-3-5-sonnet-20241022-v2:0"
used_model = model_id

try:
    response = client.invoke_model(
        modelId=model_id,
        body=json.dumps({
            "anthropic_version": "bedrock-2023-05-31",
            "anthropic_beta": ["output-128k-2025-02-19"],
            "max_tokens": 32768,
            "system": system_prompt,
            "messages": [{"role": "user", "content": user_message}]
        }),
        contentType="application/json",
        accept="application/json"
    )
except Exception as e:
    print(f"Primary model failed: {e}", flush=True)
    print(f"Falling back to {fallback_model}...", flush=True)
    used_model = fallback_model
    response = client.invoke_model(
        modelId=fallback_model,
        body=json.dumps({
            "anthropic_version": "bedrock-2023-05-31",
            "anthropic_beta": ["output-128k-2025-02-19"],
            "max_tokens": 32768,
            "system": system_prompt,
            "messages": [{"role": "user", "content": user_message}]
        }),
        contentType="application/json",
        accept="application/json"
    )

result = json.loads(response["body"].read())
raw_text = result["content"][0]["text"].strip()
usage = result.get("usage", {})
print(f"Model used: {used_model}", flush=True)
print(f"Response received. Input tokens: {usage.get('input_tokens')}, Output tokens: {usage.get('output_tokens')}", flush=True)

# Write raw response for debugging
with open("/home/fredw/projects/fip/nexus/pipeline/ADO2554-BEDROCK-RAW.txt", "w") as f:
    f.write(raw_text)

# Strip markdown fences if present
stripped = raw_text
if stripped.startswith("```"):
    stripped = re.sub(r"^```[a-z]*\n?", "", stripped)
    stripped = re.sub(r"```$", "", stripped).strip()

# Parse
try:
    wi_array = json.loads(stripped)
    parse_ok = True
    print(f"JSON parse: OK — {len(wi_array)} items", flush=True)
except Exception as e:
    # Try parsing raw_text directly
    try:
        wi_array = json.loads(raw_text)
        parse_ok = True
        print(f"JSON parse (raw): OK — {len(wi_array)} items", flush=True)
    except Exception as e2:
        wi_array = []
        parse_ok = False
        print(f"JSON parse FAILED: {e2}", flush=True)

# Write output
output_path = "/home/fredw/projects/fip/nexus/pipeline/ADO2554-BEDROCK-OUTPUT.json"
with open(output_path, "w") as f:
    if parse_ok:
        json.dump(wi_array, f, indent=2)
    else:
        f.write(raw_text)

print(f"\nOutput written to {output_path}", flush=True)

# --- Summary stats ---
print("\n=== SUMMARY STATS ===", flush=True)
type_counts = {}
for wi in wi_array:
    t = wi.get("type", "Unknown")
    type_counts[t] = type_counts.get(t, 0) + 1
print("WI counts:", type_counts, flush=True)
print(f"Total WIs: {len(wi_array)}", flush=True)

# External deps
ext_deps = [wi for wi in wi_array if wi.get("isExternalDependency") == True]
print(f"\nExternal dependencies ({len(ext_deps)}):", flush=True)
for ed in ext_deps:
    print(f"  - {ed.get('title', '?')} | owner={ed.get('externalOwner', '?')} | tags={ed.get('tags', '?')}", flush=True)

# Epics
epics = [wi for wi in wi_array if wi.get("type") == "Epic"]
print(f"\nEpics ({len(epics)}):", flush=True)
for ep in epics:
    print(f"  - {ep.get('title', '?')}", flush=True)

# Features
features = [wi for wi in wi_array if wi.get("type") == "Feature"]
print(f"\nFeatures ({len(features)}):", flush=True)
for ft in features:
    print(f"  - {ft.get('title', '?')} (parent: {ft.get('parentTitle', '?')})", flush=True)

# TCs and their parents
tcs = [wi for wi in wi_array if wi.get("type") == "Test Case"]
print(f"\nTest Cases ({len(tcs)}):", flush=True)
tc_parents = {}
for tc in tcs:
    p = tc.get("parentTitle", "?")
    tc_parents[p] = tc_parents.get(p, 0) + 1
    print(f"  - {tc.get('title', '?')} (parent: {p})", flush=True)
print("\nTC parent summary:", flush=True)
for parent, count in tc_parents.items():
    print(f"  {count} TCs under: {parent}", flush=True)

# Tasks
tasks = [wi for wi in wi_array if wi.get("type") == "Task"]
print(f"\nTasks ({len(tasks)}):", flush=True)

# Stories
stories = [wi for wi in wi_array if wi.get("type") == "User Story"]
print(f"\nUser Stories ({len(stories)}):", flush=True)
for s in stories:
    child_tasks = [t for t in tasks if t.get("parentTitle") == s.get("title")]
    child_tcs = [t for t in tcs if t.get("parentTitle") == s.get("title")]
    print(f"  - {s.get('title', '?')} | wiTemplate={s.get('wiTemplate', '?')} | tasks={len(child_tasks)} | TCs={len(child_tcs)} | extDep={s.get('isExternalDependency', False)} | specRef={s.get('specReference', 'NULL')}", flush=True)
    if s.get("predecessorTitles"):
        print(f"    predecessorTitles: {s.get('predecessorTitles')}", flush=True)

# Infrastructure WIs
infra_wis = [wi for wi in wi_array if wi.get("wiTemplate") == "infrastructure"]
print(f"\nInfrastructure WIs ({len(infra_wis)}):", flush=True)
for iw in infra_wis:
    print(f"  - {iw.get('title', '?')} | type={iw.get('type', '?')}", flush=True)

# Migration WIs
migration_wis = [wi for wi in wi_array if wi.get("wiTemplate") == "migration"]
print(f"\nMigration WIs ({len(migration_wis)}):", flush=True)
for mw in migration_wis:
    desc = mw.get("description", "")
    has_before = "Before" in desc or "**Before:**" in desc
    has_after = "After" in desc or "**After:**" in desc
    has_validation = "Validation" in desc or "**Validation:**" in desc
    print(f"  - {mw.get('title', '?')} | type={mw.get('type', '?')} | Before={has_before} | After={has_after} | Validation={has_validation}", flush=True)

# specReference check
stories_missing_specref = [s for s in stories if not s.get("specReference")]
print(f"\nStories missing specReference: {len(stories_missing_specref)}", flush=True)
for s in stories_missing_specref:
    print(f"  - {s.get('title', '?')}", flush=True)

# rationale check
tcs_missing_rationale = [tc for tc in tcs if not tc.get("rationale")]
print(f"\nTest Cases missing rationale: {len(tcs_missing_rationale)}", flush=True)

# Stories with <2 tasks
stories_few_tasks = []
for s in stories:
    child_tasks = [t for t in tasks if t.get("parentTitle") == s.get("title")]
    if len(child_tasks) < 2:
        stories_few_tasks.append((s.get("title", "?"), len(child_tasks)))
print(f"\nStories with <2 tasks: {len(stories_few_tasks)}", flush=True)
for title, count in stories_few_tasks:
    print(f"  - {title} ({count} tasks)", flush=True)

print("\n=== DONE ===", flush=True)
