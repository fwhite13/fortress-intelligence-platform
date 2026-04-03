using System.Runtime.CompilerServices;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using Amazon;
using Amazon.BedrockRuntime;
using Amazon.BedrockRuntime.Model;
using Amazon.Runtime.Documents;
using FortressAI.Shared.Models;
using FortressAI.Web.Services.Mcp;
using Microsoft.Extensions.Configuration;

namespace FortressAI.Web.Services;

public class BedrockService : IDisposable
{
    private readonly AmazonBedrockRuntimeClient _client;
    private readonly ILogger<BedrockService> _logger;
    private readonly IConfiguration _config;

    public BedrockService(ILogger<BedrockService> logger, IConfiguration config)
    {
        _logger = logger;
        _config = config;
        _client = new AmazonBedrockRuntimeClient(new AmazonBedrockRuntimeConfig
        {
            RegionEndpoint = RegionEndpoint.USEast1
        });
    }

    public async IAsyncEnumerable<StreamChunk> StreamChatAsync(
        List<MessageDto> messages,
        string? systemPrompt,
        string modelId,
        int maxTokens = 4096,
        double temperature = 0.7)
    {
        var bedrockModelId = ModelInfo.GetModel(modelId).BedrockModelId;

        // Extract PDF and image data URIs from system prompt before building request
        var pdfBase64List = new List<string>();
        var imageList = new List<(string mediaType, string base64Data)>();
        var cleanedSystemPrompt = systemPrompt;
        if (!string.IsNullOrEmpty(systemPrompt))
        {
            (cleanedSystemPrompt, pdfBase64List, imageList) = ExtractMediaDataUris(systemPrompt);
            if (pdfBase64List.Count > 0 || imageList.Count > 0)
            {
                _logger.LogInformation("[BEDROCK_API] Extracted {PdfCount} PDF(s) and {ImageCount} image(s) from system prompt for user message injection",
                    pdfBase64List.Count, imageList.Count);
            }
        }

        // Build request as JSON object directly for multimodal support
        var requestObj = new JsonObject
        {
            ["anthropic_version"] = "bedrock-2023-05-31",
            ["max_tokens"] = maxTokens,
            ["temperature"] = temperature
        };

        // Build messages array with multimodal support
        var messagesArray = new JsonArray();
        for (int i = 0; i < messages.Count; i++)
        {
            var msg = messages[i];
            var isLastUserMessage = msg.Role == "user" && !messages.Skip(i + 1).Any(m => m.Role == "user");

            if (isLastUserMessage && (pdfBase64List.Count > 0 || imageList.Count > 0))
            {
                // Inject PDF document blocks and image blocks into the last user message
                var contentArray = new JsonArray();

                int docIndex = 1;
                foreach (var pdfBase64 in pdfBase64List)
                {
                    contentArray.Add(new JsonObject
                    {
                        ["type"] = "document",
                        ["title"] = $"pdf_document_{docIndex}",
                        ["source"] = new JsonObject
                        {
                            ["type"] = "base64",
                            ["media_type"] = "application/pdf",
                            ["data"] = pdfBase64
                        }
                    });
                    _logger.LogInformation("[BEDROCK_API] Added PDF document block {Index}: base64Length={Length}",
                        docIndex, pdfBase64.Length);
                    docIndex++;
                }

                int imgIndex = 1;
                foreach (var (mediaType, base64Data) in imageList)
                {
                    contentArray.Add(new JsonObject
                    {
                        ["type"] = "image",
                        ["source"] = new JsonObject
                        {
                            ["type"] = "base64",
                            ["media_type"] = mediaType,
                            ["data"] = base64Data
                        }
                    });
                    _logger.LogInformation("[BEDROCK_API] Added image block {Index}: mediaType={MediaType}, base64Length={Length}",
                        imgIndex, mediaType, base64Data.Length);
                    imgIndex++;
                }

                contentArray.Add(new JsonObject
                {
                    ["type"] = "text",
                    ["text"] = msg.Content
                });

                messagesArray.Add(new JsonObject
                {
                    ["role"] = msg.Role,
                    ["content"] = contentArray
                });
            }
            else
            {
                messagesArray.Add(new JsonObject
                {
                    ["role"] = msg.Role,
                    ["content"] = msg.Content
                });
            }
        }
        // Sonnet 4.6+ does not support assistant prefill — strip trailing assistant messages.
        while (messagesArray.Count > 0 &&
               messagesArray[^1] is JsonObject lastObj &&
               lastObj["role"]?.GetValue<string>() == "assistant")
        {
            _logger.LogWarning("Stripping trailing assistant message from JSON messages array (prefill not supported in Sonnet 4.6+).");
            messagesArray.RemoveAt(messagesArray.Count - 1);
        }

        requestObj["messages"] = messagesArray;

        // Handle system prompt - may contain image data URIs that need multimodal format
        if (!string.IsNullOrEmpty(cleanedSystemPrompt))
        {
            var systemBlocks = BuildSystemContentBlocks(cleanedSystemPrompt);
            requestObj["system"] = systemBlocks;
            _logger.LogInformation("[BEDROCK_API] System prompt included: {Length} chars, {BlockCount} blocks",
                cleanedSystemPrompt.Length, systemBlocks.Count);
        }
        else
        {
            _logger.LogInformation("[BEDROCK_API] No system prompt provided");
        }

        var json = requestObj.ToJsonString(new JsonSerializerOptions
        {
            DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
        });

        _logger.LogDebug("Bedrock request to {Model}: {Length} bytes", bedrockModelId, json.Length);

        var request = new InvokeModelWithResponseStreamRequest
        {
            ModelId = bedrockModelId,
            ContentType = "application/json",
            Accept = "application/json",
            Body = new MemoryStream(Encoding.UTF8.GetBytes(json))
        };

        InvokeModelWithResponseStreamResponse? response = null;
        string? apiError = null;
        try
        {
            response = await _client.InvokeModelWithResponseStreamAsync(request);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Bedrock API error for model {Model}", bedrockModelId);
            apiError = $"API Error: {ex.Message}";
        }

        if (apiError != null)
        {
            yield return new StreamChunk { Type = "error", Text = apiError };
            yield break;
        }

        int totalInputTokens = 0;
        int totalOutputTokens = 0;

        foreach (var ev in response!.Body)
        {
            if (ev is PayloadPart payloadPart)
            {
                var eventJson = Encoding.UTF8.GetString(payloadPart.Bytes.ToArray());
                var eventData = JsonSerializer.Deserialize<BedrockStreamEvent>(eventJson, new JsonSerializerOptions
                {
                    PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower
                });

                if (eventData == null) continue;

                switch (eventData.Type)
                {
                    case "content_block_delta":
                        if (eventData.Delta?.Type == "text_delta" && eventData.Delta.Text != null)
                        {
                            yield return new StreamChunk { Type = "text", Text = eventData.Delta.Text };
                        }
                        break;

                    case "message_delta":
                        if (eventData.Usage != null)
                        {
                            totalOutputTokens = eventData.Usage.OutputTokens;
                        }
                        break;

                    case "message_start":
                        if (eventData.Message?.Usage != null)
                        {
                            totalInputTokens = eventData.Message.Usage.InputTokens;
                        }
                        break;

                    case "message_stop":
                        yield return new StreamChunk
                        {
                            Type = "done",
                            InputTokens = totalInputTokens,
                            OutputTokens = totalOutputTokens
                        };
                        break;
                }
            }
        }
    }

