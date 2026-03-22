using FamOs.Web.Data.Dtos;

namespace FamOs.Web.Services;

public class AlertService : IAlertService
{
    private const decimal PremiumDifferentialThreshold = 0.20m;

    public List<AlertDto> EvaluateAlerts(
        ComparisonContextDto context,
        ComparisonState state,
        GapEvaluationResult gaps)
    {
        var alerts = new List<AlertDto>();

        // Alert 7: New Venture — account is not a renewal; suppress incumbent comparisons
        if (!context.Account.IsRenewal)
        {
            alerts.Add(new AlertDto
            {
                Type    = "info",
                Title   = "New Venture",
                Message = "This is a new account. Incumbent comparison is not applicable.",
            });
        }

        var allSelectedQuotes = GetSelectedQuotes(state, context.Quotes);
        var linesBySlug       = context.Lines.ToDictionary(l => l.Slug);

        // Alert 1: Premium Differential — selected carrier > 20% below benchmark
        foreach (var (pkg, selections) in new[] { ("A", state.PackageASelections), ("B", state.PackageBSelections) })
        {
            foreach (var (lineSlug, quoteId) in selections)
            {
                var quote = context.Quotes.FirstOrDefault(q => q.Id == quoteId);
                if (quote?.LineOfBusinessId == null) continue;

                if (!context.Benchmarks.TryGetValue(quote.LineOfBusinessId.Value, out var benchmark)) continue;
                if (benchmark <= 0) continue;

                if (quote.PremiumAmount < benchmark * (1 - PremiumDifferentialThreshold))
                {
                    alerts.Add(new AlertDto
                    {
                        Type     = "warning",
                        Title    = "Premium Differential",
                        Message  = $"Selected premium for {linesBySlug.GetValueOrDefault(lineSlug)?.Name ?? lineSlug} in Package {pkg} " +
                                   $"is more than 20% below benchmark ({quote.PremiumAmount:C} vs {benchmark:C}).",
                        Package  = pkg,
                        LineSlug = lineSlug,
                    });
                }
            }
        }

        // Alert 2: Mixed Billing — package has both Direct Bill and Agency Bill AND 3+ lines
        foreach (var (pkg, selections) in new[] { ("A", state.PackageASelections), ("B", state.PackageBSelections) })
        {
            if (selections.Count < 3) continue;

            var billingTypes = selections.Values
                .Select(qid => context.Quotes.FirstOrDefault(q => q.Id == qid)?.CoverageDetails?.Billing)
                .Where(b => !string.IsNullOrWhiteSpace(b))
                .Select(b => b!)
                .ToHashSet(StringComparer.OrdinalIgnoreCase);

            if (billingTypes.Contains("Direct Bill") && billingTypes.Contains("Agency Bill"))
            {
                alerts.Add(new AlertDto
                {
                    Type    = "warning",
                    Title   = "Mixed Billing",
                    Message = $"Package {pkg} mixes Direct Bill and Agency Bill carriers across {selections.Count} lines.",
                    Package = pkg,
                });
            }
        }

        // Alert 3: Surplus Lines Disclosure — any selected carrier has EsStatus = "Surplus Lines"
        foreach (var (pkg, selections) in new[] { ("A", state.PackageASelections), ("B", state.PackageBSelections) })
        {
            foreach (var (lineSlug, quoteId) in selections)
            {
                var quote = context.Quotes.FirstOrDefault(q => q.Id == quoteId);
                if (quote?.CoverageDetails?.EsStatus?.Equals("Surplus Lines", StringComparison.OrdinalIgnoreCase) == true)
                {
                    alerts.Add(new AlertDto
                    {
                        Type     = "info",
                        Title    = "Surplus Lines Disclosure",
                        Message  = $"{quote.CarrierName} in Package {pkg} ({linesBySlug.GetValueOrDefault(lineSlug)?.Name ?? lineSlug}) " +
                                   "is a Surplus Lines carrier. Disclosure requirements may apply.",
                        Package  = pkg,
                        LineSlug = lineSlug,
                    });
                }
            }
        }

        // Alerts 4 & 5 only apply to renewal accounts
        if (context.Account.IsRenewal)
        {
            // Alert 4: Coverage Adequacy vs Incumbent — coverage absent/reduced vs incumbent
            foreach (var (pkg, selections) in new[] { ("A", state.PackageASelections), ("B", state.PackageBSelections) })
            {
                foreach (var (lineSlug, quoteId) in selections)
                {
                    var quote = context.Quotes.FirstOrDefault(q => q.Id == quoteId);
                    if (quote?.LineOfBusinessId == null) continue;

                    if (!context.Incumbents.TryGetValue(quote.LineOfBusinessId.Value, out var incumbent)) continue;

                    var lob = context.Lines.FirstOrDefault(l => l.Id == quote.LineOfBusinessId.Value);
                    if (lob == null) continue;

                    var proposedVals  = quote.CoverageDetails?.Vals ?? new Dictionary<string, string>();
                    foreach (var (key, incumbentValue) in incumbent.Vals)
                    {
                        if (!proposedVals.TryGetValue(key, out var proposedValue))
                        {
                            alerts.Add(new AlertDto
                            {
                                Type     = "danger",
                                Title    = "Coverage Adequacy",
                                Message  = $"Package {pkg} — {lob.Name}: field '{key}' present in incumbent policy is absent in the proposed quote.",
                                Package  = pkg,
                                LineSlug = lineSlug,
                            });
                        }
                        else if (IsValueReduced(incumbentValue, proposedValue))
                        {
                            alerts.Add(new AlertDto
                            {
                                Type     = "warning",
                                Title    = "Coverage Adequacy",
                                Message  = $"Package {pkg} — {lob.Name}: '{key}' reduced from {incumbentValue} to {proposedValue}.",
                                Package  = pkg,
                                LineSlug = lineSlug,
                            });
                        }
                    }
                }
            }

            // Alert 5: Package Total Differential vs Incumbent — total > 20% different from incumbent
            var incumbentTotal = context.Incumbents.Values.Sum(i => i.AnnualPremium);
            if (incumbentTotal > 0)
            {
                foreach (var (pkg, selections) in new[] { ("A", state.PackageASelections), ("B", state.PackageBSelections) })
                {
                    var packageTotal = selections.Values
                        .Sum(qid => context.Quotes.FirstOrDefault(q => q.Id == qid)?.PremiumAmount ?? 0);

                    var diff = Math.Abs(packageTotal - incumbentTotal) / incumbentTotal;
                    if (diff > PremiumDifferentialThreshold)
                    {
                        var direction = packageTotal > incumbentTotal ? "above" : "below";
                        alerts.Add(new AlertDto
                        {
                            Type    = "warning",
                            Title   = "Package Total vs Incumbent",
                            Message = $"Package {pkg} total ({packageTotal:C}) is {diff:P0} {direction} incumbent total ({incumbentTotal:C}).",
                            Package = pkg,
                        });
                    }
                }
            }
        }

        // Alert 6: Unsatisfied Requirement — from GapEvaluationResult
        foreach (var reqSlug in gaps.UnsatisfiableRequirements)
        {
            alerts.Add(new AlertDto
            {
                Type    = "danger",
                Title   = "Unsatisfied Requirement",
                Message = $"No carrier in the market covers requirement '{reqSlug}'.",
            });
        }

        return alerts;
    }

    // ── Helpers ────────────────────────────────────────────────────────────────

    private static List<QuoteWithCoverageDto> GetSelectedQuotes(ComparisonState state, List<QuoteWithCoverageDto> allQuotes)
    {
        var ids = state.PackageASelections.Values
            .Concat(state.PackageBSelections.Values)
            .ToHashSet();
        return allQuotes.Where(q => ids.Contains(q.Id)).ToList();
    }

    private static bool IsValueReduced(string incumbentValue, string proposedValue)
    {
        static decimal? TryParse(string v)
        {
            var cleaned = v.Replace("$", "").Replace(",", "").Trim();
            return decimal.TryParse(cleaned, out var n) ? n : null;
        }

        var iNum = TryParse(incumbentValue);
        var pNum = TryParse(proposedValue);
        return iNum.HasValue && pNum.HasValue && pNum.Value < iNum.Value;
    }
}
