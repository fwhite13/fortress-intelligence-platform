using System.Text;
using System.Text.Json;
using Amazon.BedrockRuntime;
using Amazon.BedrockRuntime.Model;
using Microsoft.EntityFrameworkCore;
using FortressFormTools.Data;
using FortressFormTools.Data.Entities;

namespace FortressFormTools.Web.Services;

public class GeneratorService
{
    private readonly IDbContextFactory<AppDbContext> _contextFactory;
    private readonly ILogger<GeneratorService> _logger;
    private readonly IAmazonBedrockRuntime _bedrockRuntime;

    public GeneratorService(
        IDbContextFactory<AppDbContext> contextFactory,
        ILogger<GeneratorService> logger,
        IAmazonBedrockRuntime bedrockRuntime)
    {
        _contextFactory = contextFactory;
        _logger = logger;
        _bedrockRuntime = bedrockRuntime;
    }

    // ──────────────────────────────────────────────────────────────
    // Sprint 5: Project-level SurveyJS generation from FormFieldCode
    // ──────────────────────────────────────────────────────────────

    /// <summary>
    /// Generates a SurveyJS JSON string from a project's approved FormFieldCode records.
    /// In-memory only — no DB save.
    /// </summary>
    public async Task<string> GenerateSurveyJsonAsync(int projectId, string tone)
    {
        await using var db = await _contextFactory.CreateDbContextAsync();

        var project = await db.FormProjects
            .AsNoTracking()
            .FirstOrDefaultAsync(p => p.Id == projectId);

        if (project == null)
            throw new ArgumentException($"Project {projectId} not found");

        var fields = await db.FormFieldCodes
            .AsNoTracking()
            .Where(f => f.ProjectId == projectId)
            .OrderBy(f => f.SectionName)
            .ThenBy(f => f.SortOrder)
            .ToListAsync();

        var fieldLines = fields.Any()
            ? string.Join("\n", fields.Select(f =>
                $"{f.FieldCode} | {f.FieldLabel} | {f.FieldType} | {(f.IsRequired ? "required" : "optional")} | {f.SectionName ?? "General"}"))
            : "(no fields defined yet — generate a placeholder survey)";

        var prompt = $$"""
You are generating a SurveyJS JSON form for an insurance application.

Project: {{project.Name}}
Tone: {{tone}} (professional/conversational/formal/simple)

Generate a valid SurveyJS JSON with pages organized by section. Each page = one section.

Fields:
{{fieldLines}}

Field type mapping:
- text → "text"
- number/currency → "text" with inputType:"number"
- date → "text" with inputType:"date"
- yes_no → "boolean"
- checkbox → "checkbox"
- select → "dropdown" with choices:["Option 1","Option 2","Option 3"]
- textarea → "comment"

Return ONLY valid JSON. No markdown, no explanation. Start with { and end with }.

SurveyJS structure:
{"title":"...","pages":[{"name":"page_sectionname","title":"Section Name","elements":[{"type":"text","name":"field_code","title":"Field Label","isRequired":true}]}]}
""";

        var raw = await RunClaudeAsync(prompt);
        var stripped = ExtractJson(raw);

        try
        {
            JsonDocument.Parse(stripped);
        }
        catch (JsonException)
        {
            throw new InvalidOperationException($"Generated content is not valid JSON. Raw response: {raw}");
        }

        return stripped;
    }

    // ──────────────────────────────────────────────────────────────
    // Sprint 4: QuestionSet-level generation (preserved)
    // ──────────────────────────────────────────────────────────────

