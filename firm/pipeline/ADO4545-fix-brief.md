# CC Fix Brief: ADO#4545 — OnRedirectToLogin suppression for /api/ paths

## Context

File: `firm/src/FortressIntelligenceRM.Web/Program.cs`

QA found that the `DefaultChallengeScheme = Cookie` causes the cookie middleware to append a `Location: /auth/redirect-to-login` redirect header on all unauthenticated requests — including `/api/` paths. Mobile API clients receive a `401` with a redirect header and empty body instead of a clean `401 JSON`.

## Task

In `firm/src/FortressIntelligenceRM.Web/Program.cs`, find the existing `.AddCookie(options => { ... })` block. It currently ends with:

```csharp
    options.Cookie.IsEssential = true;
})
```

Add an `OnRedirectToLogin` event handler **inside** that cookie options lambda, so the block becomes:

```csharp
    options.Cookie.IsEssential = true;
    options.Events.OnRedirectToLogin = ctx =>
    {
        if (ctx.Request.Path.StartsWithSegments("/api"))
        {
            ctx.Response.StatusCode = 401;
            return Task.CompletedTask;
        }
        ctx.Response.Redirect(ctx.RedirectUri);
        return Task.CompletedTask;
    };
})
```

## Constraints

- **ONLY change the `AddCookie` options lambda in `Program.cs`** — no other files
- Do NOT change any other options, policies, middleware, or files
- Do NOT add any using statements (they are not needed — `Task` is already in scope)

## After the change

1. Run `dotnet build firm/src/FortressIntelligenceRM.Web/FortressIntelligenceRM.Web.csproj` from `/home/fredw/projects/fip/`
2. Confirm build succeeds with 0 errors
3. Confirm no other files were changed (git diff --name-only)
4. Print the final diff of the change

## Working directory

`/home/fredw/projects/fip/`
