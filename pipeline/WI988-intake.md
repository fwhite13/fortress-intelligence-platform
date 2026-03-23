# WI#988 — Auto-Memory Plugin: Implement MEMORY-AUTO-INJECT-SPEC.md

## Type
Infrastructure / Platform

## Source
Fred White + Bruce/Reed research (2026-03-20)

## Description
Implement the auto-memory injection system for the OpenClaw pgvector memory plugin as specified in `~/projects/fip/memory/MEMORY-AUTO-INJECT-SPEC.md` (76KB, 1,978 lines).

This adds automatic memory recall (before each agent turn) and automatic memory capture (after each agent turn) to the existing `~/.openclaw/extensions/memory-pgvector/` plugin.

## Spec Location
`~/projects/fip/memory/MEMORY-AUTO-INJECT-SPEC.md`

The spec is complete and includes:
- §1–3: Auto-recall via `before_prompt_build` hook → pgvector search → `prependSystemContext` (Bedrock-cacheable)
- §4–5: Auto-capture via `agent_end` hook
- Addendum A1–A10: Mem0-style fact extraction pipeline (Stage 1: Pepper qwen2.5:14b, Stage 2: Pepper qwen2.5:32b-instruct-q4_K_M)
- New `POST /ingest/update` endpoint for serve.py
- JSONL retry queue with 3-attempt dead-letter

## Files to modify
- `~/.openclaw/extensions/memory-pgvector/index.ts` — main plugin (auto-recall + auto-capture + fact pipeline)
- `~/.openclaw/extensions/memory-pgvector/openclaw.plugin.json` — add new config fields
- `~/.openclaw/workspace/rag/serve.py` — add `/ingest/update` endpoint + chunk_id back-fill

## Key implementation notes from spec
- `runFactExtractionPipeline` MUST be defined as a closure inside `register(api)` — it references closed-over variables; module-level definition will silently break everything
- Both Ollama calls (30s, 90s timeout) run inside `setImmediate` — must NOT block the agent turn
- Pepper Ollama URL: `http://100.118.68.63:11434`
- Embeddings still go through local `serve.py` → `nomic-embed-text` (127.0.0.1:11434)
- Retry queue: `fs.appendFile` is atomic (O_APPEND) — safe for concurrent writes
- `chunk_type: "fact"` is distinct from existing `"conversation"` and `"daily_note"` types

## Testing
- After implementation: verify a conversation turn triggers fact extraction (check logs)
- Verify facts appear in pgvector with `chunk_type="fact"`
- Verify auto-recall injects relevant facts into system context on next turn
- Verify Pepper unreachable → retry queue written, not dropped
- Clint code review required; Natasha cannot browser-test this (infrastructure change)

## Notes
- This is the Jarvis-side implementation (main agent)
- FAIT/Cowork memory is a separate future effort (different spec, different scope)
- This does NOT require any OpenClaw core changes — plugin API only
