import boto3, json, re, sys

# Load v5 prompt
with open("/home/fredw/.openclaw/workspace/memory/projects/nexus-prompt-v5-candidate.md") as f:
    candidate_text = f.read()

match = re.search(r"## Full prompt text.*?```\n(.*?)```", candidate_text, re.DOTALL)
if not match:
    print("ERROR: Could not find Full prompt text code block", file=sys.stderr)
    sys.exit(1)

raw_string = match.group(1).strip()
if raw_string.startswith('"') and raw_string.endswith('"'):
    raw_string = raw_string[1:-1]
system_prompt = raw_string.replace("\\\\n", "\x00LITERAL_NEWLINE\x00").replace("\\n", "\n").replace('\\"', '"').replace("\x00LITERAL_NEWLINE\x00", "\\n")
print(f"System prompt extracted: {len(system_prompt)} chars", flush=True)

# Load FORGE KB spec
with open("/home/fredw/.openclaw/workspace/memory/projects/forge-kb-mcp-server-spec-2026-04-27.md") as f:
    forge_spec = f.read()

user_message = "Please decompose the following specification into Azure DevOps work items per your instructions:\n\n---\n\n" + forge_spec

# Call Bedrock — extended read timeout for large output
from botocore.config import Config
bedrock_config = Config(read_timeout=300, retries={"max_attempts": 0})
session = boto3.Session(profile_name="fortress-tools-deployer", region_name="us-east-1")
client = session.client("bedrock-runtime", config=bedrock_config)

models = [
    ("us.anthropic.claude-sonnet-4-20250514-v1:0", 32768, ["output-128k-2025-02-19"]),
    ("us.anthropic.claude-sonnet-4-6-20250620-v1:0", 32768, ["output-128k-2025-02-19"]),
    ("anthropic.claude-3-5-sonnet-20241022-v2:0", 16384, None),
    ("us.anthropic.claude-3-5-sonnet-20241022-v2:0", 16384, None),
]

response = None
model_used = None
for model_id, max_tok, beta in models:
    print(f"Trying model: {model_id}...", flush=True)
    body = {
        "anthropic_version": "bedrock-2023-05-31",
        "max_tokens": max_tok,
        "system": system_prompt,
        "messages": [{"role": "user", "content": user_message}]
    }
    if beta:
        body["anthropic_beta"] = beta
    try:
        response = client.invoke_model(
            modelId=model_id,
            body=json.dumps(body),
            contentType="application/json",
            accept="application/json"
        )
        model_used = model_id
        print(f"Success with {model_id}", flush=True)
        break
    except Exception as e:
        print(f"  Failed: {e}", flush=True)

if response is None:
    print("ERROR: All models failed", file=sys.stderr)
    sys.exit(1)

result = json.loads(response["body"].read())
raw_text = result["content"][0]["text"].strip()
usage = result.get("usage", {})
print(f"Model used: {model_used}", flush=True)
print(f"Tokens — Input: {usage.get('input_tokens')}, Output: {usage.get('output_tokens')}", flush=True)

# Strip markdown fences if present
if raw_text.startswith("```"):
    raw_text = re.sub(r"^```[a-z]*\n?", "", raw_text)
    raw_text = re.sub(r"```$", "", raw_text).strip()

# Parse
try:
    wi_array = json.loads(raw_text)
    parse_ok = True
    print(f"JSON parse: OK — {len(wi_array)} items", flush=True)
except Exception as e:
    wi_array = []
    parse_ok = False
    print(f"JSON parse FAILED: {e}", flush=True)

# Write output
output_path = "/home/fredw/projects/fip/nexus/pipeline/ADO2559-BEDROCK-OUTPUT.json"
with open(output_path, "w") as f:
    if parse_ok:
        json.dump(wi_array, f, indent=2)
    else:
        f.write(raw_text)
print(f"Output written to {output_path}", flush=True)

# --- Summary stats ---
type_counts = {}
for wi in wi_array:
    t = wi.get("type", "Unknown")
    type_counts[t] = type_counts.get(t, 0) + 1
print("\nWI type counts:", type_counts, flush=True)

epics = [wi for wi in wi_array if wi.get("type") == "Epic"]
print(f"\nEpics ({len(epics)}):", flush=True)
for ep in epics:
    print(f"  - {ep.get('title')}", flush=True)

ext_deps = [wi for wi in wi_array if wi.get("isExternalDependency") == True]
print(f"\nExternal deps ({len(ext_deps)}):", flush=True)
for ed in ext_deps:
    print(f"  title: {ed.get('title')}", flush=True)
    print(f"  owner: {ed.get('externalOwner')}", flush=True)
    print(f"  tags:  {ed.get('tags')}", flush=True)

tcs = [wi for wi in wi_array if wi.get("type") == "Test Case"]
print(f"\nTest Cases ({len(tcs)}):", flush=True)
tc_parents = {}
for tc in tcs:
    p = tc.get("parentTitle", "?")
    tc_parents[p] = tc_parents.get(p, 0) + 1
for parent, count in sorted(tc_parents.items(), key=lambda x: -x[1]):
    print(f"  {count}x under: {parent}", flush=True)

migration_wis = [wi for wi in wi_array if wi.get("wiTemplate") == "migration"]
print(f"\nMigration WIs ({len(migration_wis)}):", flush=True)
for mw in migration_wis:
    desc = mw.get("description", "")
    has_before = "**Before:**" in desc or "## Before" in desc
    has_after = "**After:**" in desc or "## After" in desc
    has_validation = "**Validation:**" in desc or "## Validation" in desc
    print(f"  - {mw.get('title')} | Before:{has_before} After:{has_after} Validation:{has_validation}", flush=True)

# Check each standard story AC count vs TC count
stories = [wi for wi in wi_array if wi.get("type") == "User Story" and wi.get("wiTemplate") in ("standard", None)]
print(f"\nStandard User Stories AC count check ({len(stories)} stories):", flush=True)
for story in stories:
    title = story.get("title", "?")
    ac = story.get("acceptanceCriteria") or ""
    # Count AC items — handle both list and string formats
    if isinstance(ac, list):
        ac_count = len(ac)
    else:
        ac_items = [l for l in ac.split("\n") if re.match(r"\s*-\s*\[.\]|\s*\d+\.", l.strip())]
        ac_count = len(ac_items) if ac_items else ac.count("\n- ") + (1 if ac.startswith("- ") else 0)
    tc_count = tc_parents.get(title, 0)
    flag = " *** RULE B VIOLATION ***" if ac_count >= 4 and tc_count == 0 else ""
    if ac_count >= 3 or tc_count > 0:
        print(f"  ACs={ac_count} TCs={tc_count}: {title[:80]}{flag}", flush=True)

print("\nScript complete.", flush=True)