    /// <summary>
    /// Extracts PDF and image data URIs from the system prompt and returns the cleaned prompt
    /// plus lists of base64-encoded media data (without the data URI prefix).
    /// Both PDFs and images are routed to user messages as content blocks, not the system prompt.
    /// </summary>
    private (string cleanedPrompt, List<string> pdfBase64List, List<(string mediaType, string base64Data)> imageList) ExtractMediaDataUris(string systemPrompt)
    {
        var pdfBase64List = new List<string>();
        var imageList = new List<(string mediaType, string base64Data)>();
        var lines = systemPrompt.Split('\n');
        var cleanedLines = new List<string>();

        foreach (var line in lines)
        {
            var trimmed = line.TrimStart();
            if (trimmed.StartsWith("data:application/pdf;base64,"))
            {
                var base64Data = trimmed.Substring("data:application/pdf;base64,".Length);
                pdfBase64List.Add(base64Data);
                _logger.LogInformation("[BEDROCK_API] Extracted PDF data URI from system prompt: base64Length={Length}", base64Data.Length);
            }
            else if (trimmed.StartsWith("data:image/") && trimmed.Contains(";base64,"))
            {
                // Parse the data URI to get media type and base64 data
                var semiIdx = trimmed.IndexOf(';');
                var mediaType = trimmed.Substring(5, semiIdx - 5); // skip "data:" to get e.g. "image/png"
                var base64Data = trimmed.Substring(trimmed.IndexOf(',') + 1);
                imageList.Add((mediaType, base64Data));
                _logger.LogInformation("[BEDROCK_API] Extracted image data URI from system prompt: mediaType={MediaType}, base64Length={Length}",
                    mediaType, base64Data.Length);
            }
            else
            {
                cleanedLines.Add(line);
            }
        }

        var cleaned = string.Join('\n', cleanedLines).Trim();
        return (cleaned, pdfBase64List, imageList);
    }

