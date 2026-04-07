# NEXUS Discovery KB Seed Documents

## Status
Documents are created and ready to upload. Two values are needed from Fred/Rob before upload can proceed:

1. **FORGE-DevTeam-Shared S3 bucket name** — the S3 bucket that backs the FORGE KB
2. **FORGE KB ID** — needed to update `Nexus:DiscoveryKnowledgeBaseId` in appsettings / Key Vault

## To Upload
Once bucket name is confirmed:
```bash
FORGE_KB_BUCKET=<actual-bucket-name> ./upload-seed.sh
```

## To Update Config After KB ID Confirmed
Update `appsettings.json` (or Key Vault for production):
```json
"Nexus": {
  "DiscoveryKnowledgeBaseId": "<actual-kb-id>"
}
```

## Documents
| File | Purpose |
|------|---------|
| `nexus-discovery/iaapa-portal-case-study.md` | Gold standard discovery question examples |
| `nexus-discovery/sample-spec-tig.md` | Complete spec format reference |
| `nexus-discovery/fip-dev-wiki.md` | FIP auth/deployment/data patterns |
| `nexus-discovery/fip-arch-overview.md` | Architecture context for discovery agent |
| `nexus-discovery/lessons-learned.md` | Lessons from past projects |
