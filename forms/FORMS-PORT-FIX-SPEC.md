# FORMS Port Fix Spec

**Author:** Reed Richards (Software Architect)  
**Date:** 2026-03-17  
**Status:** Ready for Implementation — surgical, 3 files  
**Target:** Tony Stark (software-engineer) via CC  
**Reviewer:** Clint Barton (code-reviewer)

---

## Root Cause

`appsettings.Development.json` binds Kestrel to port 5200:

```json
{
  "Kestrel": {
    "Endpoints": {
      "Http": {
        "Url": "http://0.0.0.0:5200"
      }
    }
  }
}
```

`Properties/launchSettings.json` also specifies port 5200:

```json
"applicationUrl": "http://0.0.0.0:5200"
```

`Program.cs` lines 129–130 hardcode 5200 for the internal HttpClient base address:

```csharp
var internalBaseUrl = builder.Environment.IsDevelopment()
    ? "http://localhost:5200/"   // ← wrong in ECS dev env
    : "http://localhost:8080/";
```

Line 387 of `Program.cs` also logs the wrong port:

```csharp
Console.WriteLine("  Running at: http://localhost:5200");
```

**Why this breaks ECS:** In ECS, `ASPNETCORE_ENVIRONMENT=Development` is set so the dev Kestrel config wins — port 5200. The ALB target group expects port 8080. Manual TG re-registration required after every deploy.

**Why FAIT and FIRM don't have this problem:** FAIT's `appsettings.Development.json` contains only logging config and stub auth flags — no Kestrel endpoint override. Kestrel falls back to `ASPNETCORE_URLS=http://+:8080` from the ECS task definition. FORMS added the Kestrel block for local dev convenience and never removed it.

---

## Fix: 3 Files, Minimal Changes

### File 1: `appsettings.Development.json`

**Remove the entire `Kestrel` block.** Replace with FAIT-equivalent content (logging + dev flags):

```json
{
  "Logging": {
    "LogLevel": {
      "Default": "Information",
      "Microsoft.AspNetCore": "Warning"
    }
  }
}
```

The local dev server will now bind to whatever `ASPNETCORE_URLS` is set to, or the default (`http://localhost:5000`) if not set. Developers running locally should set `ASPNETCORE_URLS=http://localhost:5200` in their shell or `.env` if they want port 5200 — it's not committed.

### File 2: `Program.cs`

**Two line changes:**

**Change 1** — Lines 129–130: Remove the dev/prod port branch. Both environments use 8080 (Kestrel binds to whatever `ASPNETCORE_URLS` says):

```csharp
// Before:
var internalBaseUrl = builder.Environment.IsDevelopment()
    ? "http://localhost:5200/"
    : "http://localhost:8080/";

// After:
var internalBaseUrl = "http://localhost:8080/";
```

**Change 2** — Line 387: Fix the startup log message:

```csharp
// Before:
Console.WriteLine("  Running at: http://localhost:5200");

// After:
Console.WriteLine("  Running at: http://localhost:8080 (or ASPNETCORE_URLS if set)");
```

### File 3: `Properties/launchSettings.json`

Update `applicationUrl` to match the actual port:

```json
{
  "profiles": {
    "FortressFormTools.Web": {
      "commandName": "Project",
      "dotnetRunMessages": true,
      "launchBrowser": false,
      "applicationUrl": "http://localhost:8080",
      "environmentVariables": {
        "ASPNETCORE_ENVIRONMENT": "Development"
      }
    }
  }
}
```

Developers who prefer port 5200 locally can add `"ASPNETCORE_URLS": "http://localhost:5200"` to their `environmentVariables` block in their personal `launchSettings.json` override — not committed.

---

## ECS Verification

The FORMS ECS task definition must have `ASPNETCORE_URLS=http://+:8080`. Confirm with:

```bash
aws ecs describe-task-definition --task-definition formiq \
  --query 'taskDefinition.containerDefinitions[0].environment'
```

If `ASPNETCORE_URLS` is absent, add it. Without it, ASP.NET Core 9's default is `http://+:8080` when `ASPNETCORE_ENVIRONMENT` is not `Development` — but with `Development` set and no `ASPNETCORE_URLS`, it defaults to `http://localhost:5000`. After this fix, the `appsettings.Development.json` Kestrel block is gone, so Kestrel will use `ASPNETCORE_URLS` regardless of environment.

**This is a DevOps task (Rhodey) to confirm/add the env var — no code change.**

---

## Acceptance Criteria

1. FORMS starts and binds to port 8080 in ECS dev environment after deploy. ALB health check passes without manual TG re-registration.
2. `docker run -e ASPNETCORE_ENVIRONMENT=Development -e ASPNETCORE_URLS=http://+:8080 formiq` starts on 8080.
3. Local dev: `dotnet run` starts on 8080 (or `ASPNETCORE_URLS` if set).
4. No `appsettings.Development.json` Kestrel block remains.

---

## Clint Review Priorities

```
⚠️  HIGH: Verify appsettings.Development.json has NO Kestrel block after
          the change. The entire "Kestrel": {...} section must be removed.
          A partial removal (e.g. keeping the key but removing the URL)
          could result in Kestrel binding to port 0 (random).

⚠️  MEDIUM: Verify Program.cs internalBaseUrl change doesn't break local dev.
            The HttpClient base address is used for internal Blazor component
            API calls. After the fix it's hardcoded to 8080 — developers running
            locally on a different port will get connection refused on those calls.
            Acceptable for now: FORMS runs on 8080 everywhere consistently.

⚠️  LOW: Confirm ASPNETCORE_URLS=http://+:8080 is in the FORMS ECS task
         definition (Rhodey task). Without it, the fix is incomplete for
         the Development environment in ECS.
```

---

_Spec by Reed Richards | FORMS port fix: 3 files, 4 line changes. Remove `appsettings.Development.json` Kestrel block; fix 2 hardcoded port references in `Program.cs`; update `launchSettings.json`._
