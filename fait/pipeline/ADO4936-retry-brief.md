# ADO#4936 Retry — InternalPreview Port Fix

## Context
The FAIT web app registers an HttpClient named "InternalPreview" used to render DOCX/XLSX file previews.
The client's BaseAddress was set to `http://localhost/` (port 80).
Inside the ECS container, Kestrel is configured via `ASPNETCORE_URLS=http://+:8080` and does NOT listen on port 80.
This causes `HttpRequestException: Connection refused (localhost:80)` for every DOCX/XLSX preview request.

## Task: ONE-LINE FIX — Port 80 → Port 8080

### File to edit
`/home/fredw/projects/fip/fait/src/FortressAI.Web/Program.cs`

### Exact change required
Find this exact line (currently line 80):
```csharp
builder.Services.AddHttpClient("InternalPreview", c => { c.BaseAddress = new Uri("http://localhost/"); });
```

Replace it with:
```csharp
builder.Services.AddHttpClient("InternalPreview", c => { c.BaseAddress = new Uri("http://localhost:8080/"); });
```

The ONLY change is: `http://localhost/` → `http://localhost:8080/`

Do NOT change any other code in this file or any other file.

## Constraints
- Touch exactly ONE line in ONE file: `src/FortressAI.Web/Program.cs`
- No other changes anywhere
- No formatting changes, no whitespace changes beyond the URI string itself
- Do not add logging, do not add comments

## Acceptance Criteria
- Line 80 of Program.cs reads: `builder.Services.AddHttpClient("InternalPreview", c => { c.BaseAddress = new Uri("http://localhost:8080/"); });`
- No other lines in Program.cs changed
- File is syntactically valid C#
