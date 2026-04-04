# Security Rules

## AWS Credentials Profile
- ALWAYS use `fortress-tools-deployer` AWS profile for any AWS CLI operations
- NEVER use `openclaw-bedrock` profile for deployments or builds
- NEVER hardcode AWS account IDs — use env vars or IConfiguration
- NEVER hardcode AWS region — use env vars or IConfiguration (appsettings.json)

## Bedrock Model IDs
- NEVER hardcode Bedrock model IDs in source code
- Always read model IDs from `IConfiguration` (appsettings.json / environment override)
- Pattern: `_config["Bedrock:ModelId"]` or equivalent — never a string literal

## Docker Image Builds
- NEVER build FAIT/FIRM/FORMS/NEXUS/FAMOS Docker images locally
- ALL Docker builds go through AWS CodeBuild (buildspec.yml in each service)
- Local `docker build` is PROHIBITED for these services — CodeBuild only

## Auth-Bypass Paths (bypassPermissions spec)
These paths are NEVER to be touched by pipeline agents under bypassPermissions:
- `.git/` — git internals
- `.claude/` — agent rule files (this directory)
- `~/.bashrc`, `~/.bash_profile`, `~/.zshrc`, `~/.profile` — shell config
- `~/.ssh/` — SSH keys
- `~/.aws/credentials` — AWS credentials
- Any file outside `~/projects/fip/` unless explicitly specified in the WI

## Secrets
- No secrets, tokens, or credentials in source code
- No secrets in commit messages
- AWS Secrets Manager or environment variables only
