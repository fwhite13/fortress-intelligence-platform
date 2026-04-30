#!/usr/bin/env python3
"""ADO#2543 — v2 Bedrock validation call for §11 ArtifactGenSystem prompt."""

import boto3
import json
import re

# --- Step 1: Extract the ArtifactGenSystem prompt from the spec ---
spec_path = "/home/fredw/.openclaw/workspace/memory/projects/nexus-decomp-upgrade-spec-2026-04-27.md"
with open(spec_path, "r") as f:
    spec_text = f.read()

# Find the JSON block containing ArtifactGenSystem
# The value is a single long JSON string on one line
match = re.search(r'"ArtifactGenSystem":\s*"((?:[^"\\]|\\.)*)"', spec_text)
if not match:
    raise ValueError("Could not find ArtifactGenSystem in spec")

raw_json_string = match.group(1)

# Unescape JSON string escapes
system_prompt = raw_json_string.replace('\\n', '\n').replace('\\"', '"').replace('\\\\', '\\')

print(f"System prompt extracted: {len(system_prompt)} chars")
print(f"First 200 chars: {system_prompt[:200]}")

# --- Step 2: Read the FORGE KB spec (user message input) ---
forge_spec_path = "/home/fredw/.openclaw/workspace/memory/projects/forge-kb-mcp-server-spec-2026-04-27.md"
with open(forge_spec_path, "r") as f:
    forge_spec = f.read()

user_message = (
    "Please decompose the following specification into Azure DevOps work items "
    "per your instructions:\n\n---\n\n" + forge_spec
)

print(f"\nUser message: {len(user_message)} chars")

# --- Step 3: Make the Bedrock call ---
from botocore.config import Config

session = boto3.Session(profile_name="fortress-tools-deployer", region_name="us-east-1")
client = session.client("bedrock-runtime", config=Config(read_timeout=300))

print("\nCalling Bedrock (us.anthropic.claude-sonnet-4-5)...")

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

result = json.loads(response["body"].read())
raw_text = result["content"][0]["text"]
usage = result.get("usage", {})

print(f"\nTokens used: {usage}")
print(f"Stop reason: {result.get('stop_reason', 'unknown')}")

# --- Step 4: Parse and save ---
# Strip any markdown fences if present
clean_text = raw_text.strip()
if clean_text.startswith("```"):
    clean_text = re.sub(r'^```\w*\n?', '', clean_text)
    clean_text = re.sub(r'\n?```$', '', clean_text)
    clean_text = clean_text.strip()

wi_array = json.loads(clean_text)
print(f"\nParsed successfully: {len(wi_array)} WIs")

output_path = "/home/fredw/projects/fip/nexus/pipeline/nexus-prompt-validation-output-v2.json"
with open(output_path, "w") as f:
    json.dump(wi_array, f, indent=2)

print(f"Output saved to: {output_path}")

# --- Step 5: Quick summary ---
type_counts = {}
for wi in wi_array:
    t = wi.get("type", "Unknown")
    type_counts[t] = type_counts.get(t, 0) + 1

print("\nWI Type Counts:")
for t, c in sorted(type_counts.items()):
    print(f"  {t}: {c}")

# Show epics
epics = [wi for wi in wi_array if wi.get("type") == "Epic"]
print(f"\nEpics ({len(epics)}):")
for e in epics:
    print(f"  - {e['title']}")

# Show external deps
ext_deps = [wi for wi in wi_array if wi.get("isExternalDependency")]
print(f"\nExternal Dependencies ({len(ext_deps)}):")
for ed in ext_deps:
    print(f"  - {ed['title']} | owner={ed.get('externalOwner')} | tags={ed.get('tags')}")
