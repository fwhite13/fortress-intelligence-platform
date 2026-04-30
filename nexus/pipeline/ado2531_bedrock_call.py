#!/usr/bin/env python3
"""ADO#2531 — Standalone Bedrock validation call for ArtifactGenSystem prompt."""

import boto3
import json
import re
import sys

SPEC_PATH = "/home/fredw/.openclaw/workspace/memory/projects/nexus-decomp-upgrade-spec-2026-04-27.md"
FORGE_KB_PATH = "/home/fredw/.openclaw/workspace/memory/projects/forge-kb-mcp-server-spec-2026-04-27.md"
OUTPUT_PATH = "/home/fredw/projects/fip/nexus/pipeline/ADO2531-BEDROCK-OUTPUT.json"

def extract_system_prompt(spec_path):
    """Extract and unescape the ArtifactGenSystem prompt value from §11."""
    with open(spec_path, 'r') as f:
        content = f.read()

    # Find the ArtifactGenSystem JSON string value
    # Pattern: "ArtifactGenSystem": "..." where the value spans many lines (escaped)
    match = re.search(r'"ArtifactGenSystem":\s*"((?:[^"\\]|\\.)*)"', content, re.DOTALL)
    if not match:
        print("ERROR: Could not find ArtifactGenSystem value in spec")
        sys.exit(1)

    raw_value = match.group(1)

    # Unescape: \n -> newline, \" -> quote, \\ -> backslash
    # Python's codecs can handle this
    unescaped = raw_value.encode('utf-8').decode('unicode_escape')

    return unescaped

def build_user_message(forge_kb_path):
    """Build user message: prefix + full FORGE KB spec text."""
    with open(forge_kb_path, 'r') as f:
        spec_text = f.read()

    return f"Please decompose the following specification into Azure DevOps work items per your instructions:\n\n---\n\n{spec_text}"

def main():
    print("Extracting system prompt from §11...")
    system_prompt = extract_system_prompt(SPEC_PATH)
    print(f"System prompt length: {len(system_prompt)} chars")

    print("Building user message from FORGE KB spec...")
    user_message = build_user_message(FORGE_KB_PATH)
    print(f"User message length: {len(user_message)} chars")

    print("Making Bedrock call...")
    session = boto3.Session(profile_name='fortress-tools-deployer', region_name='us-east-1')
    from botocore.config import Config
    client = session.client('bedrock-runtime', config=Config(read_timeout=600))

    request_body = {
        "anthropic_version": "bedrock-2023-05-31",
        "anthropic_beta": ["output-128k-2025-02-19"],
        "max_tokens": 65536,
        "system": system_prompt,
        "messages": [
            {"role": "user", "content": user_message}
        ]
    }

    try:
        response = client.invoke_model(
            modelId='us.anthropic.claude-sonnet-4-20250514-v1:0',
            body=json.dumps(request_body),
            contentType='application/json',
            accept='application/json'
        )
    except Exception as e:
        print(f"ERROR: Bedrock call failed: {e}")
        # Write error info for the report
        with open(OUTPUT_PATH, 'w') as f:
            json.dump({"error": str(e)}, f, indent=2)
        sys.exit(1)

    result = json.loads(response['body'].read())
    raw_text = result['content'][0]['text']

    # Print metadata
    usage = result.get('usage', {})
    print(f"Input tokens: {usage.get('input_tokens', 'N/A')}")
    print(f"Output tokens: {usage.get('output_tokens', 'N/A')}")
    print(f"Stop reason: {result.get('stop_reason', 'N/A')}")

    # Try to parse as JSON
    parse_ok = False
    try:
        parsed = json.loads(raw_text)
        parse_ok = True
        print(f"JSON parse: SUCCESS — {len(parsed)} items in array")
        with open(OUTPUT_PATH, 'w') as f:
            json.dump(parsed, f, indent=2)
    except json.JSONDecodeError as e:
        print(f"JSON parse: FAILURE — {e}")
        with open(OUTPUT_PATH, 'w') as f:
            f.write(raw_text)

    # Write metadata sidecar for the report
    meta = {
        "input_tokens": usage.get('input_tokens'),
        "output_tokens": usage.get('output_tokens'),
        "stop_reason": result.get('stop_reason'),
        "parse_ok": parse_ok,
        "item_count": len(parsed) if parse_ok else None,
        "model": result.get('model', 'us.anthropic.claude-sonnet-4-5')
    }
    with open(OUTPUT_PATH.replace('.json', '-META.json'), 'w') as f:
        json.dump(meta, f, indent=2)

    print(f"\nOutput written to: {OUTPUT_PATH}")
    print("Done.")

if __name__ == '__main__':
    main()
