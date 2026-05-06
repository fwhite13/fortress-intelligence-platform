using Amazon.BedrockDataAutomationRuntime;
using Amazon.BedrockDataAutomationRuntime.Model;
using Amazon.S3;
using Amazon.S3.Model;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace FortressAI.Web.Services;

/// <summary>
/// Runs Bedrock Data Automation (BDA) async processing on images already uploaded to S3.
/// Writes OCR + visual description as a .txt sidecar at {s3Key}-bda-text.txt.
/// All operations are non-fatal — logs warning on any failure.
/// </summary>
public class BdaProcessingService
{
    private readonly IAmazonS3 _s3;
    private readonly IConfiguration _config;
    private readonly ILogger<BdaProcessingService> _logger;

    private const string BucketName = "fortress-tools";

    // BDA Runtime client — us-east-1 (BDA only available in us-east-1)
    private AmazonBedrockDataAutomationRuntimeClient CreateBdaClient() =>
        new AmazonBedrockDataAutomationRuntimeClient(Amazon.RegionEndpoint.USEast1);

    // AWS-managed standard output profile ARN for BDA
    // See: https://docs.aws.amazon.com/bedrock/latest/userguide/bda-using-api.html
    // NOTE: This ARN is OMITTED intentionally — BDA defaults to standard output when not specified.
    // If the SDK requires it, the format is: arn:aws:bedrock:us-east-1::data-automation/aws-standard-output-profile/1.0.0
    // We pass null/omit DataAutomationProfileArn to let BDA use its default.

    public BdaProcessingService(IAmazonS3 s3, IConfiguration config, ILogger<BdaProcessingService> logger)
    {
        _s3 = s3;
        _config = config;
        _logger = logger;
    }

