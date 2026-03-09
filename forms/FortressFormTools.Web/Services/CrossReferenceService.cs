using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.EntityFrameworkCore;
using FortressFormTools.Data;
using FortressFormTools.Data.Entities;

namespace FortressFormTools.Web.Services;

public class CrossReferenceService
{
    private readonly IDbContextFactory<AppDbContext> _contextFactory;
    private readonly ILogger<CrossReferenceService> _logger;
    private readonly Amazon.BedrockRuntime.IAmazonBedrockRuntime _bedrock;

    public CrossReferenceService(
        IDbContextFactory<AppDbContext> contextFactory,
        ILogger<CrossReferenceService> logger,
        Amazon.BedrockRuntime.IAmazonBedrockRuntime bedrock)
    {
        _contextFactory = contextFactory;
        _logger = logger;
        _bedrock = bedrock;
    }

    // ──────────────────────────────────────────────────────────────
    // Sprint 3: Project-level cross-reference via Bedrock/Claude
    // ──────────────────────────────────────────────────────────────

    public async Task<CrossReferenceResult> CrossReferenceProjectAsync(int projectId, CancellationToken ct = default)
    {
        try
        {
            await using var db = await _contextFactory.CreateDbContextAsync(ct);

            // 1. Load project
            var project = await db.FormProjects
                .AsNoTracking()
                .FirstOrDefaultAsync(p => p.Id == projectId, ct);

            if (project == null)
                return new CrossReferenceResult(projectId, 0, 0, 0, new List<string>(), $"Project {projectId} not found");

            // 2. Load approved documents with their extracted fields
            var docs = await db.FormLibraries
                .AsNoTracking()
                .Include(f => f.Fields)
                .Where(f => f.ProjectId == projectId && (f.Status == "Approved" || f.ApprovedAt != null))
                .ToListAsync(ct);

            if (!docs.Any())
                return new CrossReferenceResult(projectId, 0, 0, 0, new List<string>(), "No approved documents found for this project");

            // 3. Derive vertical abbreviation for field code prefixing
            var verticalAbbr = (project.Vertical?.ToUpperInvariant()) switch
            {
                "AVIATION" => "AV",
                "AUTO" => "AU",
                "GL" => "GL",
                "WC" => "WC",
                "PROPERTY" => "PROP",
                _ => project.Vertical?.ToUpperInvariant() ?? "GEN"
            };

            // 4. Build prompt
            var prompt = BuildCrossReferencePrompt(project, docs, verticalAbbr);

            // 5. Call Claude/Bedrock
            string claudeResponse;
            try
            {
                claudeResponse = await RunClaudeAsync(prompt, ct);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Bedrock call failed for project {ProjectId}", projectId);
                return new CrossReferenceResult(projectId, 0, 0, 0, new List<string>(), $"AI analysis failed: {ex.Message}");
            }

            if (string.IsNullOrWhiteSpace(claudeResponse))
                return new CrossReferenceResult(projectId, 0, 0, 0, new List<string>(), "AI returned empty response");

            // 6. Parse response into FormFieldCode records
            var fieldCodes = ParseFieldCodesFromResponse(claudeResponse, projectId);
            if (!fieldCodes.Any())
                return new CrossReferenceResult(projectId, 0, 0, 0, new List<string>(), "No fields parsed from AI response");

            // 7. Persist — upsert: preserve user-edited fields, update AI-determined fields
            await using var dbWrite = await _contextFactory.CreateDbContextAsync(ct);
            var existing = await dbWrite.FormFieldCodes
                .Where(f => f.ProjectId == projectId)
                .ToDictionaryAsync(f => f.FieldCode, ct);

            var sortOrder = 1;
            foreach (var fc in fieldCodes)
            {
                fc.SortOrder = sortOrder++;
                if (existing.TryGetValue(fc.FieldCode, out var existingRecord))
                {
                    // Update derived fields from AI analysis; preserve user-edited FieldLabel/FieldType/IsRequired
                    existingRecord.IsShared = fc.IsShared;
                    existingRecord.CarrierSources = fc.CarrierSources;
                    existingRecord.SectionName = fc.SectionName;
                    existingRecord.PanelId = fc.PanelId;
                    existingRecord.SortOrder = fc.SortOrder;
                    // IsSensitive is AI-determined, update it too
                    existingRecord.IsSensitive = fc.IsSensitive;
                }
                else
                {
                    dbWrite.FormFieldCodes.Add(fc);
                }
            }
            await dbWrite.SaveChangesAsync(ct);

            // 8. Create or update the project's QuestionSet
            await UpsertProjectQuestionSetAsync(dbWrite, project, fieldCodes, ct);

            // 9. Build result summary
            var sharedCount = fieldCodes.Count(f => f.IsShared);
            var specificCount = fieldCodes.Count(f => !f.IsShared);
            var panels = fieldCodes
                .Where(f => f.PanelId != null)
                .Select(f => f.PanelId!)
                .Distinct()
                .OrderBy(p => p)
                .ToList();

            return new CrossReferenceResult(projectId, fieldCodes.Count, sharedCount, specificCount, panels, null);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "CrossReferenceProjectAsync failed for project {ProjectId}", projectId);
            return new CrossReferenceResult(projectId, 0, 0, 0, new List<string>(), $"Cross-reference failed: {ex.Message}");
        }
    }

    private static string BuildCrossReferencePrompt(FormProject project, List<FormLibrary> docs, string verticalAbbr)
    {
        var lines = new List<string>
        {
            "You are analyzing insurance carrier application forms for cross-referencing.",
            "",
            $"VERTICAL: {project.Vertical}",
            $"PROJECT: {project.Name}",
            $"VERTICAL_ABBR: {verticalAbbr}",
            "",
            $"DOCUMENTS ({docs.Count} forms):"
        };

        foreach (var doc in docs)
        {
            lines.Add($"--- DOCUMENT: {doc.CarrierName} | Type: {doc.DocumentType ?? "application"} ---");
            if (doc.Fields.Any())
            {
                foreach (var field in doc.Fields.OrderBy(f => f.SortOrder ?? 0))
                {
                    var req = field.IsRequired ? " [required]" : "";
                    var section = field.SectionName != null ? $" ({field.SectionName})" : "";
                    lines.Add($"  - [{field.FieldType}] {field.FieldLabel}{section}{req}");
                }
            }
            else
            {
                lines.Add("  (no extracted fields)");
            }
        }

        lines.Add("");
        lines.Add("TASK: Analyze these forms and produce a unified field list in JSON format.");
        lines.Add("");
        lines.Add("For each unique field across all forms:");
        lines.Add($"- fieldCode: UPPER_SNAKE_CASE for vertical-specific (prefix: {verticalAbbr}_), lowercase_snake_case for standard shared fields");
        lines.Add("- fieldLabel: human-readable label");
        lines.Add("- fieldType: one of: text, number, currency, date, yes_no, checkbox, select, textarea");
        lines.Add("- isSensitive: true if SSN, DOB, license number, financial data");
        lines.Add("- isShared: true if appears on 2+ carrier forms");
        lines.Add("- isRequired: true if required on ALL forms that include it");
        lines.Add("- sectionName: logical grouping (e.g., \"Applicant Information\", \"Aircraft Details\")");
        lines.Add("- panelId: if part of a repeating entity (aircraft, pilot, vehicle, driver) use the panel name, else null");
        lines.Add("- carrierSources: JSON array of carrier names that include this field");
        lines.Add("");
        lines.Add("Standard shared field codes (use exactly these if applicable):");
        lines.Add("  business_name, dba_name, years_in_business, effective_date, expiration_date");
        lines.Add("  annual_revenue, num_employees, primary_contact_name, primary_contact_phone, primary_contact_email");
        lines.Add("  mailing_address, location_address, gl_limit, property_value, deductible, description_of_operations");
        lines.Add("  naics_code, sic_code, fein");
        lines.Add("");
        lines.Add("Return ONLY valid JSON array. No markdown, no explanation:");
        lines.Add("[{\"fieldCode\": \"...\", \"fieldLabel\": \"...\", \"fieldType\": \"...\", \"isSensitive\": false, \"isShared\": false, \"isRequired\": false, \"sectionName\": \"...\", \"panelId\": null, \"carrierSources\": []}, ...]");

        return string.Join("\n", lines);
    }

    private List<FormFieldCode> ParseFieldCodesFromResponse(string response, int projectId)
    {
        var result = new List<FormFieldCode>();
        try
        {
            var json = ExtractJsonArray(response);
            if (string.IsNullOrWhiteSpace(json)) return result;

            var items = JsonSerializer.Deserialize<List<FieldCodeDto>>(json, new JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true
            });

            if (items == null) return result;

            foreach (var item in items)
            {
                if (string.IsNullOrWhiteSpace(item.FieldCode)) continue;

                result.Add(new FormFieldCode
                {
                    ProjectId = projectId,
                    FieldCode = item.FieldCode.Trim(),
                    FieldLabel = item.FieldLabel?.Trim() ?? item.FieldCode,
                    FieldType = item.FieldType?.Trim() ?? "text",
                    IsSensitive = item.IsSensitive,
                    IsShared = item.IsShared,
                    IsRequired = item.IsRequired,
                    SectionName = item.SectionName,
                    PanelId = item.PanelId,
                    CarrierSources = item.CarrierSources != null
                        ? JsonSerializer.Serialize(item.CarrierSources)
                        : null
                });
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to parse field codes from AI response");
        }

        return result;
    }

    private async Task UpsertProjectQuestionSetAsync(
        AppDbContext db, FormProject project, List<FormFieldCode> fieldCodes, CancellationToken ct)
    {
        var questionSet = await db.QuestionSets
            .Include(q => q.Fields)
            .FirstOrDefaultAsync(q => q.ProjectId == project.Id, ct);

        if (questionSet == null)
        {
            questionSet = new QuestionSet
            {
                Name = $"{project.Name} — Cross-Reference",
                Vertical = project.Vertical,
                Description = $"Auto-generated from cross-reference analysis of {project.Name}",
                Status = "Draft",
                ProjectId = project.Id,
                CreatedAt = DateTime.UtcNow,
                UpdatedAt = DateTime.UtcNow
            };
            db.QuestionSets.Add(questionSet);
            await db.SaveChangesAsync(ct);
        }
        else
        {
            db.QuestionSetFields.RemoveRange(questionSet.Fields);
            await db.SaveChangesAsync(ct);
        }

        var sortOrder = 1;
        foreach (var fc in fieldCodes)
        {
            db.QuestionSetFields.Add(new QuestionSetField
            {
                QuestionSetId = questionSet.Id,
                QuestionText = fc.FieldLabel,
                FieldType = fc.FieldType,
                SectionName = fc.SectionName,
                IsRequired = fc.IsRequired,
                SortOrder = sortOrder++,
                SourceFormCount = fc.IsShared ? 2 : 1
            });
        }

        questionSet.UpdatedAt = DateTime.UtcNow;
        questionSet.Status = "Draft";
        await db.SaveChangesAsync(ct);
    }

    private static string ExtractJsonArray(string text)
    {
        var trimmed = text.Trim();
        if (trimmed.StartsWith("```"))
        {
            var firstNewline = trimmed.IndexOf('\n');
            if (firstNewline > 0) trimmed = trimmed[(firstNewline + 1)..];
            if (trimmed.EndsWith("```")) trimmed = trimmed[..^3];
            trimmed = trimmed.Trim();
        }

        var start = trimmed.IndexOf('[');
        var end = trimmed.LastIndexOf(']');
        if (start >= 0 && end > start)
            return trimmed[start..(end + 1)];

        return trimmed;
    }

    // ──────────────────────────────────────────────────────────────
    // Sprint 1/2 (legacy): Question-set-level cross-reference
    // ──────────────────────────────────────────────────────────────

    public async Task<FieldCrossReferenceResult> AnalyzeFormsAsync(int questionSetId, int[] formIds)
    {
        await using var _db = await _contextFactory.CreateDbContextAsync();
        var questionSet = await _db.QuestionSets.FindAsync(questionSetId);
        if (questionSet == null)
            throw new ArgumentException($"QuestionSet {questionSetId} not found");

        var forms = await _db.FormLibraries
            .AsNoTracking()
            .Where(f => formIds.Contains(f.Id))
            .Include(f => f.Fields)
                .ThenInclude(ff => ff.DictionaryField)
            .ToListAsync();

        var allFields = forms.SelectMany(f => f.Fields).ToList();
        var fieldGroups = new List<FieldGroup>();
        var totalForms = formIds.Length;

        var matchedFields = allFields.Where(f => f.DictionaryFieldId != null).ToList();
        var unmatchedFields = allFields.Where(f => f.DictionaryFieldId == null).ToList();

        var exactGroups = matchedFields
            .GroupBy(f => f.DictionaryFieldId!.Value)
            .Select(g =>
            {
                var first = g.First();
                return new FieldGroup
                {
                    Id = Guid.NewGuid().ToString(),
                    CanonicalName = first.DictionaryField?.DisplayName ?? first.FieldLabel,
                    DictionaryCode = first.DictionaryField?.FieldCode,
                    MatchType = "exact",
                    Coverage = g.Select(f => f.FormLibraryId).Distinct().Count(),
                    TotalForms = totalForms,
                    Variants = g.Select(f => new FieldVariant
                    {
                        FormId = f.FormLibraryId,
                        FormName = forms.FirstOrDefault(fm => fm.Id == f.FormLibraryId)?.FormName ?? "",
                        FieldLabel = f.FieldLabel,
                        FieldId = f.Id,
                        Confidence = 1.0
                    }).ToList()
                };
            })
            .ToList();

        fieldGroups.AddRange(exactGroups);

        if (unmatchedFields.Any())
        {
            try
            {
                var synonymGroups = await DetectSynonymsAsync(unmatchedFields, forms, totalForms);
                fieldGroups.AddRange(synonymGroups);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Synonym detection failed, treating unmatched fields as unique");
                foreach (var field in unmatchedFields)
                {
                    fieldGroups.Add(new FieldGroup
                    {
                        Id = Guid.NewGuid().ToString(),
                        CanonicalName = field.FieldLabel,
                        DictionaryCode = null,
                        MatchType = "unique",
                        Coverage = 1,
                        TotalForms = totalForms,
                        Variants = new List<FieldVariant>
                        {
                            new FieldVariant
                            {
                                FormId = field.FormLibraryId,
                                FormName = forms.FirstOrDefault(f => f.Id == field.FormLibraryId)?.FormName ?? "",
                                FieldLabel = field.FieldLabel,
                                FieldId = field.Id,
                                Confidence = 1.0
                            }
                        }
                    });
                }
            }
        }

        return new FieldCrossReferenceResult
        {
            QuestionSetId = questionSetId,
            FormsAnalyzed = formIds.ToList(),
            FieldGroups = fieldGroups.OrderByDescending(g => g.Coverage).ThenBy(g => g.CanonicalName).ToList()
        };
    }

    private async Task<List<FieldGroup>> DetectSynonymsAsync(
        List<FormField> unmatchedFields, List<FormLibrary> forms, int totalForms)
    {
        var formFieldMap = unmatchedFields
            .GroupBy(f => f.FormLibraryId)
            .ToDictionary(g => g.Key, g => g.ToList());

        var promptLines = new List<string>
        {
            "Analyze these insurance form field labels and identify which ones are semantically equivalent (synonyms or near-synonyms for the same data point). Group them.",
            ""
        };

        foreach (var (formId, fields) in formFieldMap)
        {
            var form = forms.FirstOrDefault(f => f.Id == formId);
            var formName = form != null ? $"{form.CarrierName} - {form.FormName}" : $"Form {formId}";
            promptLines.Add($"Fields from {formName} (FormId={formId}):");
            foreach (var field in fields)
                promptLines.Add($"  - \"{field.FieldLabel}\" (fieldId={field.Id})");
            promptLines.Add("");
        }

        promptLines.Add("Return ONLY valid JSON with this exact structure:");
        promptLines.Add("{");
        promptLines.Add("  \"groups\": [");
        promptLines.Add("    {");
        promptLines.Add("      \"canonicalName\": \"Human-readable group name\",");
        promptLines.Add("      \"confidence\": 0.92,");
        promptLines.Add("      \"variants\": [");
        promptLines.Add("        { \"formId\": 1, \"fieldLabel\": \"Field Label\", \"fieldId\": 42 }");
        promptLines.Add("      ]");
        promptLines.Add("    }");
        promptLines.Add("  ],");
        promptLines.Add("  \"uniqueFields\": [");
        promptLines.Add("    { \"formId\": 2, \"fieldLabel\": \"Unique Field\", \"fieldId\": 91 }");
        promptLines.Add("  ]");
        promptLines.Add("}");
        promptLines.Add("No explanation. JSON only.");

        var prompt = string.Join("\n", promptLines);
        var jsonResponse = await RunClaudeAsync(prompt, CancellationToken.None);

        if (string.IsNullOrWhiteSpace(jsonResponse))
        {
            _logger.LogWarning("Claude returned empty response for synonym detection");
            throw new Exception("Empty Claude response");
        }

        var json = ExtractJson(jsonResponse);
        var result = JsonSerializer.Deserialize<ClaudeSynonymResult>(json, new JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = true
        });

        var fieldGroups = new List<FieldGroup>();

        if (result?.Groups != null)
        {
            foreach (var group in result.Groups)
            {
                fieldGroups.Add(new FieldGroup
                {
                    Id = Guid.NewGuid().ToString(),
                    CanonicalName = group.CanonicalName ?? "Unknown",
                    DictionaryCode = null,
                    MatchType = "synonym",
                    Coverage = group.Variants?.Select(v => v.FormId).Distinct().Count() ?? 0,
                    TotalForms = totalForms,
                    Variants = group.Variants?.Select(v => new FieldVariant
                    {
                        FormId = v.FormId,
                        FormName = forms.FirstOrDefault(f => f.Id == v.FormId)?.FormName ?? "",
                        FieldLabel = v.FieldLabel ?? "",
                        FieldId = v.FieldId,
                        Confidence = group.Confidence
                    }).ToList() ?? new List<FieldVariant>()
                });
            }
        }

        if (result?.UniqueFields != null)
        {
            foreach (var unique in result.UniqueFields)
            {
                fieldGroups.Add(new FieldGroup
                {
                    Id = Guid.NewGuid().ToString(),
                    CanonicalName = unique.FieldLabel ?? "Unknown",
                    DictionaryCode = null,
                    MatchType = "unique",
                    Coverage = 1,
                    TotalForms = totalForms,
                    Variants = new List<FieldVariant>
                    {
                        new FieldVariant
                        {
                            FormId = unique.FormId,
                            FormName = forms.FirstOrDefault(f => f.Id == unique.FormId)?.FormName ?? "",
                            FieldLabel = unique.FieldLabel ?? "",
                            FieldId = unique.FieldId,
                            Confidence = 1.0
                        }
                    }
                });
            }
        }

        return fieldGroups;
    }

    private static string ExtractJson(string text)
    {
        var trimmed = text.Trim();
        if (trimmed.StartsWith("```"))
        {
            var firstNewline = trimmed.IndexOf('\n');
            if (firstNewline > 0) trimmed = trimmed[(firstNewline + 1)..];
            if (trimmed.EndsWith("```")) trimmed = trimmed[..^3];
            trimmed = trimmed.Trim();
        }

        var start = trimmed.IndexOf('{');
        var end = trimmed.LastIndexOf('}');
        if (start >= 0 && end > start)
            return trimmed[start..(end + 1)];

        return trimmed;
    }

    private async Task<string> RunClaudeAsync(string prompt, CancellationToken ct = default)
    {
        var requestBody = System.Text.Json.JsonSerializer.Serialize(new
        {
            anthropic_version = "bedrock-2023-05-31",
            max_tokens = 8192,
            messages = new[]
            {
                new { role = "user", content = prompt }
            }
        });

        var request = new Amazon.BedrockRuntime.Model.InvokeModelRequest
        {
            ModelId = "us.anthropic.claude-sonnet-4-6",
            ContentType = "application/json",
            Accept = "application/json",
            Body = new System.IO.MemoryStream(System.Text.Encoding.UTF8.GetBytes(requestBody))
        };

        var response = await _bedrock.InvokeModelAsync(request, ct);
        var responseBody = System.Text.Json.JsonDocument.Parse(response.Body);
        return responseBody.RootElement
            .GetProperty("content")[0]
            .GetProperty("text")
            .GetString() ?? string.Empty;
    }

    public async Task<List<QuestionSetField>> SaveBulkFieldsAsync(int questionSetId, List<BulkFieldInput> fields)
    {
        await using var _db = await _contextFactory.CreateDbContextAsync();
        var questionSet = await _db.QuestionSets.FindAsync(questionSetId);
        if (questionSet == null)
            throw new ArgumentException($"QuestionSet {questionSetId} not found");

        var saved = new List<QuestionSetField>();
        var order = 1;

        foreach (var field in fields)
        {
            var qsf = new QuestionSetField
            {
                QuestionSetId = questionSetId,
                DictionaryFieldId = field.DictionaryFieldId,
                QuestionText = field.CanonicalName,
                FieldType = field.FieldType ?? "text",
                SectionName = field.SectionName,
                IsRequired = field.Coverage >= field.TotalForms,
                SortOrder = order++,
                SourceFormCount = field.Coverage
            };
            _db.QuestionSetFields.Add(qsf);
            saved.Add(qsf);
        }

        questionSet.UpdatedAt = DateTime.UtcNow;
        await _db.SaveChangesAsync();
        return saved;
    }
}

