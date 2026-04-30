import boto3, json, re, sys
from botocore.config import Config

# Load v4 prompt
with open("/home/fredw/.openclaw/workspace/memory/projects/nexus-prompt-v4-candidate.md") as f:
    candidate_text = f.read()

match = re.search(r"## Full prompt text.*?```\n(.*?)```", candidate_text, re.DOTALL)
if not match:
    print("ERROR: Could not find Full prompt text code block", file=sys.stderr)
    sys.exit(1)

raw_string = match.group(1).strip()
if raw_string.startswith('"') and raw_string.endswith('"'):
    raw_string = raw_string[1:-1]
# Unescape: \\n -> literal \n first, then \n -> newline, \" -> "
system_prompt = raw_string.replace("\\\\n", "\x00LITERAL_BACKSLASH_N\x00").replace("\\n", "\n").replace('\\"', '"').replace("\x00LITERAL_BACKSLASH_N\x00", "\\n")

print(f"System prompt extracted: {len(system_prompt)} chars", flush=True)

# Load FORGE KB spec
with open("/home/fredw/.openclaw/workspace/memory/projects/forge-kb-mcp-server-spec-2026-04-27.md") as f:
    forge_spec = f.read()

user_message = "Please decompose the following specification into Azure DevOps work items per your instructions:\n\n---\n\n" + forge_spec

# Call Bedrock with extended timeout
session = boto3.Session(profile_name="fortress-tools-deployer", region_name="us-east-1")
bedrock_config = Config(read_timeout=300, connect_timeout=10, retries={"max_attempts": 0})
client = session.client("bedrock-runtime", config=bedrock_config)

print("Calling Bedrock with v4 prompt...", flush=True)
fallback_used = False
try:
    response = client.invoke_model(
        modelId="us.anthropic.claude-sonnet-4-20250514-v1:0",
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
    model_used = "us.anthropic.claude-sonnet-4-20250514-v1:0"
except Exception as e:
    print(f"Bedrock call failed with us.anthropic.claude-sonnet-4-20250514-v1:0: {e}", flush=True)
    print("Retrying with fallback model us.anthropic.claude-3-5-sonnet-20241022-v2:0...", flush=True)
    response = client.invoke_model(
        modelId="us.anthropic.claude-3-5-sonnet-20241022-v2:0",
        body=json.dumps({
            "anthropic_version": "bedrock-2023-05-31",
            "max_tokens": 8192,
            "system": system_prompt,
            "messages": [{"role": "user", "content": user_message}]
        }),
        contentType="application/json",
        accept="application/json"
    )
    model_used = "us.anthropic.claude-3-5-sonnet-20241022-v2:0"
    fallback_used = True
    print("Fallback model used.", flush=True)

result = json.loads(response["body"].read())
raw_text = result["content"][0]["text"].strip()
usage = result.get("usage", {})
print(f"Response received. Input: {usage.get('input_tokens')} tokens, Output: {usage.get('output_tokens')} tokens", flush=True)
print(f"Model: {model_used}", flush=True)

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
    print(f"First 500 chars of response: {raw_text[:500]}", flush=True)

# Write output
output_path = "/home/fredw/projects/fip/nexus/pipeline/ADO2557-BEDROCK-OUTPUT.json"
with open(output_path, "w") as f:
    if parse_ok:
        json.dump(wi_array, f, indent=2)
    else:
        f.write(raw_text)
print(f"Output written to {output_path}", flush=True)

# Summary
type_counts = {}
for wi in wi_array:
    t = wi.get("type", "Unknown")
    type_counts[t] = type_counts.get(t, 0) + 1
print("WI type counts:", type_counts, flush=True)

epics = [wi for wi in wi_array if wi.get("type") == "Epic"]
print(f"\nEpics ({len(epics)}):", flush=True)
for ep in epics:
    print(f"  - {ep.get('title')}", flush=True)

ext_deps = [wi for wi in wi_array if wi.get("isExternalDependency") == True]
print(f"\nExternal deps ({len(ext_deps)}):", flush=True)
for ed in ext_deps:
    print(f"  - {ed.get('title')} | owner={ed.get('externalOwner')} | tags={ed.get('tags')}", flush=True)

tcs = [wi for wi in wi_array if wi.get("type") == "Test Case"]
print(f"\nTest Cases ({len(tcs)}):", flush=True)
tc_parents = {}
for tc in tcs:
    p = tc.get("parentTitle", "?")
    tc_parents[p] = tc_parents.get(p, 0) + 1
for parent, count in sorted(tc_parents.items(), key=lambda x: -x[1]):
    print(f"  {count} TCs under: {parent}", flush=True)

# Check for get_job_status TCs specifically
gjs_tcs = [tc for tc in tcs if "job" in tc.get("parentTitle", "").lower() or "polling" in tc.get("parentTitle", "").lower() or "status" in tc.get("parentTitle", "").lower()]
print(f"\nget_job_status-related TCs: {len(gjs_tcs)}", flush=True)
for tc in gjs_tcs:
    print(f"  - {tc.get('title')}", flush=True)

# Check for FIRM migration
migration_wis = [wi for wi in wi_array if wi.get("wiTemplate") == "migration"]
print(f"\nMigration WIs ({len(migration_wis)}):", flush=True)
for mw in migration_wis:
    print(f"  - {mw.get('title')}", flush=True)

# Infrastructure WIs
infra_wis = [wi for wi in wi_array if wi.get("wiTemplate") == "infrastructure"]
print(f"\nInfrastructure WIs ({len(infra_wis)}):", flush=True)
for iw in infra_wis:
    print(f"  - {iw.get('title')}", flush=True)

# Cross-Epic predecessors for FAIT v2 DB stories
print("\nFAIT v2 DB stories (Epic 2) predecessorTitles:", flush=True)
if len(epics) >= 2:
    epic2_title = epics[1].get("title", "")
    # Find features under epic2
    epic2_features = [wi for wi in wi_array if wi.get("type") == "Feature" and wi.get("parentTitle") == epic2_title]
    for feat in epic2_features:
        feat_title = feat.get("title", "")
        stories_under = [wi for wi in wi_array if wi.get("type") == "User Story" and wi.get("parentTitle") == feat_title]
        for s in stories_under:
            print(f"  - {s.get('title')} | predecessorTitles={s.get('predecessorTitles')}", flush=True)
else:
    print("  WARNING: Less than 2 Epics found!", flush=True)

# Check stories with 4+ ACs
print("\nStories with 4+ ACs:", flush=True)
for wi in wi_array:
    if wi.get("type") == "User Story" and wi.get("wiTemplate") == "standard":
        ac = wi.get("acceptanceCriteria", "") or ""
        # Count Given/When/Then blocks or numbered items
        ac_count = len(re.findall(r'(?:Given|AC-|^\d+\.)', ac, re.MULTILINE))
        if ac_count == 0:
            # Try counting by line items or bullet points
            ac_count = len([line for line in ac.split('\n') if line.strip().startswith(('-', '*', '•')) and len(line.strip()) > 5])
        if ac_count >= 4:
            story_tcs = [tc for tc in tcs if tc.get("parentTitle") == wi.get("title")]
            print(f"  - {wi.get('title')} | ACs~{ac_count} | TCs={len(story_tcs)}", flush=True)

print("\nDone.", flush=True)
