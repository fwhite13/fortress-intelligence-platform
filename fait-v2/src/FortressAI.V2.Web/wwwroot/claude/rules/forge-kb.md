# FORGE Knowledge Base Access

FORGE KBs are AWS Bedrock Knowledge Bases. KB content is injected directly into your context envelope by the platform — no tool call is needed to retrieve it.

## How KB Access Works
- KB context is pre-loaded into your session before you start
- If a KB is enabled for your session, its relevant content already appears in your context
- You do NOT need to call any tool to query or fetch KB content
- There are no MCP KB query tools — the system handles KB injection automatically at the envelope level

## Access Scope
- Only KBs whose content appears in your context are available to you
- You cannot enumerate or query KBs directly — use only what is already in context
- Respect read/write permissions listed per KB in your context envelope
