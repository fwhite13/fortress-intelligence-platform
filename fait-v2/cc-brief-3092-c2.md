# CC Brief: ADO#3092 Cycle 2 Fixes

## Working directory
/home/fredw/projects/fip/fait-v2/src/FortressAI.V2.Web

## Fix 1 — AvatarModerationService.cs: Move hardcoded Bedrock model ID to IConfiguration

In file `Services/AvatarModerationService.cs`:

1. Remove `private const string ModerationModel = "us.anthropic.claude-haiku-4-5-20251001-v1:0";`
2. Add `private readonly string _moderationModel;`
3. Add `IConfiguration config` parameter to the constructor (after `ILogger<AvatarModerationService> logger`)
4. In constructor body, add: `_moderationModel = config["Bedrock:AvatarModerationModelId"] ?? "us.anthropic.claude-haiku-4-5-20251001-v1:0";`
5. Replace `ModelId = ModerationModel,` with `ModelId = _moderationModel,`

## Fix 2 — appsettings.json: Add Bedrock section and AWS:AvatarBaseUrl

In file `appsettings.json`:

The existing `"AWS"` section currently looks like:
```json
"AWS": {
    "Region": "us-east-1",
    "WorkspaceBucket": "fortress-user-workspaces"
},
```

Update it to:
```json
"AWS": {
    "Region": "us-east-1",
    "WorkspaceBucket": "fortress-user-workspaces",
    "AvatarBaseUrl": ""
},
```

Also add a new `"Bedrock"` section after the `"AWS"` section:
```json
"Bedrock": {
    "AvatarModerationModelId": "us.anthropic.claude-haiku-4-5-20251001-v1:0"
},
```

## Fix 3 — Program.cs: Config-drive S3 avatar URL base domain

In `Program.cs`, find the line (around line 946):
```csharp
var avatarUrl = $"https://{bucket}.s3.amazonaws.com/{s3Key}";
```

Replace it with:
```csharp
var avatarBaseUrl = config["AWS:AvatarBaseUrl"];
if (string.IsNullOrWhiteSpace(avatarBaseUrl))
    avatarBaseUrl = $"https://{bucket}.s3.amazonaws.com";
var avatarUrl = $"{avatarBaseUrl}/{s3Key}";
```

## Fix 4 — Program.cs: Add magic-byte MIME validation

In `Program.cs`, find the avatar upload endpoint. After the MIME type header validation block (after the line `if (!allowedTypes.Contains(mimeType))`), and BEFORE the size validation block (`const long maxBytes = 2 * 1024 * 1024;`), insert magic-byte validation.

The current code looks like:
```csharp
    // Validate MIME type
    var allowedTypes = new[] { "image/jpeg", "image/jpg", "image/png", "image/webp", "image/gif" };
    var mimeType = file.ContentType?.ToLowerInvariant() ?? string.Empty;
    if (!allowedTypes.Contains(mimeType))
        return Results.BadRequest(new { error = "Only image files are accepted (jpeg, png, webp, gif)" });

    // Validate size (2MB)
    const long maxBytes = 2 * 1024 * 1024;
```

Change it to:
```csharp
    // Validate MIME type
    var allowedTypes = new[] { "image/jpeg", "image/jpg", "image/png", "image/webp", "image/gif" };
    var mimeType = file.ContentType?.ToLowerInvariant() ?? string.Empty;
    if (!allowedTypes.Contains(mimeType))
        return Results.BadRequest(new { error = "Only image files are accepted (jpeg, png, webp, gif)" });

    // Magic-byte MIME validation (prevent spoofed Content-Type)
    using var magicStream = file.OpenReadStream();
    var magicBuffer = new byte[12];
    var magicBytesRead = await magicStream.ReadAsync(magicBuffer, 0, 12, ct);
    bool validMagic = mimeType switch
    {
        "image/jpeg" or "image/jpg" => magicBytesRead >= 3 && magicBuffer[0] == 0xFF && magicBuffer[1] == 0xD8 && magicBuffer[2] == 0xFF,
        "image/png"  => magicBytesRead >= 8 && magicBuffer[0] == 0x89 && magicBuffer[1] == 0x50 && magicBuffer[2] == 0x4E && magicBuffer[3] == 0x47,
        "image/gif"  => magicBytesRead >= 4 && magicBuffer[0] == 0x47 && magicBuffer[1] == 0x49 && magicBuffer[2] == 0x46 && magicBuffer[3] == 0x38,
        "image/webp" => magicBytesRead >= 12 && magicBuffer[0] == 0x52 && magicBuffer[1] == 0x49 && magicBuffer[2] == 0x46 && magicBuffer[3] == 0x46
                        && magicBuffer[8] == 0x57 && magicBuffer[9] == 0x45 && magicBuffer[10] == 0x42 && magicBuffer[11] == 0x50,
        _ => false
    };
    if (!validMagic)
        return Results.BadRequest(new { error = "File content does not match declared image type." });

    // Validate size (2MB)
    const long maxBytes = 2 * 1024 * 1024;
```

Note: IFormFile.OpenReadStream() returns a new stream each time it's called, so using a separate `magicStream` for the magic check does NOT interfere with the subsequent `imageStream` or `uploadStream` calls — those call `file.OpenReadStream()` fresh themselves.

## Important Notes
- The `IConfiguration config` is already injected in the avatar endpoint in Program.cs (it's a parameter of the MapPost lambda). So no changes to DI registration needed.
- For AvatarModerationService, `IConfiguration` must be added to the constructor signature. Check if the service is registered with AddScoped/AddSingleton in Program.cs; IConfiguration should be auto-resolved by DI.
- Do NOT modify any other code outside these specific changes.
- After all edits, run `dotnet build` from the project root to confirm 0 errors.
