# FAIT v2 — CLAUDE.md

## Project
FAIT v2 (FortressAI v2) — Fargate-based AI toolkit for Refuge Group.
Repo: `~/projects/fip/fait-v2/`
Branch: `main`

## Architecture
- **Blazor app:** `src/FortressAI.V2.Web/` (.NET 8, MudBlazor)
- **Agent harness:** `agent-harness/` (Node.js 20, Express, port 3000)
- **DB:** Aurora MySQL (EF Core, GuidFormat=None, varchar(36) for all GUID columns)
- **Auth:** Entra SSO (unified FIP cookie — Cognito removed)
- **AWS:** ECS Fargate, ECR, ALB, CodeBuild, CloudWatch, Secrets Manager

## Stitch MCP (Google Labs)
- **Package:** `stitch-mcp` (npm, binary: `stitch-mcp`)
- **Purpose:** HTML/CSS visual screen generation, design DNA extraction
- **Tools:** `generate_screen_from_text`, `extract_design_context`, `fetch_screen_code`, `fetch_screen_image`, `list_projects`, `list_screens`, `refine_screen`
- **Auth:** GCP service account via `GOOGLE_APPLICATION_CREDENTIALS` env var, bootstrapped from Secrets Manager secret `fait-v2/gcp-stitch-service-account` at harness startup
- **Availability:** Only when GCP credentials are configured. Check `GET /tools/stitch/health` on the harness before use.
- **Harness routing:** Stitch tools are called via `POST /tools/{toolName}` on the harness (port 3000)

## CSS Rules
- ALL UI elements must be CSS-class-driven — no inline styles, no MudBlazor default props

## Critical Rules
- NEVER hardcode AWS account IDs or region strings — use env vars
- varchar(36) for GUID columns, GuidFormat=None on ALL MySQL connections
- Build FIP apps from `~/projects/fip/` monorepo root
- FORMS MUST use `Dockerfile.debian` — standard Dockerfile fails on WSL2
