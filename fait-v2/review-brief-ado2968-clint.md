# Code Review Brief — ADO#2968
## Reviewer: Hawkeye (Clint Barton)
## Commit: d313f64 — Replace fip-mcp dependencies with direct integrations (v1 pattern)

You are performing a thorough code review of the following files. Read each file carefully.

## Files to Review

### New Files Created:
1. `src/Services/BraveSearchService.cs`
2. `src/Services/DevOpsConnectionService.cs`
3. `src/Services/MicrosoftTokenService.cs`
4. `src/Data/Models/UserDevOpsConnection.cs`
5. `src/Data/Migrations/20260508172829_AddUserDevOpsConnections.cs`

### Modified Files:
6. `src/Services/ForgeKbService.cs`
7. `src/Data/FaitV2DbContext.cs`
8. `src/FortressAI.V2.Web.csproj`
9. `src/Program.cs`

## Review Checklist

### 1. ForgeKbService.cs
- `ListKbsAsync` must make ZERO HTTP calls — reads config only
- No fip-mcp JSON-RPC calls remain ANYWHERE in the file
- Direct Bedrock calls: RetrieveAsync, GetKnowledgeBaseAsync, StartIngestionJobAsync

### 2. ContextEnvelopeService (if present in scope)
- No HTTP calls to mcp.fortressam.ai remain in startup path
- BuildEnvelopeAsync should complete cleanly even if Bedrock is unavailable

### 3. Program.cs
- FipMcpClient and IFipTokenProvider registrations REMOVED
- Bedrock services registered correctly (AWSSDK.BedrockAgent, AWSSDK.BedrockRuntime, AWSSDK.BedrockAgentRuntime)
- 4 new direct service registrations present

### 4. DevOpsConnectionService.cs
- PAT encrypted via DataProtection with "DevOpsPat" purpose
- NO plaintext PAT storage anywhere
- DB operations use IDbContextFactory (not injected DbContext directly)
- Interface IBraveSearchService / BraveSearchService pattern

### 5. MicrosoftTokenService.cs
- Reads from FipPortalDbContext — NOT IHttpContextAccessor (IHttpContextAccessor is null in Blazor Server context)
- Token refresh logic is sensible

### 6. BraveSearchService.cs
- Reads BraveSearch:ApiKey from config (IConfiguration)
- HttpClient used correctly (injected via IHttpClientFactory or constructor)
- No hardcoded API keys

### 7. UserDevOpsConnection.cs (EF entity)
- Table name follows fait-v2 snake_case convention: user_devops_connections
- Column names snake_case
- Any GUID columns: GuidFormat=None
- No plaintext PAT field — should store encrypted PAT

### 8. EF Migration (20260508172829_AddUserDevOpsConnections.cs)
- Migration creates correct table with snake_case columns
- No issues with schema

### 9. Dead Code Check
- FipTokenProvider.cs and IFipTokenProvider.cs: confirm they are NOT referenced anywhere in the active codebase (dead code, safe to leave)

### 10. csproj AWSSDK packages
- Verify AWSSDK package versions are reasonable (not ancient, not bleeding-edge pre-release)

### 11. No hardcoded secrets anywhere in any file

## Output Format

Please provide a detailed code review report covering:
1. Each file reviewed with findings (Critical / Important / Nitpick)
2. Specific line references for any issues found
3. Final verdict: PASS / NEEDS-CHANGES / FAIL
4. If NEEDS-CHANGES or FAIL: list all required fixes clearly

Be thorough. Check for security issues (especially around PAT storage and token handling), correctness of the v1 pattern migration, and adherence to fait-v2 conventions.