    /// <summary>
    /// Run BDA async processing on an image already in S3 at {s3Key}.
    /// On success: writes {s3Key}-bda-text.txt containing extracted OCR + description text.
    /// On failure: logs warning and returns false. Never throws.
    /// </summary>
    public async Task<bool> ProcessImageAsync(string s3Key, CancellationToken ct = default)
    {
        try
        {
            var inputUri  = $"s3://{BucketName}/{s3Key}";
            var outputPrefix = $"bda-output/{s3Key}/";
            var outputUri = $"s3://{BucketName}/{outputPrefix}";

            _logger.LogInformation("[BDA] Starting image processing for {S3Key}", s3Key);

            using var bdaClient = CreateBdaClient();

            // Invoke BDA async job
            var invokeReq = new InvokeDataAutomationAsyncRequest
            {
                InputConfiguration  = new InputConfiguration  { S3Uri = inputUri  },
                OutputConfiguration = new OutputConfiguration { S3Uri = outputUri },
                ClientToken = Guid.NewGuid().ToString("N")
                // DataAutomationProfileArn intentionally omitted — BDA uses standard default
            };

            var invokeResp = await bdaClient.InvokeDataAutomationAsyncAsync(invokeReq, ct);
            var invocationArn = invokeResp.InvocationArn;
            _logger.LogInformation("[BDA] Job submitted. InvocationArn={Arn}", invocationArn);

            // Poll for completion: up to 12 × 5 s = 60 s
            string? statusValue = null;
            for (int attempt = 1; attempt <= 12; attempt++)
            {
                await Task.Delay(5_000, ct);

                var statusResp = await bdaClient.GetDataAutomationStatusAsync(
                    new GetDataAutomationStatusRequest { InvocationArn = invocationArn }, ct);

                // AutomationJobStatus enum — .Value gives the string representation
                statusValue = statusResp.Status?.Value;
                _logger.LogInformation("[BDA] Poll {Attempt}/12: Status={Status}", attempt, statusValue);

                if (statusValue is "Success" or "ServiceError" or "ClientError")
                    break;
            }

            if (statusValue != "Success")
            {
                _logger.LogWarning("[BDA] Job did not succeed for {S3Key}: FinalStatus={Status}", s3Key, statusValue);
                return false;
            }

            // Find the output JSON under the output prefix
            var listResp = await _s3.ListObjectsV2Async(new ListObjectsV2Request
            {
                BucketName = BucketName,
                Prefix     = outputPrefix
            }, ct);

            var resultKey = listResp.S3Objects
                .Where(o => o.Key.EndsWith(".json", StringComparison.OrdinalIgnoreCase))
                .OrderByDescending(o => o.LastModified)
                .FirstOrDefault()?.Key;

            if (resultKey == null)
            {
                _logger.LogWarning("[BDA] No output JSON found under prefix {Prefix}", outputPrefix);
                return false;
            }

            // Read output JSON
            var getResp = await _s3.GetObjectAsync(
                new GetObjectRequest { BucketName = BucketName, Key = resultKey }, ct);

            string resultJson;
            using (var reader = new StreamReader(getResp.ResponseStream))
                resultJson = await reader.ReadToEndAsync(ct);

            // Extract text from BDA output
            var extractedText = ExtractTextFromBdaOutput(resultJson);
            if (string.IsNullOrWhiteSpace(extractedText))
            {
                _logger.LogWarning("[BDA] No text extracted from output JSON for {S3Key}", s3Key);
                return false;
            }

            // Write .txt sidecar
            var sidecarKey = $"{s3Key}-bda-text.txt";
            await _s3.PutObjectAsync(new PutObjectRequest
            {
                BucketName  = BucketName,
                Key         = sidecarKey,
                ContentBody = extractedText,
                ContentType = "text/plain"
            }, ct);

            _logger.LogInformation("[BDA] Sidecar written: {SidecarKey} ({Chars} chars)", sidecarKey, extractedText.Length);
            return true;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "[BDA] Image processing failed non-fatally for {S3Key}", s3Key);
            return false;
        }
    }

    /// <summary>
    /// Extract text from the BDA standard output JSON.
    /// BDA output structure (standard profile):
    ///   root["output_segments"][i]["standard_output"]["text"]            — OCR text
    ///   root["output_segments"][i]["standard_output"]["semantic_modality_output"]["description"] — visual description
    /// Fallback: root["content"] or root["text"] for simpler response shapes.
    /// If none match, serializes the entire JSON as plain text so something is always stored.
    /// </summary>
    private static string ExtractTextFromBdaOutput(string json)
    {
        try
        {
            using var doc = System.Text.Json.JsonDocument.Parse(json);
            var sb = new System.Text.StringBuilder();

            if (doc.RootElement.TryGetProperty("output_segments", out var segments))
            {
                foreach (var seg in segments.EnumerateArray())
                {
                    if (!seg.TryGetProperty("standard_output", out var stdOut)) continue;

                    if (stdOut.TryGetProperty("text", out var textEl) &&
                        textEl.ValueKind == System.Text.Json.JsonValueKind.String)
                        sb.AppendLine(textEl.GetString());

                    if (stdOut.TryGetProperty("semantic_modality_output", out var modal) &&
                        modal.TryGetProperty("description", out var desc) &&
                        desc.ValueKind == System.Text.Json.JsonValueKind.String)
                        sb.AppendLine(desc.GetString());
                }
            }
            else if (doc.RootElement.TryGetProperty("content", out var content) &&
                     content.ValueKind == System.Text.Json.JsonValueKind.String)
            {
                sb.AppendLine(content.GetString());
            }
            else if (doc.RootElement.TryGetProperty("text", out var text) &&
                     text.ValueKind == System.Text.Json.JsonValueKind.String)
            {
                sb.AppendLine(text.GetString());
            }
            else
            {
                // Fallback: store the raw JSON so the sidecar file is never empty
                sb.AppendLine(json);
            }

            return sb.ToString().Trim();
        }
        catch
        {
            return json; // worst case: store raw JSON
        }
    }
}
