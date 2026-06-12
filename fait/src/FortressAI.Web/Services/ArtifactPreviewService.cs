using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text;
using Amazon.S3;
using Amazon.S3.Model;
using FortressAI.Web.Data;
using Microsoft.EntityFrameworkCore;

namespace FortressAI.Web.Services;

/// <summary>
/// Provides HMAC-SHA256 token generation and validation for artifact preview URLs.
/// Token format: base64url(HMAC-SHA256("{artifactId}:{userId}:{expires}"))
/// where expires is a Unix timestamp (seconds).
/// Token validity: 15 minutes from generation.
/// PREVIEW_TOKEN_SECRET env var is the HMAC key.
/// </summary>
public class ArtifactPreviewService
{
    private readonly string _secret;
    private readonly ILogger<ArtifactPreviewService> _logger;
    private readonly IDbContextFactory<AppDbContext> _dbFactory;
    private readonly IConfiguration _config;
    private readonly IAmazonS3 _s3;
    private readonly IXlsxPresizerService _xlsxPresizerService;
    private const int TokenValiditySeconds = 900; // 15 minutes

    public ArtifactPreviewService(IConfiguration config, ILogger<ArtifactPreviewService> logger, IDbContextFactory<AppDbContext> dbFactory, IAmazonS3 s3, IXlsxPresizerService xlsxPresizerService)
    {
        _secret = config["PREVIEW_TOKEN_SECRET"] ?? "";
        _logger = logger;
        _dbFactory = dbFactory;
        _config = config;
        _s3 = s3;
        _xlsxPresizerService = xlsxPresizerService;
        if (string.IsNullOrWhiteSpace(_secret))
            throw new InvalidOperationException(
                "PREVIEW_TOKEN_SECRET is not configured. This setting is required. " +
                "Set PREVIEW_TOKEN_SECRET in your ECS task definition or environment.");
    }

    /// <summary>
    /// Generates a preview token for the given artifact and user.
    /// Returns (token, expiresUnixTimestamp).
    /// </summary>
    public (string token, long expires) GenerateToken(Guid artifactId, Guid userId)
    {
        var expires = DateTimeOffset.UtcNow.AddSeconds(TokenValiditySeconds).ToUnixTimeSeconds();
        var payload = $"{artifactId}:{userId}:{expires}";
        var token = ComputeHmac(payload);
        return (token, expires);
    }

    /// <summary>
    /// Checks whether a preview is ready for the given artifact and user.
    /// Returns (isReady, previewUrl) — previewUrl is null if not ready.
    /// Previewurl is a relative path like /api/artifacts/{id}/preview?token=...
    /// </summary>
    public async Task<(bool IsReady, string? PreviewUrl)> GetPreviewStatusAsync(Guid artifactId, Guid userId)
    {
        await using var db = await _dbFactory.CreateDbContextAsync();
        var upload = await db.WorkspaceUploads
            .FirstOrDefaultAsync(u => u.Id == artifactId && u.UserId == userId);
        if (upload == null || string.IsNullOrEmpty(upload.PreviewS3Key))
            return (false, null);
        var (token, expires) = GenerateToken(artifactId, userId);
        var previewUrl = $"/api/artifacts/{artifactId}/preview?token={Uri.EscapeDataString(token)}&expires={expires}&preview=true";
        return (true, previewUrl);
    }

    /// <summary>
    /// Validates a preview token. Returns true if valid and not expired.
    /// </summary>
    public bool ValidateToken(Guid artifactId, Guid userId, string token, long expires)
    {
        // Check expiry
        var now = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        if (now > expires)
        {
            _logger.LogDebug("[ArtifactPreview] Token expired for artifact {ArtifactId}", artifactId);
            return false;
        }

        // Recompute HMAC and compare
        var payload = $"{artifactId}:{userId}:{expires}";
        var expected = ComputeHmac(payload);
        return CryptographicEquals(expected, token);
    }