    /// <summary>
    /// Builds system content blocks from a system prompt string.
    /// Returns a single text block. Image and PDF data URIs are extracted earlier
    /// via ExtractMediaDataUris and routed to user messages instead.
    /// </summary>
    private JsonArray BuildSystemContentBlocks(string systemPrompt)
    {
        var blocks = new JsonArray();
        blocks.Add(new JsonObject { ["type"] = "text", ["text"] = systemPrompt });
        return blocks;
    }

    /// <summary>
    /// Invokes Claude synchronously and returns the full text response as a string.
    /// Used for classification, summarization, and other non-streaming tasks.
    /// </summary>
    public async Task<string> InvokeClaudeAsync(string prompt, int maxTokens = 1000, string? systemPrompt = null,
        string? modelId = null)
    {
        modelId ??= _config.GetValue<string>("Bedrock:InvokeModelId", "us.anthropic.claude-sonnet-4-5-20250929-v1:0")!;
        var requestObj = new JsonObject
        {
            ["anthropic_version"] = "bedrock-2023-05-31",
            ["max_tokens"] = maxTokens,
            ["messages"] = new JsonArray
            {
                new JsonObject
                {
                    ["role"] = "user",
                    ["content"] = prompt
                }
            }
        };

        if (!string.IsNullOrEmpty(systemPrompt))
        {
            requestObj["system"] = systemPrompt;
        }

        var json = requestObj.ToJsonString();
        var request = new InvokeModelRequest
        {
            ModelId = modelId,
            ContentType = "application/json",
            Accept = "application/json",
            Body = new MemoryStream(Encoding.UTF8.GetBytes(json))
        };

        var response = await _client.InvokeModelAsync(request);
        var responseJson = await new StreamReader(response.Body).ReadToEndAsync();

        using var doc = System.Text.Json.JsonDocument.Parse(responseJson);
        var content = doc.RootElement
            .GetProperty("content")[0]
            .GetProperty("text")
            .GetString();

        return content ?? string.Empty;
    }

