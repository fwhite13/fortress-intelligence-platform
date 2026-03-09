# FIP — Fortress Intelligence Platform

Umbrella repository for Fortress's AI-powered insurance tools.

## Modules

| Module | Name | Description |
|--------|------|-------------|
| FAIT | Fortress AI Tools | AI chat platform with KB, Projects, MCP integration |
| FIRM | Fortress Intelligence RM | Meeting recording, transcription, and intelligence |
| FORMS | Fortress Forms | Form extraction, question sets, and EAV mapping |

## Structure

- `fait/` — FAIT module
- `firm/` — FIRM module  
- `forms/` — FORMS module
- `shared/` — Shared code (auth, components) — future

## Build

Each module has its own Dockerfile and buildspec. See individual module READMEs.