// ── Sprint 3: Project-level cross-reference result ──

/// <summary>
/// Result of a project-level Bedrock cross-reference analysis.
/// </summary>
public record CrossReferenceResult(
    int ProjectId,
    int FieldsFound,
    int SharedFields,
    int CarrierSpecificFields,
    List<string> PanelsDetected,
    string? ErrorMessage
);

// ── Sprint 1/2 (legacy): Question-set cross-reference result types ──

public class FieldCrossReferenceResult
{
    public int QuestionSetId { get; set; }
    public List<int> FormsAnalyzed { get; set; } = new();
    public List<FieldGroup> FieldGroups { get; set; } = new();
}

public class FieldGroup
{
    public string Id { get; set; } = "";
    public string CanonicalName { get; set; } = "";
    public string? DictionaryCode { get; set; }
    public string MatchType { get; set; } = "unique"; // exact, synonym, unique
    public int Coverage { get; set; }
    public int TotalForms { get; set; }
    public List<FieldVariant> Variants { get; set; } = new();
}

public class FieldVariant
{
    public int FormId { get; set; }
    public string FormName { get; set; } = "";
    public string FieldLabel { get; set; } = "";
    public int FieldId { get; set; }
    public double Confidence { get; set; }
}

public class BulkFieldInput
{
    public string CanonicalName { get; set; } = "";
    public int? DictionaryFieldId { get; set; }
    public string? FieldType { get; set; }
    public string? SectionName { get; set; }
    public int Coverage { get; set; }
    public int TotalForms { get; set; }
}