    /// <summary>
    /// Triggers PPTX → PDF conversion via the converter service, or returns the cached key.
    /// Accepts IHttpClientFactory as a parameter to avoid a constructor dependency.
    /// </summary>
    public async Task<string?> ConvertPptxAsync(Guid artifactId, string s3Key, Guid userId, IHttpClientFactory httpClientFactory)
    {
        await using var db = await _dbFactory.CreateDbContextAsync();
        var upload = await db.WorkspaceUploads.FirstOrDefaultAsync(u => u.Id == artifactId && u.UserId == userId);

        if (upload != null && !string.IsNullOrEmpty(upload.PreviewS3Key))
            return upload.PreviewS3Key;

        var converterBaseRaw = _config["CONVERTER_BASE_URL"];
        if (string.IsNullOrEmpty(converterBaseRaw))
            _logger.LogWarning("[ArtifactPreview] CONVERTER_BASE_URL not set — falling back to localhost. PPTX conversion may fail in production.");
        var converterBase = converterBaseRaw ?? "http://localhost:3001";
        var converterApiKey = _config["CONVERTER_API_KEY"];
        using var client = httpClientFactory.CreateClient("HarnessClient");
        client.Timeout = TimeSpan.FromSeconds(90); // ADO#4908: cap converter wait to avoid indefinite spin
        if (!string.IsNullOrEmpty(converterApiKey))
            client.DefaultRequestHeaders.Authorization =
                new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", converterApiKey);

        var body = new
        {
            artifactId = artifactId.ToString(),
            s3Key,
            userId = userId.ToString(),
            outputBucket = _config["WORKSPACE_S3_BUCKET"] ?? "fortress-user-workspaces"
        };
        var resp = await client.PostAsJsonAsync($"{converterBase}/convert", body);
        if (!resp.IsSuccessStatusCode)
        {
            var errBody = await resp.Content.ReadAsStringAsync();
            _logger.LogWarning("[preview] [pptx] PPTX converter returned {Status} for artifact {Id}: {Body}", resp.StatusCode, artifactId, errBody);
            return null;
        }

        var result = await resp.Content.ReadFromJsonAsync<ConvertPptxResult>();
        if (result?.PreviewS3Key != null && upload != null)
        {
            upload.PreviewS3Key = result.PreviewS3Key;
            await db.SaveChangesAsync();
        }
        return result?.PreviewS3Key;
    }

    private record ConvertPptxResult([property: System.Text.Json.Serialization.JsonPropertyName("previewS3Key")] string? PreviewS3Key);

