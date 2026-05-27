using Amazon.CloudFront;
using Amazon.SecretsManager;
using Amazon.SecretsManager.Model;

namespace FortressAI.Web.Services;

public class CloudFrontSignedUrlService : ICloudFrontSignedUrlService
{
    private readonly string? _distributionDomain;
    private readonly string? _keyPairId;
    private readonly string? _privateKeySecretName;
    private readonly int _urlExpirySeconds;
    private readonly IAmazonSecretsManager _secrets;
    private readonly ILogger<CloudFrontSignedUrlService> _logger;
    private string? _cachedPem;
    private readonly SemaphoreSlim _keyLoadLock = new(1, 1);

    public bool IsConfigured { get; }

    public CloudFrontSignedUrlService(
        IConfiguration config,
        IAmazonSecretsManager secrets,
        ILogger<CloudFrontSignedUrlService> logger)
    {
        _secrets = secrets;
        _logger = logger;
        _distributionDomain = config["CloudFront:DistributionDomain"];
        _keyPairId = config["CloudFront:KeyPairId"];
        _privateKeySecretName = config["CloudFront:PrivateKeySecretName"];
        _urlExpirySeconds = int.TryParse(config["CloudFront:UrlExpirySeconds"], out var s) ? s : 3600;
        IsConfigured = !string.IsNullOrEmpty(_distributionDomain)
                       && !string.IsNullOrEmpty(_keyPairId)
                       && !string.IsNullOrEmpty(_privateKeySecretName);
    }

    public async Task<string?> GetSignedUrlAsync(string s3Key, int? expirySeconds = null)
    {
        if (!IsConfigured) return null;

        var pem = await EnsurePemLoadedAsync();
        if (pem == null) return null;

        var expiry = expirySeconds ?? _urlExpirySeconds;
        var expiresAt = DateTime.UtcNow.AddSeconds(expiry);

        try
        {
            using var reader = new StringReader(pem);
            var signedUrl = AmazonCloudFrontUrlSigner.GetCannedSignedURL(
                AmazonCloudFrontUrlSigner.Protocol.https,
                _distributionDomain!,
                reader,
                s3Key,
                _keyPairId!,
                expiresAt);
            return signedUrl;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to generate CloudFront signed URL for key {S3Key}", s3Key);
            return null;
        }
    }

    private async Task<string?> EnsurePemLoadedAsync()
    {
        if (_cachedPem != null) return _cachedPem;

        await _keyLoadLock.WaitAsync();
        try
        {
            if (_cachedPem != null) return _cachedPem; // double-check after lock

            var response = await _secrets.GetSecretValueAsync(new GetSecretValueRequest
            {
                SecretId = _privateKeySecretName
            });
            _cachedPem = response.SecretString;
            _logger.LogInformation("CloudFront private key loaded from Secrets Manager secret '{SecretName}'", _privateKeySecretName);
            return _cachedPem;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to load CloudFront private key from Secrets Manager secret '{SecretName}'", _privateKeySecretName);
            return null;
        }
        finally
        {
            _keyLoadLock.Release();
        }
    }
}