public class AnalyzeRequest
{
    public int[] FormIds { get; set; } = Array.Empty<int>();
}

public class BulkFieldsRequest
{
    public List<BulkFieldInput> Fields { get; set; } = new();
}

// ── Internal DTO for parsing Bedrock cross-reference JSON response ──

internal class FieldCodeDto
{
    [JsonPropertyName("fieldCode")]
    public string? FieldCode { get; set; }

    [JsonPropertyName("fieldLabel")]
    public string? FieldLabel { get; set; }

    [JsonPropertyName("fieldType")]
    public string? FieldType { get; set; }

    [JsonPropertyName("isSensitive")]
    public bool IsSensitive { get; set; }

    [JsonPropertyName("isShared")]
    public bool IsShared { get; set; }

    [JsonPropertyName("isRequired")]
    public bool IsRequired { get; set; }

    [JsonPropertyName("sectionName")]
    public string? SectionName { get; set; }

    [JsonPropertyName("panelId")]
    public string? PanelId { get; set; }

    [JsonPropertyName("carrierSources")]
    public List<string>? CarrierSources { get; set; }
}

// ── Claude synonym detection models ──

public class ClaudeSynonymResult
{
    [JsonPropertyName("groups")]
    public List<ClaudeSynonymGroup>? Groups { get; set; }

    [JsonPropertyName("uniqueFields")]
    public List<ClaudeUniqueField>? UniqueFields { get; set; }
}

public class ClaudeSynonymGroup
{
    [JsonPropertyName("canonicalName")]
    public string? CanonicalName { get; set; }

    [JsonPropertyName("confidence")]
    public double Confidence { get; set; }

    [JsonPropertyName("variants")]
    public List<ClaudeFieldRef>? Variants { get; set; }
}

public class ClaudeFieldRef
{
    [JsonPropertyName("formId")]
    public int FormId { get; set; }

    [JsonPropertyName("fieldLabel")]
    public string? FieldLabel { get; set; }

    [JsonPropertyName("fieldId")]
    public int FieldId { get; set; }
}

public class ClaudeUniqueField
{
    [JsonPropertyName("formId")]
    public int FormId { get; set; }

    [JsonPropertyName("fieldLabel")]
    public string? FieldLabel { get; set; }

    [JsonPropertyName("fieldId")]
    public int FieldId { get; set; }
}
