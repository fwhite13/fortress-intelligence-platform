# BUILD BRIEF — ADO#2865 — Design Agent
**Tony Stark — BUILD cycle 2 (review fixes)**

## Review verdict: NEEDS-CHANGES
Clint found 3 critical + 1 important issue. Fix ONLY these — no scope creep.

---

## Fix 1 (CRITICAL): `downloadBase64` JS function is missing — runtime crash on download

`DesignArtifactCard.razor` calls `JS.InvokeVoidAsync("downloadBase64", ...)` but there is no custom JS file loaded anywhere. Every Download button click throws a JS TypeError.

**Fix:**

Create `wwwroot/js/app.js`:
```javascript
window.downloadBase64 = function (fileName, base64String, mimeType) {
    const link = document.createElement('a');
    link.href = `data:${mimeType};base64,${base64String}`;
    link.download = fileName;
    document.body.appendChild(link);
    link.click();
    document.body.removeChild(link);
};
```

Then reference it in `Components/App.razor` (or `_Host.cshtml` if that's the entry point — check the existing structure). Add a `<script src="/js/app.js"></script>` tag just before `</body>`.

---

## Fix 2 (CRITICAL): `DesignAgentService` never writes to DB

`DesignAgentSession` and `DesignAgentArtifact` models exist in the DB context but `DesignAgentService` never persists them. `_currentSessionId` in the view is an ephemeral client-side GUID.

**Fix:** Inject `IDbContextFactory<FaitV2DbContext>` into `DesignAgentService`. Persist:

1. **Session creation** — In `GenerateScreenAsync`, before generating:
```csharp
// Create or reuse session
var sessionId = Guid.NewGuid().ToString();
await using var db = await _dbFactory.CreateDbContextAsync(ct);
var session = new DesignAgentSession
{
    Id = sessionId,
    UserId = userId,
    StitchProjectId = null,
    DesignDna = designDnaContext,
    CreatedAt = DateTime.UtcNow,
    UpdatedAt = DateTime.UtcNow
};
db.DesignAgentSessions.Add(session);
await db.SaveChangesAsync(ct);
```

2. **Artifact persistence** — In `SaveArtifactAsync`, after S3 upload, persist a `DesignAgentArtifact` record. You'll need to thread `sessionId` through to `SaveArtifactAsync` — add it as a parameter or return it from `GenerateScreenAsync` so the caller can pass it. The `DesignAgentResult` record already has a `ScreenId` field — you can store the sessionId there if needed.

Keep it simple — the goal is that design sessions are persisted so they can be listed/retrieved later. Don't over-engineer.

---

## Fix 3 (CRITICAL): `IsStitchAvailableAsync` health check never actually calls the endpoint

The method has a branch for `Stitch:HealthEndpoint` config but returns `Task.FromResult(true)` without making any HTTP call. This means availability checks always say "available" regardless of reality.

**Fix:** Two options — pick the simpler one:

**Option A (recommended):** Remove the health endpoint branch entirely. Rely on exception-as-availability-signal: if `DispatchToolCallAsync` throws on the first Stitch call, catch it in `GenerateScreenAsync` and fall back to CC-native HTML. `IsStitchAvailableAsync` simply checks whether `Stitch:GcpCredentialsConfigured` is `"true"`:
```csharp
public Task<bool> IsStitchAvailableAsync(CancellationToken ct = default)
{
    var configured = _config["Stitch:GcpCredentialsConfigured"];
    return Task.FromResult(configured == "true");
}
```

**Option B:** If you kept the HTTP health endpoint branch, actually implement the HTTP call:
```csharp
if (!string.IsNullOrEmpty(healthEndpoint))
{
    try
    {
        var client = _httpClientFactory.CreateClient();
        var resp = await client.GetAsync(healthEndpoint, ct);
        return resp.IsSuccessStatusCode;
    }
    catch
    {
        return false;
    }
}
```

---

## Fix 4 (IMPORTANT): Add logging to swallowed catch in `SendPrompt`

In `DesignAgentView.razor` or wherever `SendPrompt` catches exceptions silently, add:
```csharp
catch (Exception ex)
{
    Logger.LogError(ex, "SendPrompt failed for userId={UserId}", _userId);
    // existing error handling...
}
```

---

## Process
1. Use CC: `CLAUDE_CODE_ENTRYPOINT=ado-pipeline CLAUDE_CODE_DISABLE_AUTO_MEMORY=1 CLAUDE_BASH_MAINTAIN_PROJECT_WORKING_DIR=1 CLAUDE_CODE_GLOB_TIMEOUT_SECONDS=30 cat pipeline/brief-c2-2865.md | claude --model sonnet --print --dangerously-skip-permissions`
2. Verify build: `dotnet build` (0 errors, 0 warnings)
3. Pull and rebase: `git pull --rebase origin main` (Lane 1 WI#2887 may have concurrent commits)
4. Commit: `git add -A && git commit -m "fix(fait-v2#2865): downloadBase64 JS, DB persistence, IsStitchAvailableAsync, logging"`
5. Push: `git push origin main`
6. Post ADO comment: `mcporter call devops.add_comment --args '{"project":"Fortress","id":2865,"text":"**[Tony Stark — BUILD cycle 2]**\nCommit {hash}: Added downloadBase64 JS function, DB persistence for sessions/artifacts, fixed IsStitchAvailableAsync, added error logging. Build: SUCCEEDED."}'`
7. Update Build Report at `pipeline/ADO2865-BUILD-REPORT.md`
8. Reply with your Build Report so Maria can send back to Clint.
