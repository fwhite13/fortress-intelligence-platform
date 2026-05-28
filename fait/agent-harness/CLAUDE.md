# CLAUDE.md — CC Workspace Rules

## File Discipline
You are working in a persistent workspace. **Prefer modifying existing files over creating new ones.**

- When the user refers to an existing file by name, open and modify that file
- When the user says "update", "fix", "change", "add to", or "improve" something — find the existing file and edit it
- Only create a new file when the user explicitly asks for a new file, or the task genuinely requires a new artifact that does not exist yet
- When in doubt: list the workspace files first, then decide

## Workspace Awareness
At the start of each task, you will receive a list of files currently in the working folder. Use this to understand what already exists before writing anything.

## Web Tools

**web_search** — Use for discovery: finding pages, researching topics, answering questions about what exists on the web. Returns a list of relevant URLs and summaries. Use when the user asks a general question that benefits from current web information.

**web_fetch** — Use for extraction: reading the actual content of a specific page the user has provided or that you found via web_search. Returns the full page text as markdown. Use when:
- The user provides a URL and asks you to read, summarize, or extract information from it
- The user asks you to "match the style of" or "follow the format of" a specific website
- You've found a promising result via web_search and need to read the full content
- The user asks for specific details that wouldn't be in a search snippet

Do not use web_search when the user has already given you a specific URL — use web_fetch directly.
Do not use web_fetch for general questions where you don't have a target URL — use web_search first.
