# CC Brief — ADO#3092: Avatar NSFW Check on Upload via Bedrock Vision Model

## Context
Working in: `/home/fredw/projects/fip/fait-v2/src/FortressAI.V2.Web/`

This app is an ASP.NET 8 Blazor Server app using Minimal API endpoints in `Program.cs`.
- Uses `IAmazonBedrockRuntime` (already registered via `AddAWSService<IAmazonBedrockRuntime>()`)
- Uses `IAmazonS3` (already registered)
- `GetUserId(ctx)` helper at bottom of Program.cs extracts user OID from claims
- All existing services follow `IXxxService` / `XxxService` pattern with `AddScoped<>` registration
- `InvokeModelAsync` pattern used in `ConversationTitleService.cs` for Bedrock calls
- Bedrock model: `us.anthropic.claude-haiku-4-5-20251001-v1:0` (same as other services)
- S3 bucket: `config["AWS:WorkspaceBucket"] ?? config["AWS:S3Bucket"] ?? "fortress-user-workspaces"`

## Task 1: Add `AvatarUrl` column to `User` entity + EF migration

**File: `Data/Models/User.cs`**

Add after `UpdatedAt` property (before the navigation properties):
```csharp
[Column("avatar_url")]
[MaxLength(1000)]
public string? AvatarUrl { get; set; }
```

**File: `Data/FaitV2DbContext.cs`**

In `OnModelCreating`, inside the `modelBuilder.Entity<User>` block, after `entity.Property(e => e.UpdatedAt)...` line, add:
```csharp
entity.Property(e => e.AvatarUrl).HasColumnName("avatar_url").HasMaxLength(1000);
```

**Create migration file:**

Create: `Data/Migrations/20260509100000_AddAvatarUrlToUser.cs`

```csharp
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace FortressAI.V2.Web.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddAvatarUrlToUser : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "avatar_url",
                table: "users",
                type: "varchar(1000)",
                maxLength: 1000,
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "avatar_url",
                table: "users");
        }
    }
}
```

**Update the snapshot:** `Data/Migrations/FaitV2DbContextModelSnapshot.cs`

In the snapshot file, find the block for the `users` table (it will have a `modelBuilder.Entity("FortressAI.V2.Web.Data.Models.User"` or similar block). Add the `avatar_url` column property alongside the other columns like `updated_at`. Add:
```csharp
b.Property<string>("AvatarUrl")
    .HasMaxLength(1000)
    .HasColumnType("varchar(1000)")
    .HasColumnName("avatar_url");
```
Place it alphabetically or after the `UpdatedAt` property in the snapshot block.

## Task 2: Create `IAvatarModerationService` + `AvatarModerationService`

**Create file: `Services/AvatarModerationService.cs`**

```csharp
using Amazon.BedrockRuntime;
using Amazon.BedrockRuntime.Model;
using System.Text.Json;

namespace FortressAI.V2.Web.Services;

public interface IAvatarModerationService
{
    Task<AvatarModerationResult> CheckImageAsync(Stream imageStream, string contentType, CancellationToken ct = default);
}

public record AvatarModerationResult(bool IsAllowed, string? Reason = null);

public class AvatarModerationService : IAvatarModerationService
{
    private const string ModerationModel = "us.anthropic.claude-haiku-4-5-20251001-v1:0";

    private readonly IAmazonBedrockRuntime _bedrock;
    private readonly ILogger<AvatarModerationService> _logger;

    public AvatarModerationService(IAmazonBedrockRuntime bedrock, ILogger<AvatarModerationService> logger)
    {
        _bedrock = bedrock;
        _logger = logger;
    }

    public async Task<AvatarModerationResult> CheckImageAsync(Stream imageStream, string contentType, CancellationToken ct = default)
    {
        try
        {
            // Read image bytes
            using var ms = new MemoryStream();
            await imageStream.CopyToAsync(ms, ct);
            var imageBytes = ms.ToArray();
            var base64Image = Convert.ToBase64String(imageBytes);

            // Map content type to Bedrock image format
            var mediaType = contentType.ToLowerInvariant() switch
            {
                "image/jpeg" => "image/jpeg",
                "image/jpg"  => "image/jpeg",
                "image/png"  => "image/png",
                "image/gif"  => "image/gif",
                "image/webp" => "image/webp",
                _            => "image/jpeg"
            };

            var body = JsonSerializer.Serialize(new
            {
                anthropic_version = "bedrock-2023-05-31",
                max_tokens = 100,
                system = "You are a content moderation system. Respond with only 'SAFE' or 'UNSAFE: {reason}' for the following image.",
                messages = new[]
                {
                    new
                    {
                        role = "user",
                        content = new object[]
                        {
                            new
                            {
                                type = "image",
                                source = new
                                {
                                    type = "base64",
                                    media_type = mediaType,
                                    data = base64Image
                                }
                            },
                            new
                            {
                                type = "text",
                                text = "Is this image safe for use as a profile avatar in a professional business application?"
                            }
                        }
                    }
                }
            });

            var response = await _bedrock.InvokeModelAsync(new InvokeModelRequest
            {
                ModelId = ModerationModel,
                ContentType = "application/json",
                Accept = "application/json",
                Body = new MemoryStream(System.Text.Encoding.UTF8.GetBytes(body))
            }, ct);

            using var reader = new StreamReader(response.Body);
            var json = await reader.ReadToEndAsync(ct);
            using var doc = JsonDocument.Parse(json);
            var text = doc.RootElement
                .GetProperty("content")[0]
                .GetProperty("text")
                .GetString()
                ?.Trim() ?? string.Empty;

            if (text.StartsWith("UNSAFE", StringComparison.OrdinalIgnoreCase))
            {
                var reason = text.Length > 7 ? text[7..].TrimStart(':', ' ') : "Content not appropriate for a profile avatar";
                return new AvatarModerationResult(false, reason);
            }

            return new AvatarModerationResult(true);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Avatar moderation check failed — failing open to allow upload");
            return new AvatarModerationResult(true); // fail open
        }
    }
}
```

