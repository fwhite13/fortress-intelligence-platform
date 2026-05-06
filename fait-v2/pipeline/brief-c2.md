# CC Brief — ADO#2842 Build Cycle 2

## Task
Two targeted fixes only. No scope creep. Do not touch any other files.

## Project Root
`/home/fredw/projects/fip/fait-v2/src/FortressAI.V2.Web/`

---

## Fix I1 — Add `@attribute [Authorize]` to Onboarding.razor

**File:** `/home/fredw/projects/fip/fait-v2/src/FortressAI.V2.Web/Components/Pages/Onboarding.razor`

Current line 1:
```
@page "/onboarding"
```

Change to:
```
@page "/onboarding"
@attribute [Authorize]
```

Insert `@attribute [Authorize]` as line 2, immediately after the `@page` directive — same pattern as all other pages in the same directory.

---

## Fix I2 — Move security headers middleware above UseStaticFiles() in Program.cs

**File:** `/home/fredw/projects/fip/fait-v2/src/FortressAI.V2.Web/Program.cs`

Current order (wrong):
```csharp
app.UseStaticFiles();

// Security headers
app.Use(async (context, next) =>
{
    context.Response.Headers["X-Content-Type-Options"] = "nosniff";
    context.Response.Headers["X-Frame-Options"] = "DENY";
    context.Response.Headers["X-XSS-Protection"] = "1; mode=block";
    context.Response.Headers["Referrer-Policy"] = "strict-origin-when-cross-origin";
    await next();
});
```

Replace with (correct order — security headers BEFORE static files):
```csharp
// Security headers — must be before UseStaticFiles
app.Use(async (context, next) =>
{
    context.Response.Headers["X-Content-Type-Options"] = "nosniff";
    context.Response.Headers["X-Frame-Options"] = "DENY";
    context.Response.Headers["X-XSS-Protection"] = "1; mode=block";
    context.Response.Headers["Referrer-Policy"] = "strict-origin-when-cross-origin";
    await next();
});

app.UseStaticFiles();
```

---

## Constraints
- Touch ONLY these two files: `Components/Pages/Onboarding.razor` and `Program.cs`
- Do NOT add, remove, or change anything else
- Do NOT reformat or reorder anything outside the specified change
- Do NOT add any comments except the updated comment on the security headers block

## Done
After both edits are made, output "FIXES APPLIED" and list the two files modified.
