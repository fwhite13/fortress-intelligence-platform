using System.Text.Json;
using FamOs.Web.Data.Dtos;
using FamOs.Web.Data.Entities;

namespace FamOs.Web.Services;

public class CoverageGapService : ICoverageGapService
{
    /// <inheritdoc/>
    public GapEvaluationResult EvaluateGaps(
        HashSet<string> checkedRequirementSlugs,
        List<QuoteWithCoverageDto> allQuotes,
        Dictionary<string, Guid> packageASelections,
        Dictionary<string, Guid> packageBSelections,
        List<Requirement> requirements,
        List<LineOfBusiness> lines)
    {
        var result = new GapEvaluationResult();

        foreach (var reqSlug in checkedRequirementSlugs)
        {
            var req = requirements.FirstOrDefault(r => r.Slug == reqSlug);
            if (req == null) continue;

            var lineId = req.LineOfBusinessId;
            var line   = lines.FirstOrDefault(l => l.Id == lineId);
            if (line == null) continue;

            var lineSlug     = line.Slug;
            var quotesOnLine = allQuotes.Where(q => q.LineOfBusinessId == lineId).ToList();

            var selectedA = packageASelections.TryGetValue(lineSlug, out var qidA)
                ? allQuotes.FirstOrDefault(q => q.Id == qidA)
                : null;

            var selectedB = packageBSelections.TryGetValue(lineSlug, out var qidB)
                ? allQuotes.FirstOrDefault(q => q.Id == qidB)
                : null;

            var anyCarrierCovers = quotesOnLine.Any(q =>
                q.CoverageDetails?.Includes.Contains(reqSlug) == true);

            var gapA = selectedA != null &&
                       selectedA.CoverageDetails?.Excludes.Contains(reqSlug) == true;

            var gapB = selectedB != null &&
                       selectedB.CoverageDetails?.Excludes.Contains(reqSlug) == true;

            GapStatus status;
            if (!anyCarrierCovers)
            {
                status = GapStatus.Unsatisfiable;
            }
            else if (gapA || gapB)
            {
                status = GapStatus.Gap;
            }
            else if ((selectedA?.CoverageDetails?.Includes.Contains(reqSlug) == true) ||
                     (selectedB?.CoverageDetails?.Includes.Contains(reqSlug) == true))
            {
                status = GapStatus.Covered;
            }
            else
            {
                status = GapStatus.Unchecked;
            }

            result.RequirementStatus[reqSlug] = status;

            if (status == GapStatus.Gap)
            {
                if (gapA)
                    result.PackageAGapsByLine[lineSlug] =
                        result.PackageAGapsByLine.GetValueOrDefault(lineSlug) + 1;
                if (gapB)
                    result.PackageBGapsByLine[lineSlug] =
                        result.PackageBGapsByLine.GetValueOrDefault(lineSlug) + 1;
            }
            else if (status == GapStatus.Unsatisfiable)
            {
                result.UnsatisfiableRequirements.Add(reqSlug);
            }
        }

        return result;
    }

    /// <inheritdoc/>
    public List<CoverageChangeDto> DetectCoverageRemovals(
        IncumbentPolicyDto incumbent,
        QuoteWithCoverageDto proposedQuote,
        LineOfBusiness lob)
    {
        var changes = new List<CoverageChangeDto>();

        if (incumbent.Vals.Count == 0) return changes;

        var proposedVals = proposedQuote.CoverageDetails?.Vals ?? new Dictionary<string, string>();

        // Parse LOB field definitions for human-readable labels
        var fieldLabels = ParseFieldLabels(lob.FieldDefinitions);

        foreach (var (key, incumbentValue) in incumbent.Vals)
        {
            if (!proposedVals.TryGetValue(key, out var proposedValue))
            {
                // Field absent in proposed → removed
                changes.Add(new CoverageChangeDto
                {
                    LineOfBusinessId = lob.Id,
                    FieldKey         = key,
                    FieldLabel       = fieldLabels.GetValueOrDefault(key, key),
                    IncumbentValue   = incumbentValue,
                    ProposedValue    = null,
                    ChangeType       = "removed",
                });
            }
            else if (proposedValue != incumbentValue)
            {
                // Try to detect numeric reduction
                var changeType = DetectNumericChangeType(incumbentValue, proposedValue);
                if (changeType != null)
                {
                    changes.Add(new CoverageChangeDto
                    {
                        LineOfBusinessId = lob.Id,
                        FieldKey         = key,
                        FieldLabel       = fieldLabels.GetValueOrDefault(key, key),
                        IncumbentValue   = incumbentValue,
                        ProposedValue    = proposedValue,
                        ChangeType       = changeType,
                    });
                }
            }
        }

        return changes;
    }

    // ── Private helpers ────────────────────────────────────────────────────────

    private static Dictionary<string, string> ParseFieldLabels(string? fieldDefinitionsJson)
    {
        if (string.IsNullOrWhiteSpace(fieldDefinitionsJson))
            return new();

        try
        {
            var defs = JsonSerializer.Deserialize<List<FieldDefinition>>(fieldDefinitionsJson,
                new JsonSerializerOptions { PropertyNameCaseInsensitive = true });

            return defs?.ToDictionary(f => f.Key, f => f.Label) ?? new();
        }
        catch
        {
            return new();
        }
    }

    /// <returns>"reduced" if proposed is numerically less than incumbent, "removed" if non-numeric change, null if equal or increased.</returns>
    private static string? DetectNumericChangeType(string incumbentValue, string proposedValue)
    {
        // Strip common currency/formatting chars
        static decimal? TryParse(string v)
        {
            var cleaned = v.Replace("$", "").Replace(",", "").Trim();
            return decimal.TryParse(cleaned, out var n) ? n : null;
        }

        var iNum = TryParse(incumbentValue);
        var pNum = TryParse(proposedValue);

        if (iNum.HasValue && pNum.HasValue)
            return pNum.Value < iNum.Value ? "reduced" : null;

        // Non-numeric difference → treat as removed/changed
        return "removed";
    }

    private record FieldDefinition(string Key, string Label, int Order);
}
