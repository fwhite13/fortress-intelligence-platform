using Amazon;
using Amazon.BedrockRuntime;
using Amazon.BedrockRuntime.Model;
using FortressAI.Shared.Models;
using Microsoft.Extensions.Configuration;

namespace FortressAI.Web.Services;

/// <summary>
/// Converts a raw user message + recent conversation history into 2–3 semantically
/// precise search queries optimized for vector KB retrieval.
///
/// Uses Claude Haiku 4.5 (us.anthropic.claude-haiku-4-5-20251001-v1:0) — lightweight,
/// fast, and cost-effective for query generation. Model ID is sourced from ModelInfo.cs.
/// </summary>
public class KbQueryService
{
    private readonly AmazonBedrockRuntimeClient _client;
    private readonly ILogger<KbQueryService> _logger;
    private readonly int _timeoutMs;

    // Claude Haiku 4.5 — fast and cost-effective for query generation.
    // Model ID from ModelInfo.cs (claude-haiku-4-5). HARDCODED — never use the user's selected model here.
    private const string QueryModelId = "us.anthropic.claude-haiku-4-5-20251001-v1:0";

    public KbQueryService(IConfiguration config, ILogger<KbQueryService> logger)
    {
        _logger = logger;
        _timeoutMs = config.GetValue<int>("KnowledgeBase:QueryGenerationTimeoutMs", 5000);
        _client = new AmazonBedrockRuntimeClient(new AmazonBedrockRuntimeConfig
        {
            RegionEndpoint = RegionEndpoint.USEast1
        });
    }

    /// <summary>
    /// Given the user's current message and recent conversation history, generate
    /// 2–3 semantically precise search queries for KB retrieval.
    ///
    /// Falls back to [ userMessage ] on any failure — never throws.
    /// </summary>
    public async Task<List<string>> GenerateSearchQueriesAsync(
        string userMessage,
        IEnumerable<MessageDto>? recentMessages = null)
    {
        try
        {
            var contextMessages = recentMessages?
                .TakeLast(5)
                .Select(m => $"{m.Role.ToUpper()}: {m.Content?.Trim()}")
                .Where(s => !string.IsNullOrWhiteSpace(s))
                .ToList() ?? new List<string>();

            var prompt = BuildQueryPrompt(userMessage, contextMessages);

            using var cts = new CancellationTokenSource(_timeoutMs);

            var request = new InvokeModelRequest
            {
                ModelId = QueryModelId,
                ContentType = "application/json",
                Accept = "application/json",
                Body = new MemoryStream(System.Text.Encoding.UTF8.GetBytes(
                    System.Text.Json.JsonSerializer.Serialize(new
                    {
                        anthropic_version = "bedrock-2023-05-31",
                        max_tokens = 256,
                        temperature = 0.2,
                        messages = new[]
                        {
                            new { role = "user", content = prompt }
                        }
                    })
                ))
            };

            var response = await _client.InvokeModelAsync(request, cts.Token);
            var responseBody = await new StreamReader(response.Body).ReadToEndAsync();
            var queries = ParseQueriesFromResponse(responseBody, userMessage);

            _logger.LogInformation(
                "[KbQueryService] Generated {Count} queries from user message (len={Len}): {Queries}",
                queries.Count, userMessage.Length, string.Join(" | ", queries));

            return queries;
        }
        catch (OperationCanceledException)
        {
            _logger.LogWarning(
                "[KbQueryService] Query generation timed out ({TimeoutMs}ms) — falling back to raw message",
                _timeoutMs);
            return new List<string> { userMessage };
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "[KbQueryService] Query generation failed — falling back to raw message");
            return new List<string> { userMessage };
        }
    }

    private static string BuildQueryPrompt(string userMessage, List<string> contextMessages)
    {
        var contextBlock = contextMessages.Any()
            ? $"""
              ## Recent Conversation Context
              {string.Join("\n", contextMessages)}

              """
            : "";

        return $"""
            {contextBlock}## Current User Message
            {userMessage}

            ## Task
            You are a search query optimizer for a knowledge base retrieval system.
            Generate 2-3 precise search queries to retrieve the most relevant documents for the user's message.

            Rules:
            - Extract key entities, topics, and technical terms from the user message
            - Remove filler words, greetings, and conversational noise ("can you help me", "I was wondering", "that thing from earlier")
            - Use the conversation context to resolve ambiguous pronouns ("it", "that", "this") into specific terms
            - Generate varied phrasings to maximize recall (synonyms, alternate terminology)
            - Each query should be 3-10 words, optimized for semantic vector search
            - Do NOT include queries that are too broad (e.g., "company information") or too vague
            - If the user message is already a precise, specific query, return it as-is (1 query)

            Output ONLY a JSON array of strings. No explanation, no markdown, no wrapper object.
            Example output: ["disability insurance claim process", "how to file STD claim", "short term disability documentation requirements"]
            """;
    }

    private List<string> ParseQueriesFromResponse(string responseBody, string fallbackQuery)
    {
        try
        {
            using var doc = System.Text.Json.JsonDocument.Parse(responseBody);
            // Bedrock response: { "content": [{ "type": "text", "text": "..." }] }
            var text = doc.RootElement
                .GetProperty("content")[0]
                .GetProperty("text")
                .GetString()?.Trim() ?? "";

            // Strip markdown code fences if present
            text = System.Text.RegularExpressions.Regex.Replace(text, @"```(?:json)?\s*|\s*```", "").Trim();

            var queries = System.Text.Json.JsonSerializer.Deserialize<List<string>>(text) ?? new();
            var valid = queries
                .Where(q => !string.IsNullOrWhiteSpace(q) && q.Length >= 3 && q.Length <= 500)
                .Take(3)
                .ToList();

            return valid.Any() ? valid : new List<string> { fallbackQuery };
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "[KbQueryService] Failed to parse query response — using fallback");
            return new List<string> { fallbackQuery };
        }
    }
}