    /// <summary>
    /// Triggers XLSX → PDF conversion via the presizer pipeline + converter service, or returns the cached key and sheet names.
    /// Accepts IHttpClientFactory as a parameter to avoid a constructor dependency.
    /// </summary>
    public async Task<(string? previewS3Key, string[] sheetNames)> ConvertXlsxAsync(Guid artifactId, string s3Key, Guid userId, IHttpClientFactory httpClientFactory)
    {
        await using var db = await _dbFactory.CreateDbContextAsync();
        var upload = await db.WorkspaceUploads.FirstOrDefaultAsync(u => u.Id == artifactId && u.UserId == userId);

        if (upload != null && !string.IsNullOrEmpty(upload.PreviewS3Key))
            return (upload.PreviewS3Key, Array.Empty<string>());

        if (upload == null)
            return (null, Array.Empty<string>());

        var bucket = _config["WORKSPACE_S3_BUCKET"] ?? "fortress-user-workspaces";

        try
        {
            // Step 1: Download XLSX from S3
            _logger.LogInformation("[preview] [xlsx] Downloading XLSX from S3 key={Key} for artifact {Id}", s3Key, artifactId);
            var s3Response = await _s3.GetObjectAsync(bucket, s3Key);
            using var xlsxStream = new MemoryStream();
            await s3Response.ResponseStream.CopyToAsync(xlsxStream);
            xlsxStream.Position = 0;

            // Step 2: Presize in-process via XlsxPresizerService
            _logger.LogInformation("[preview] [xlsx] Presizing XLSX for artifact {Id}", artifactId);
            var presizeResult = await _xlsxPresizerService.PresizeAsync(xlsxStream);
            _logger.LogInformation("[preview] [xlsx] Presized — {SheetCount} sheet(s): {Names}",
                presizeResult.SheetNames.Length, string.Join(", ", presizeResult.SheetNames));

            // Step 3: Upload presized XLSX to temp S3 key
            var presizedKey = $"previews/tmp/{artifactId}-presized.xlsx";
            _logger.LogInformation("[preview] [xlsx] Uploading presized XLSX to s3://{Bucket}/{Key}", bucket, presizedKey);
            using var presizedStream = new MemoryStream(presizeResult.Bytes);
            await _s3.PutObjectAsync(new PutObjectRequest
            {
                BucketName = bucket,
                Key = presizedKey,
                InputStream = presizedStream,
                ContentType = "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet"
            });

            // Step 4: Call pptx-converter /convert with presized XLSX
            var converterBaseRaw = _config["CONVERTER_BASE_URL"];
            if (string.IsNullOrEmpty(converterBaseRaw))
                _logger.LogWarning("[preview] [xlsx] CONVERTER_BASE_URL not set — falling back to localhost. XLSX conversion may fail in production.");
            var converterBase = converterBaseRaw ?? "http://localhost:3001";
            var converterApiKey = _config["CONVERTER_API_KEY"];
            using var client = httpClientFactory.CreateClient("HarnessClient");
            client.Timeout = TimeSpan.FromSeconds(90);
            if (!string.IsNullOrEmpty(converterApiKey))
                client.DefaultRequestHeaders.Authorization =
                    new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", converterApiKey);

            var body = new
            {
                artifactId = artifactId.ToString(),
                s3Key = presizedKey,
                userId = userId.ToString(),
                outputBucket = bucket
            };

            _logger.LogInformation("[preview] [xlsx] Calling pptx-converter /convert for artifact {Id}", artifactId);
            var resp = await client.PostAsJsonAsync($"{converterBase}/convert", body);
            if (!resp.IsSuccessStatusCode)
            {
                var errBody = await resp.Content.ReadAsStringAsync();
                _logger.LogWarning("[preview] [xlsx] Converter returned {Status} for artifact {Id}: {Body}", resp.StatusCode, artifactId, errBody);
                return (null, Array.Empty<string>());
            }

            var result = await resp.Content.ReadFromJsonAsync<ConvertPptxResult>();
            if (result?.PreviewS3Key != null)
            {
                upload.PreviewS3Key = result.PreviewS3Key;
                await db.SaveChangesAsync();
                _logger.LogInformation("[preview] [xlsx] Done — previewS3Key={Key} for artifact {Id}", result.PreviewS3Key, artifactId);
            }

            return (result?.PreviewS3Key, presizeResult.SheetNames);
        }
        catch (Amazon.S3.AmazonS3Exception s3Ex)
        {
            _logger.LogError(s3Ex, "[preview] [xlsx] S3 error for artifact {Id}: {Code}", artifactId, s3Ex.ErrorCode);
            return (null, Array.Empty<string>());
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[preview] [xlsx] Unexpected error for artifact {Id}", artifactId);
            return (null, Array.Empty<string>());
        }
    }

    private string ComputeHmac(string payload)
    {
        var keyBytes = Encoding.UTF8.GetBytes(_secret);
        var msgBytes = Encoding.UTF8.GetBytes(payload);
        using var hmac = new HMACSHA256(keyBytes);
        var hash = hmac.ComputeHash(msgBytes);
        // Use base64url (no padding, URL-safe)
        return Convert.ToBase64String(hash)
            .TrimEnd('=')
            .Replace('+', '-')
            .Replace('/', '_');
    }

    private static bool CryptographicEquals(string a, string b)
    {
        // Constant-time comparison to prevent timing attacks
        var aBytes = Encoding.UTF8.GetBytes(a);
        var bBytes = Encoding.UTF8.GetBytes(b);
        if (aBytes.Length != bBytes.Length) return false;
        return CryptographicOperations.FixedTimeEquals(aBytes, bBytes);
    }
}