## Task 3: Register service + add endpoint in `Program.cs`

### 3a. Register the service

After the line:
```
builder.Services.AddScoped<IUserService, UserService>();
```

Add:
```csharp
builder.Services.AddScoped<IAvatarModerationService, AvatarModerationService>();
```

### 3b. Add avatar upload endpoint

After the `/api/workspace/upload` endpoint block (the one ending with `.RequireAuthorization();`), add the following new endpoint **before** the Blazor components mapping:

```csharp
// Avatar upload with NSFW moderation
app.MapPost("/api/profile/avatar", async (
    HttpContext httpContext,
    IAvatarModerationService moderation,
    IAmazonS3 s3,
    IDbContextFactory<FaitV2DbContext> dbFactory,
    IConfiguration config,
    ILogger<Program> logger,
    CancellationToken ct) =>
{
    var userId = GetUserId(httpContext);
    if (userId == null) return Results.Unauthorized();

    if (!httpContext.Request.HasFormContentType)
        return Results.BadRequest(new { error = "multipart/form-data required" });

    var form = await httpContext.Request.ReadFormAsync(ct);
    var file = form.Files.FirstOrDefault();
    if (file == null)
        return Results.BadRequest(new { error = "No file provided" });

    // Validate MIME type
    var allowedTypes = new[] { "image/jpeg", "image/jpg", "image/png", "image/webp", "image/gif" };
    var mimeType = file.ContentType?.ToLowerInvariant() ?? string.Empty;
    if (!allowedTypes.Contains(mimeType))
        return Results.BadRequest(new { error = "Only image files are accepted (jpeg, png, webp, gif)" });

    // Validate size (2MB)
    const long maxBytes = 2 * 1024 * 1024;
    if (file.Length > maxBytes)
        return Results.BadRequest(new { error = "Image must be 2MB or smaller" });

    // Run NSFW moderation
    using var imageStream = file.OpenReadStream();
    var modResult = await moderation.CheckImageAsync(imageStream, mimeType, ct);
    if (!modResult.IsAllowed)
    {
        logger.LogWarning("AvatarUpload rejected for userId={UserId}: {Reason}", userId, modResult.Reason);
        return Results.BadRequest(new { error = $"Image rejected: {modResult.Reason}" });
    }

    // Upload to S3
    var ext = Path.GetExtension(file.FileName).ToLowerInvariant();
    if (string.IsNullOrEmpty(ext)) ext = mimeType switch
    {
        "image/jpeg" or "image/jpg" => ".jpg",
        "image/png"  => ".png",
        "image/webp" => ".webp",
        "image/gif"  => ".gif",
        _ => ".jpg"
    };
    var s3Key = $"avatars/{userId}/{Guid.NewGuid()}{ext}";
    var bucket = config["AWS:WorkspaceBucket"] ?? config["AWS:S3Bucket"] ?? "fortress-user-workspaces";

    using var uploadStream = file.OpenReadStream();
    await s3.PutObjectAsync(new Amazon.S3.Model.PutObjectRequest
    {
        BucketName = bucket,
        Key = s3Key,
        InputStream = uploadStream,
        ContentType = mimeType,
        AutoCloseStream = false,
        CannedACL = Amazon.S3.S3CannedACL.PublicRead
    }, ct);

    var avatarUrl = $"https://{bucket}.s3.amazonaws.com/{s3Key}";

    // Update user record
    try
    {
        using var cts2 = CancellationTokenSource.CreateLinkedTokenSource(ct);
        cts2.CancelAfter(TimeSpan.FromSeconds(5));
        await using var db = await dbFactory.CreateDbContextAsync(cts2.Token);
        var user = await db.Users.FirstOrDefaultAsync(u => u.EntraOid == userId, cts2.Token);
        if (user != null)
        {
            user.AvatarUrl = avatarUrl;
            user.UpdatedAt = DateTime.UtcNow;
            await db.SaveChangesAsync(cts2.Token);
        }
    }
    catch (Exception ex)
    {
        logger.LogWarning(ex, "AvatarUpload: failed to update user record for userId={UserId}", userId);
        // Don't fail the request — S3 upload succeeded
    }

    logger.LogInformation("AvatarUpload: userId={UserId} s3Key={S3Key}", userId, s3Key);
    return Results.Ok(new { avatarUrl });
}).RequireAuthorization();
```

**Important:** The endpoint uses `IDbContextFactory<FaitV2DbContext>` and `FirstOrDefaultAsync` — make sure the using directive `using Microsoft.EntityFrameworkCore;` is already at the top of Program.cs (it should be). Also verify `using FortressAI.V2.Web.Data;` is present (it should be from the existing workspace upload).

## Task 4: Build verification

After making all changes, run:
```bash
cd /home/fredw/projects/fip/fait-v2/src/FortressAI.V2.Web && dotnet build
```

Report the exact build output (success or errors). If there are errors, fix them.

## Task 5: Write build report

Create file: `/home/fredw/projects/fip/fait-v2/pipeline/ADO3092-BUILD-REPORT.md`

Document:
- What was built
- Files changed (list each)
- Migration created
- Acceptance criteria verification
- Build result (0 errors confirmed)