    public async Task<GeneratedSchema> GenerateSurveyJsonAsync(
        int questionSetId, int toneTemplateId, GeneratorSettings settings)
    {
        await using var _db = await _contextFactory.CreateDbContextAsync();
        var questionSet = await _db.QuestionSets
            .AsNoTracking()
            .Include(qs => qs.Fields)
                .ThenInclude(f => f.DictionaryField)
            .FirstOrDefaultAsync(qs => qs.Id == questionSetId);

        if (questionSet == null)
            throw new ArgumentException($"QuestionSet {questionSetId} not found");

        var toneTemplate = await _db.ToneTemplates.FindAsync(toneTemplateId);
        if (toneTemplate == null)
            toneTemplate = await _db.ToneTemplates.FirstOrDefaultAsync(t => t.IsSystem);

        var toneName = toneTemplate?.Name ?? "Professional";
        var toneFragment = toneTemplate?.PromptFragment ?? "Use clear, professional business language.";

        var fields = questionSet.Fields.OrderBy(f => f.SortOrder).ToList();

        // If no fields, generate a basic placeholder survey
        if (!fields.Any())
        {
            var placeholderJson = JsonSerializer.Serialize(new
            {
                title = questionSet.Name,
                showProgressBar = settings.ShowProgressBar ? "top" : "off",
                requiredText = settings.RequiredMark,
                pages = new[]
                {
                    new
                    {
                        name = "page1",
                        title = "General Information",
                        elements = new object[]
                        {
                            new { type = "text", name = "placeholder", title = "No questions configured yet. Add fields via Cross-Reference Analysis." }
                        }
                    }
                }
            }, new JsonSerializerOptions { WriteIndented = true });

            var placeholderSchema = new GeneratedSchema
            {
                QuestionSetId = questionSetId,
                ToneTemplateId = toneTemplateId,
                SchemaJson = placeholderJson,
                SettingsJson = JsonSerializer.Serialize(settings),
                Version = await GetNextVersionAsync(questionSetId),
                Status = "Draft",
                CreatedAt = DateTime.UtcNow
            };
            _db.GeneratedSchemas.Add(placeholderSchema);
            await _db.SaveChangesAsync();
            return placeholderSchema;
        }

        var fieldLines = string.Join("\n", fields.Select(f =>
            $"- {f.QuestionText} (type: {f.FieldType}, required: {f.IsRequired}, section: {f.SectionName ?? "General"})"));

        var prompt = $"""
You are generating a SurveyJS-compliant JSON survey for insurance applications.

Question Set: {questionSet.Name}
Tone: {toneName} — {toneFragment}

Questions to include:
{fieldLines}

Generate a complete SurveyJS JSON object with:
1. Pages grouped by section
2. Field types mapped: text→text, number→text with inputType:number, date→text with inputType:date, checkbox→boolean, dropdown→dropdown, radio→radiogroup, textarea→comment, email→text with inputType:email, phone→text with inputType:tel, currency→text with inputType:number, address→comment
3. Apply {toneName} tone to question titles (keep professional but match tone)
4. Required fields marked with isRequired:true
5. Logical page breaks between sections
6. showProgressBar: "{(settings.ShowProgressBar ? "top" : "off")}"
7. requiredText: "{settings.RequiredMark}"

Return ONLY valid JSON. No explanation, no markdown fences.
""";

        string schemaJson;
        try
        {
            var claudeOutput = await RunClaudeAsync(prompt);
            schemaJson = ExtractJson(claudeOutput);

            // Validate it's valid JSON with expected structure
            var parsed = JsonDocument.Parse(schemaJson);
            var root = parsed.RootElement;
            if (!root.TryGetProperty("pages", out _) && !root.TryGetProperty("elements", out _))
            {
                _logger.LogWarning("Claude returned JSON without pages/elements key, wrapping");
                schemaJson = JsonSerializer.Serialize(new
                {
                    title = questionSet.Name,
                    pages = new[] { new { name = "page1", elements = JsonSerializer.Deserialize<object>(schemaJson) } }
                }, new JsonSerializerOptions { WriteIndented = true });
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Claude generation failed, creating fallback schema");
            schemaJson = GenerateFallbackSchema(questionSet, fields, settings);
        }

        var schema = new GeneratedSchema
        {
            QuestionSetId = questionSetId,
            ToneTemplateId = toneTemplateId,
            SchemaJson = schemaJson,
            SettingsJson = JsonSerializer.Serialize(settings),
            Version = await GetNextVersionAsync(questionSetId),
            Status = "Draft",
            CreatedAt = DateTime.UtcNow
        };

        _db.GeneratedSchemas.Add(schema);
        await _db.SaveChangesAsync();
        return schema;
    }

    private string GenerateFallbackSchema(QuestionSet qs, List<QuestionSetField> fields, GeneratorSettings settings)
    {
        var pages = fields
            .GroupBy(f => f.SectionName ?? "General")
            .Select((g, i) => new
            {
                name = $"page{i + 1}",
                title = g.Key,
                elements = g.Select(f => MapFieldToSurveyElement(f)).ToArray()
            })
            .ToArray();

        return JsonSerializer.Serialize(new
        {
            title = qs.Name,
            showProgressBar = settings.ShowProgressBar ? "top" : "off",
            requiredText = settings.RequiredMark,
            pages
        }, new JsonSerializerOptions { WriteIndented = true });
    }

    private static object MapFieldToSurveyElement(QuestionSetField f)
    {
        var baseType = f.FieldType?.ToLower() switch
        {
            "textarea" or "comment" => "comment",
            "checkbox" or "boolean" => "boolean",
            "dropdown" or "select" => "dropdown",
            "radio" or "radiogroup" => "radiogroup",
            _ => "text"
        };

        var inputType = f.FieldType?.ToLower() switch
        {
            "number" or "currency" => "number",
            "date" => "date",
            "email" => "email",
            "phone" or "tel" => "tel",
            _ => (string?)null
        };

        if (inputType != null)
        {
            return new
            {
                type = "text",
                name = f.DictionaryField?.FieldCode ?? $"q{f.Id}",
                title = f.QuestionText,
                inputType,
                isRequired = f.IsRequired
            };
        }

        return new
        {
            type = baseType,
            name = f.DictionaryField?.FieldCode ?? $"q{f.Id}",
            title = f.QuestionText,
            isRequired = f.IsRequired
        };
    }

    private async Task<int> GetNextVersionAsync(int questionSetId)
    {
        await using var _db = await _contextFactory.CreateDbContextAsync();
        var max = await _db.GeneratedSchemas
            .Where(s => s.QuestionSetId == questionSetId)
            .MaxAsync(s => (int?)s.Version) ?? 0;
        return max + 1;
    }

    private static string ExtractJson(string text)
    {
        var trimmed = text.Trim();
        if (trimmed.StartsWith("```"))
        {
            var firstNewline = trimmed.IndexOf('\n');
            if (firstNewline > 0)
                trimmed = trimmed[(firstNewline + 1)..];
            if (trimmed.EndsWith("```"))
                trimmed = trimmed[..^3];
            trimmed = trimmed.Trim();
        }

        var start = trimmed.IndexOf('{');
        var end = trimmed.LastIndexOf('}');
        if (start >= 0 && end > start)
            return trimmed[start..(end + 1)];

        return trimmed;
    }

    private async Task<string> RunClaudeAsync(string prompt)
    {
        var requestBody = JsonSerializer.Serialize(new
        {
            anthropic_version = "bedrock-2023-05-31",
            max_tokens = 8192,
            messages = new[]
            {
                new { role = "user", content = prompt }
            }
        });

        var request = new InvokeModelRequest
        {
            ModelId = "us.anthropic.claude-sonnet-4-6",
            ContentType = "application/json",
            Accept = "application/json",
            Body = new MemoryStream(Encoding.UTF8.GetBytes(requestBody))
        };

        var response = await _bedrockRuntime.InvokeModelAsync(request);
        var responseBody = JsonDocument.Parse(response.Body);
        return responseBody.RootElement
            .GetProperty("content")[0]
            .GetProperty("text")
            .GetString() ?? string.Empty;
    }
}

public class GeneratorSettings
{
    public bool ShowProgressBar { get; set; } = true;
    public string RequiredMark { get; set; } = "*";
}

public class GenerateRequest
{
    public int ToneTemplateId { get; set; } = 1;
    public GeneratorSettings Settings { get; set; } = new();
}