    /// <summary>
    /// Generates a short conversation title using Claude Haiku (non-streaming).
    /// Used for cheap, fast title generation after the first exchange.
    /// </summary>
    public async Task<string?> GenerateTitleAsync(string prompt)
    {
        var requestObj = new JsonObject
        {
            ["anthropic_version"] = "bedrock-2023-05-31",
            ["max_tokens"] = 20,
            ["temperature"] = 0.3,
            ["messages"] = new JsonArray
            {
                new JsonObject
                {
                    ["role"] = "user",
                    ["content"] = prompt
                }
            }
        };

        var json = requestObj.ToJsonString();
        var request = new InvokeModelRequest
        {
            ModelId = _config.GetValue<string>("Bedrock:TitleModelId", "us.anthropic.claude-sonnet-4-6")!,
            ContentType = "application/json",
            Accept = "application/json",
            Body = new MemoryStream(Encoding.UTF8.GetBytes(json))
        };

        try
        {
            var response = await _client.InvokeModelAsync(request);
            var responseJson = await new StreamReader(response.Body).ReadToEndAsync();

            using var doc = System.Text.Json.JsonDocument.Parse(responseJson);
            var content = doc.RootElement
                .GetProperty("content")[0]
                .GetProperty("text")
                .GetString();

            return content?.Trim();
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "GenerateTitleAsync failed");
            return null;
        }
    }

    /// <summary>
    /// Streams a chat response using the Bedrock Converse API with optional tool use support.
    /// Handles tool_use_start, tool_input_delta, and done events in addition to text.
    /// </summary>
    public async IAsyncEnumerable<StreamChunk> StreamChatWithToolsAsync(
        List<MessageDto> messages,
        string? systemPrompt,
        string modelId,
        List<BedrockToolSpec>? tools = null,
        int maxTokens = 4096,
        double temperature = 0.7,
        [EnumeratorCancellation] CancellationToken ct = default)
    {
        var bedrockModelId = ModelInfo.GetModel(modelId).BedrockModelId;

        // Extract PDF and image data URIs from system prompt before building request (mirrors StreamChatAsync)
        var pdfBase64List = new List<string>();
        var imageList = new List<(string mediaType, string base64Data)>();
        var cleanedSystemPrompt = systemPrompt;
        if (!string.IsNullOrEmpty(systemPrompt))
        {
            (cleanedSystemPrompt, pdfBase64List, imageList) = ExtractMediaDataUris(systemPrompt);
            if (pdfBase64List.Count > 0 || imageList.Count > 0)
            {
                _logger.LogInformation("[BEDROCK_API] (tools) Extracted {PdfCount} PDF(s) and {ImageCount} image(s) from system prompt for user message injection",
                    pdfBase64List.Count, imageList.Count);
            }
        }

        // Build messages and inject image/PDF blocks into the last user message
        var converseMessages = BuildConverseMessages(messages);
        if (pdfBase64List.Count > 0 || imageList.Count > 0)
        {
            // Find the last user message and prepend image/PDF content blocks
            var lastUserMsg = converseMessages.LastOrDefault(m => m.Role == Amazon.BedrockRuntime.ConversationRole.User);
            if (lastUserMsg != null)
            {
                var extraBlocks = new List<ContentBlock>();

                foreach (var pdfBase64 in pdfBase64List)
                {
                    var pdfBytes = Convert.FromBase64String(pdfBase64);
                    extraBlocks.Add(new ContentBlock
                    {
                        Document = new DocumentBlock
                        {
                            Format = Amazon.BedrockRuntime.DocumentFormat.Pdf,
                            Name = "attachment",
                            Source = new DocumentSource
                            {
                                Bytes = new MemoryStream(pdfBytes)
                            }
                        }
                    });
                }

                int imgIdx = 1;
                foreach (var (mediaType, base64Data) in imageList)
                {
                    var imgBytes = Convert.FromBase64String(base64Data);
                    // mediaType is e.g. "image/png" — extract the sub-type for ImageFormat
                    var formatStr = mediaType.Contains('/') ? mediaType.Split('/')[1] : mediaType;
                    var imageFormat = Amazon.BedrockRuntime.ImageFormat.FindValue(formatStr)
                        ?? Amazon.BedrockRuntime.ImageFormat.Png;
                    extraBlocks.Add(new ContentBlock
                    {
                        Image = new ImageBlock
                        {
                            Format = imageFormat,
                            Source = new ImageSource
                            {
                                Bytes = new MemoryStream(imgBytes)
                            }
                        }
                    });
                    _logger.LogInformation("[BEDROCK_API] (tools) Added image block {Index}: mediaType={MediaType}", imgIdx++, mediaType);
                }

                // Prepend extra blocks; existing text block(s) remain at end
                var existing = lastUserMsg.Content ?? new List<ContentBlock>();
                lastUserMsg.Content = extraBlocks.Concat(existing).ToList();
            }
        }

        var request = new ConverseStreamRequest
        {
            ModelId = bedrockModelId,
            Messages = converseMessages,
            InferenceConfig = new InferenceConfiguration
            {
                MaxTokens = maxTokens,
                Temperature = (float)temperature
            }
        };

        if (!string.IsNullOrEmpty(cleanedSystemPrompt))
        {
            request.System = new List<SystemContentBlock>
            {
                new SystemContentBlock { Text = cleanedSystemPrompt }
            };
        }

        if (tools?.Count > 0)
        {
            request.ToolConfig = new ToolConfiguration
            {
                Tools = tools.Select(t => new Tool
                {
                    ToolSpec = new ToolSpecification
                    {
                        Name = t.Name,
                        Description = t.Description,
                        InputSchema = new ToolInputSchema
                        {
                            Json = ParseJsonToDocument(t.InputSchemaJson)
                        }
                    }
                }).ToList()
            };
        }

        ConverseStreamResponse? response = null;
        string? apiError = null;
        try
        {
            response = await _client.ConverseStreamAsync(request, ct);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Bedrock Converse Stream error for model {Model}", bedrockModelId);
            apiError = $"API Error: {ex.Message}";
        }

        if (apiError != null)
        {
            yield return new StreamChunk { Type = "error", Text = apiError };
            yield break;
        }

        // Buffer stop reason and token usage — ConverseStreamMetadataEvent fires AFTER MessageStopEvent
        string? stopReason = null;
        int inputTokens = 0;
        int outputTokens = 0;

        foreach (var ev in response!.Stream)
        {
            if (ct.IsCancellationRequested) yield break;

            if (ev is ContentBlockDeltaEvent delta)
            {
                if (delta.Delta?.Text != null)
                {
                    yield return new StreamChunk { Type = "text", Text = delta.Delta.Text };
                }
                else if (delta.Delta?.ToolUse != null)
                {
                    yield return new StreamChunk { Type = "tool_input_delta", Text = delta.Delta.ToolUse.Input };
                }
            }
            else if (ev is ContentBlockStartEvent start && start.Start?.ToolUse != null)
            {
                yield return new StreamChunk
                {
                    Type = "tool_use_start",
                    ToolUseId = start.Start.ToolUse.ToolUseId,
                    ToolName = start.Start.ToolUse.Name
                };
            }
            else if (ev is MessageStopEvent stop)
            {
                // Buffer stop reason — emit done chunk AFTER foreach so metadata tokens are captured
                stopReason = stop.StopReason?.Value;
            }
            else if (ev is ConverseStreamMetadataEvent metadata)
            {
                if (metadata.Usage != null)
                {
                    inputTokens = metadata.Usage.InputTokens;
                    outputTokens = metadata.Usage.OutputTokens;
                }
            }
        }

        // Emit done chunk after all events processed (includes metadata tokens)
        yield return new StreamChunk
        {
            Type = "done",
            StopReason = stopReason,
            InputTokens = inputTokens,
            OutputTokens = outputTokens
        };
    }

    /// <summary>
    /// Converts MessageDto list to Bedrock Converse API Message list.
    /// Handles plain text, tool_result (user turn), and tool_use (assistant turn) messages.
    /// </summary>
    private List<Message> BuildConverseMessages(List<MessageDto> messages)
    {
        var result = new List<Message>();
        foreach (var msg in messages)
        {
            var role = msg.Role == "assistant"
                ? Amazon.BedrockRuntime.ConversationRole.Assistant
                : Amazon.BedrockRuntime.ConversationRole.User;

            var content = msg.Content?.Trim() ?? "";

            // Detect tool_result blocks: user turn with JSON array containing tool_use_id
            if (msg.Role == "user" && content.StartsWith("[") && content.Contains("tool_use_id"))
            {
                try
                {
                    using var doc = JsonDocument.Parse(content);
                    var contentBlocks = new List<ContentBlock>();
                    foreach (var block in doc.RootElement.EnumerateArray())
                    {
                        if (block.TryGetProperty("type", out var typeEl) &&
                            typeEl.GetString() == "tool_result" &&
                            block.TryGetProperty("tool_use_id", out var tuId))
                        {
                            var toolUseId = tuId.GetString() ?? "";
                            var toolContent = block.TryGetProperty("content", out var tc) ? tc.GetString() ?? "" : "";
                            contentBlocks.Add(new ContentBlock
                            {
                                ToolResult = new ToolResultBlock
                                {
                                    ToolUseId = toolUseId,
                                    Content = new List<ToolResultContentBlock>
                                    {
                                        new ToolResultContentBlock { Text = toolContent }
                                    }
                                }
                            });
                        }
                        else
                        {
                            // Plain text block in array
                            var text = block.TryGetProperty("text", out var t) ? t.GetString() ?? "" : block.GetRawText();
                            contentBlocks.Add(new ContentBlock { Text = text });
                        }
                    }
                    if (contentBlocks.Any(cb => cb.ToolResult != null))
                    {
                        // Guard against orphaned tool_result blocks (no matching tool_use in preceding assistant turn).
                        // This can happen when: old DB conversations stored intermediate turns differently, or when
                        // the sliding window / summarization drops a tool_use turn while keeping the tool_result.
                        var prevMsg = result.LastOrDefault();
                        if (prevMsg == null || prevMsg.Role != Amazon.BedrockRuntime.ConversationRole.Assistant)
                        {
                            _logger.LogWarning("Dropping orphaned tool_result message (no preceding assistant turn).");
                            continue;
                        }

                        var toolUseIds = prevMsg.Content
                            .Where(cb => cb.ToolUse != null)
                            .Select(cb => cb.ToolUse!.ToolUseId)
                            .ToHashSet();

                        if (!toolUseIds.Any())
                        {
                            _logger.LogWarning("Dropping orphaned tool_result message (preceding assistant turn has no tool_use blocks).");
                            continue;
                        }

                        // Filter to only tool_result blocks with a matching tool_use_id
                        var validBlocks = contentBlocks
                            .Where(cb => cb.ToolResult == null || toolUseIds.Contains(cb.ToolResult.ToolUseId))
                            .ToList();

                        if (!validBlocks.Any(cb => cb.ToolResult != null))
                        {
                            _logger.LogWarning("Dropping tool_result message: no matching tool_use_id in preceding assistant turn.");
                            continue;
                        }

                        contentBlocks = validBlocks;
                    }

                    if (contentBlocks.Count > 0)
                    {
                        result.Add(new Message { Role = role, Content = contentBlocks });
                        continue;
                    }
                }
                catch { /* Fall through to plain text */ }
            }

            // Detect tool_use blocks: assistant turn with JSON array containing tool_use type
            if (msg.Role == "assistant" && content.StartsWith("[") && content.Contains("\"tool_use\""))
            {
                try
                {
                    using var doc = JsonDocument.Parse(content);
                    var contentBlocks = new List<ContentBlock>();
                    foreach (var block in doc.RootElement.EnumerateArray())
                    {
                        if (block.TryGetProperty("type", out var typeEl) &&
                            typeEl.GetString() == "tool_use" &&
                            block.TryGetProperty("id", out var idEl))
                        {
                            var toolUseId = idEl.GetString() ?? "";
                            var name = block.TryGetProperty("name", out var n) ? n.GetString() ?? "" : "";
                            var input = block.TryGetProperty("input", out var inp) ? inp.GetRawText() : "{}";
                            contentBlocks.Add(new ContentBlock
                            {
                                ToolUse = new ToolUseBlock
                                {
                                    ToolUseId = toolUseId,
                                    Name = name,
                                    // ParseJsonToDocument required — implicit string cast produces a string-typed
                                    // Document, not a JSON-object Document, causing Bedrock API rejection
                                    Input = ParseJsonToDocument(input)
                                }
                            });
                        }
                        else if (block.TryGetProperty("type", out var bt) && bt.GetString() == "text")
                        {
                            var text = block.TryGetProperty("text", out var t) ? t.GetString() ?? "" : "";
                            if (!string.IsNullOrEmpty(text))
                                contentBlocks.Add(new ContentBlock { Text = text });
                        }
                    }
                    if (contentBlocks.Count > 0)
                    {
                        result.Add(new Message { Role = role, Content = contentBlocks });
                        continue;
                    }
                }
                catch { /* Fall through to plain text */ }
            }

            // Plain text message
            if (!string.IsNullOrEmpty(content))
            {
                result.Add(new Message
                {
                    Role = role,
                    Content = new List<ContentBlock> { new ContentBlock { Text = content } }
                });
            }
        }
        // Sonnet 4.6+ does not support assistant prefill — messages array must end with a user turn.
        // Strip any trailing assistant messages (e.g. synthetic "Understood" ack from sliding window).
        while (result.Count > 0 && result[^1].Role == Amazon.BedrockRuntime.ConversationRole.Assistant)
        {
            _logger.LogWarning("Stripping trailing assistant message from Converse messages array (prefill not supported in Sonnet 4.6+).");
            result.RemoveAt(result.Count - 1);
        }

        return result;
    }

    /// <summary>
    /// Recursively converts a <see cref="JsonElement"/> to an AWS SDK <see cref="Document"/>.
    /// Required because <c>Document</c> has no <c>FromJson</c> factory — the implicit string cast
    /// produces a string-typed Document, not a JSON-object Document.
    /// </summary>
    private static Document JsonElementToDocument(JsonElement element) => element.ValueKind switch
    {
        JsonValueKind.Object => new Document(
            element.EnumerateObject()
                   .ToDictionary(p => p.Name, p => JsonElementToDocument(p.Value))),
        JsonValueKind.Array => new Document(
            element.EnumerateArray()
                   .Select(JsonElementToDocument)
                   .ToArray()),
        JsonValueKind.String => (Document)(element.GetString() ?? ""),
        JsonValueKind.Number when element.TryGetInt32(out var i) => (Document)i,
        JsonValueKind.Number when element.TryGetInt64(out var l) => (Document)l,
        JsonValueKind.Number => (Document)element.GetDouble(),
        JsonValueKind.True => (Document)true,
        JsonValueKind.False => (Document)false,
        _ => new Document() // null / undefined
    };

    /// <summary>
    /// Parses a JSON string into an AWS SDK <see cref="Document"/> object.
    /// </summary>
    private static Document ParseJsonToDocument(string json)
    {
        using var doc = JsonDocument.Parse(json);
        return JsonElementToDocument(doc.RootElement.Clone());
    }

    public void Dispose()
    {
        _client.Dispose();
    }
}

public class StreamChunk
{
    public string Type { get; set; } = "text";
    public string? Text { get; set; }
    public int InputTokens { get; set; }
    public int OutputTokens { get; set; }
    public string? StopReason { get; set; }
    public string? ToolUseId { get; set; }
    public string? ToolName { get; set; }
}

public class MessageDto
{
    public string Role { get; set; } = "user";
    public string Content { get; set; } = string.Empty;
}

// Bedrock request/response models
file class BedrockStreamEvent
{
    public string? Type { get; set; }
    public BedrockDelta? Delta { get; set; }
    public BedrockUsage? Usage { get; set; }
    public BedrockMessageInfo? Message { get; set; }
}

file class BedrockDelta
{
    public string? Type { get; set; }
    public string? Text { get; set; }
}

file class BedrockUsage
{
    public int InputTokens { get; set; }
    public int OutputTokens { get; set; }
}

file class BedrockMessageInfo
{
    public BedrockUsage? Usage { get; set; }
}
