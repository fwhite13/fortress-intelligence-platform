# FIP Monorepo — Agent Guide

This is the Fortress Intelligence Platform monorepo. Pipeline agents (Tony, Clint, Rhodey, Natasha) operate here.

## Sub-applications
- `fait/` — FAIT (Fortress AI Toolkit) — fred-chat ECS service
- `firm/` — FIRM (Meeting recording + transcription) — firm-web ECS service
- `nexus/` — NEXUS (Spec management) — nexus-web ECS service
- `famos/` — FAM OS — famos-dev ECS service
- `forms/` — FORMS (Form extraction)
- `shared/` — Shared models and DTOs

## Pipeline Rules
See `.claude/rules/` for detailed agent rules:
- `pipeline-rules.md` — CC invocation, wrapper script
- `security-rules.md` — credential and deployment rules
- `git-rules.md` — commit standards
- `test-rules.md` — build and test gates
