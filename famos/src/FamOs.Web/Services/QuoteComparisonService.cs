using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using FamOs.Web.Data;
using FamOs.Web.Data.Dtos;
using FamOs.Web.Data.Entities;

namespace FamOs.Web.Services;

public class QuoteComparisonService : IQuoteComparisonService
{
    private readonly IDbContextFactory<FamOsDbContext> _dbFactory;

    public QuoteComparisonService(IDbContextFactory<FamOsDbContext> dbFactory)
    {
        _dbFactory = dbFactory;
    }

    public async Task<ComparisonContextDto> GetComparisonContextAsync(Guid opportunityId, Guid userId, int tenantId)
    {
        await using var db = await _dbFactory.CreateDbContextAsync();

        var opportunity = await db.Opportunities.FirstOrDefaultAsync(o => o.Id == opportunityId)
            ?? throw new KeyNotFoundException($"Opportunity {opportunityId} not found");

        ProgramVertical? vertical = null;
        List<LineOfBusiness> lines = new();
        List<Requirement> requirements = new();
        List<BenchmarkPremium> benchmarkList = new();

        if (opportunity.ProgramId.HasValue)
        {
            vertical = await db.ProgramVerticals
                .FirstOrDefaultAsync(v => v.Id == opportunity.ProgramId.Value && v.TenantId == tenantId);

            if (vertical != null)
            {
                lines = await db.LinesOfBusiness
                    .Where(l => l.ProgramVerticalId == vertical.Id && l.TenantId == tenantId && l.IsActive)
                    .OrderBy(l => l.DisplayOrder)
                    .ToListAsync();

                requirements = await db.Requirements
                    .Where(r => r.ProgramVerticalId == vertical.Id && r.TenantId == tenantId && r.IsActive)
                    .OrderBy(r => r.DisplayOrder)
                    .ToListAsync();

                benchmarkList = await db.BenchmarkPremiums
                    .Where(bp => bp.ProgramVerticalId == vertical.Id && bp.TenantId == tenantId)
                    .ToListAsync();
            }
        }

        var rawQuotes = await db.Quotes
            .Include(q => q.QuoteLines)
            .Where(q => q.OpportunityId == opportunityId && q.TenantId == tenantId)
            .ToListAsync();

        var submissionIds = rawQuotes.Select(q => q.SubmissionId).Distinct().ToList();
        var submissionJsons = await db.Submissions
            .Where(s => submissionIds.Contains(s.Id))
            .Select(s => new { s.Id, s.QuoteResultJson })
            .ToDictionaryAsync(s => s.Id);

        var quotes = rawQuotes.Select(q =>
        {
            submissionJsons.TryGetValue(q.SubmissionId, out var sub);
            return MapToQuoteWithCoverageDto(q, sub?.QuoteResultJson);
        }).ToList();

        // IncumbentPolicies scoped to AccountId — no OpportunityId FK on the incumbent_policies table yet.
        // Returns empty until ADO#XXXX migrates incumbent_policies to carry opportunity_id.
        var incumbents = new Dictionary<Guid, IncumbentPolicyDto>();

        var benchmarks = benchmarkList
            .GroupBy(bp => bp.LineOfBusinessId)
            .ToDictionary(
                g => g.Key,
                g => g.OrderByDescending(bp => bp.EffectiveDate).First().AnnualPremium);

        var bundleRules = await db.CarrierBundleRules
            .Where(r => r.TenantId == tenantId && r.IsActive)
            .ToListAsync();

        var draft = await db.ComparisonDrafts
            .FirstOrDefaultAsync(d => d.OpportunityId == opportunityId && d.UserId == userId && d.TenantId == tenantId);

        return new ComparisonContextDto
        {
            OpportunityName = opportunity.Name,
            IsRenewal       = false, // TODO: Opportunity entity has no IsRenewal — needs separate ADO to add the property
            ProgramVertical = vertical,
            Lines           = lines,
            Requirements    = requirements,
            Quotes          = quotes,
            Incumbents      = incumbents,
            Benchmarks      = benchmarks,
            BundleRules     = bundleRules,
            SavedDraft      = draft != null ? MapDraftToDto(draft) : null,
        };
    }

    public async Task<List<QuoteWithCoverageDto>> GetQuotesForAccountAsync(Guid accountId, int tenantId)
    {
        await using var db = await _dbFactory.CreateDbContextAsync();

        var account = await db.Accounts.FirstOrDefaultAsync(a => a.Id == accountId)
            ?? throw new KeyNotFoundException($"Account {accountId} not found");

        var oppIds = await db.Opportunities
            .Where(o => o.AffinityId == account.AffinityId)
            .Select(o => o.Id)
            .ToListAsync();

        var rawQuotes = await db.Quotes
            .Where(q => oppIds.Contains(q.OpportunityId) && q.TenantId == tenantId)
            .ToListAsync();

        return rawQuotes.Select(q => MapToQuoteWithCoverageDto(q)).ToList();
    }

    public async Task SaveDraftAsync(Guid opportunityId, Guid userId, int tenantId, DraftStateDto dto)
    {
        await using var db = await _dbFactory.CreateDbContextAsync();

        var existing = await db.ComparisonDrafts
            .FirstOrDefaultAsync(d => d.OpportunityId == opportunityId && d.UserId == userId && d.TenantId == tenantId);

        if (existing != null)
        {
            existing.ActiveRequirementSlugs = JsonSerializer.Serialize(dto.CheckedRequirements);
            existing.PackageASelections     = JsonSerializer.Serialize(dto.PackageASelections);
            existing.PackageBSelections     = JsonSerializer.Serialize(dto.PackageBSelections);
            existing.ShowIncumbent          = dto.ShowIncumbent;
            existing.CollapsedBlocks        = JsonSerializer.Serialize(dto.CollapsedBlocks);
            existing.SavedAt                = DateTime.UtcNow;
        }
        else
        {
            db.ComparisonDrafts.Add(new ComparisonDraft
            {
                OpportunityId          = opportunityId,
                AccountId              = null,
                UserId                 = userId,
                TenantId               = tenantId,
                ActiveRequirementSlugs = JsonSerializer.Serialize(dto.CheckedRequirements),
                PackageASelections     = JsonSerializer.Serialize(dto.PackageASelections),
                PackageBSelections     = JsonSerializer.Serialize(dto.PackageBSelections),
                ShowIncumbent          = dto.ShowIncumbent,
                CollapsedBlocks        = JsonSerializer.Serialize(dto.CollapsedBlocks),
                SavedAt                = DateTime.UtcNow,
            });
        }

        await db.SaveChangesAsync();
    }

    public async Task<Guid> BuildProposalAsync(Guid opportunityId, Guid userId, Guid packageId, int tenantId)
    {
        await using var db = await _dbFactory.CreateDbContextAsync();

        var package = await db.Packages
            .FirstOrDefaultAsync(p => p.Id == packageId && p.TenantId == tenantId)
            ?? throw new KeyNotFoundException($"Package {packageId} not found");

        var selections = await db.PackageSelections
            .Where(s => s.PackageId == packageId && s.TenantId == tenantId)
            .ToListAsync();

        if (selections.Count == 0)
            throw new InvalidOperationException("Package has no selections — cannot build proposal");

        var opportunity = await db.Opportunities
            .FirstOrDefaultAsync(o => o.Id == opportunityId)
            ?? throw new InvalidOperationException($"Opportunity {opportunityId} not found");

        var primaryQuoteId = selections.First().QuoteId;

        var proposal = new Proposal
        {
            OpportunityId      = opportunity.Id,
            RecommendedQuoteId = primaryQuoteId,
            Status             = "draft",
            ProposalDate       = DateTime.UtcNow,
        };

        db.Proposals.Add(proposal);
        await db.SaveChangesAsync();

        return proposal.Id;
    }

    // ── Mapping helpers ────────────────────────────────────────────────────────

    private static QuoteWithCoverageDto MapToQuoteWithCoverageDto(Quote q, string? scraperJson = null)
    {
        CoverageDetailsDto? details = null;
        var coverageBySlug = new Dictionary<string, CoverageDetailsDto>();

        if (!string.IsNullOrEmpty(scraperJson))
        {
            details = BuildCoverageDetailsFromScraperJson(scraperJson, q);
            coverageBySlug = BuildCoverageBySlug(scraperJson, q);
        }

        return new QuoteWithCoverageDto
        {
            Id               = q.Id,
            OpportunityId    = q.OpportunityId,
            LineOfBusinessId = q.LineOfBusinessId,
            CarrierName      = q.CarrierName,
            PremiumAmount    = q.PremiumAmount,
            CoverageDetails  = details,
            CoverageBySlug   = coverageBySlug,
            ReceivedAt       = q.ReceivedAt,
            QuoteLines       = q.QuoteLines.ToList(),
        };
    }

    private static IncumbentPolicyDto MapToIncumbentPolicyDto(IncumbentPolicy ip)
    {
        Dictionary<string, string> vals = new();
        if (!string.IsNullOrWhiteSpace(ip.Vals))
        {
            try
            {
                vals = JsonSerializer.Deserialize<Dictionary<string, string>>(ip.Vals,
                    new JsonSerializerOptions { PropertyNameCaseInsensitive = true }) ?? new();
            }
            catch { /* malformed JSON */ }
        }

        return new IncumbentPolicyDto
        {
            Id               = ip.Id,
            AccountId        = ip.AccountId,
            LineOfBusinessId = ip.LineOfBusinessId,
            CarrierName      = ip.CarrierName,
            PolicyNumber     = ip.PolicyNumber,
            AnnualPremium    = ip.AnnualPremium,
            EffectiveDate    = ip.EffectiveDate,
            ExpirationDate   = ip.ExpirationDate,
            Vals             = vals,
            SourceType       = ip.SourceType,
            IsOverridden     = ip.IsOverridden,
        };
    }

    private static readonly Dictionary<string, string> ScraperCovKeyToSlug =
        new(StringComparer.OrdinalIgnoreCase)
        {
            ["auto"]                   = "auto",
            ["general_liability"]      = "gl",
            ["workers_compensation"]   = "wc",
            ["umbrella"]               = "umb",
            ["inland_marine"]          = "mtc",
            ["professional_liability"] = "pl",
            ["pollution"]              = "pol",
            ["crime"]                  = "cr",
            ["cyber"]                  = "cyb",
            ["property"]               = "prop",
            ["management_liability"]   = "do",
            ["participant_accident"]   = "oppacc",
            ["bobtail"]                = "bt",
            ["trailer_interchange"]    = "ti",
            ["other"]                  = "other",
        };

    // Convert snake_case key to readable label
    private static string FormatLabel(string key) =>
        System.Globalization.CultureInfo.CurrentCulture.TextInfo
            .ToTitleCase(key.Replace('_', ' '));

    private static CoverageDetailsDto BuildSlugDto(string covKey, JsonElement covVal, string quoteId = "", string carrier = "")
    {
        var dto = new CoverageDetailsDto { Id = quoteId, Carrier = carrier };

        // Sections to skip entirely (handled separately or not useful as Vals)
        var skipSections = new HashSet<string> { "premium_summary", "endorsements", "exclusions",
            "driver_schedule", "vehicle_schedule", "location_schedule", "classifications",
            "classification_summary" };

        // Flatten all object/scalar sections into Vals
        foreach (var section in covVal.EnumerateObject())
        {
            if (skipSections.Contains(section.Name)) continue;

            if (section.Value.ValueKind == JsonValueKind.Object)
            {
                foreach (var prop in section.Value.EnumerateObject())
                {
                    if (prop.Value.ValueKind == JsonValueKind.Array) continue;
                    if (prop.Value.ValueKind == JsonValueKind.Null) continue;

                    if (prop.Value.ValueKind == JsonValueKind.Object)
                    {
                        // Two levels deep — flatten sub-properties
                        foreach (var subProp in prop.Value.EnumerateObject())
                        {
                            if (subProp.Value.ValueKind == JsonValueKind.Array) continue;
                            if (subProp.Value.ValueKind == JsonValueKind.Null) continue;
                            if (subProp.Value.ValueKind == JsonValueKind.Object) continue; // max 2 levels
                            var subVal = subProp.Value.ValueKind == JsonValueKind.String
                                ? subProp.Value.GetString() ?? ""
                                : subProp.Value.ValueKind == JsonValueKind.True ? "Yes"
                                : subProp.Value.ValueKind == JsonValueKind.False ? "No"
                                : subProp.Value.ToString();
                            if (!string.IsNullOrWhiteSpace(subVal))
                                dto.Vals[$"{FormatLabel(prop.Name)}: {FormatLabel(subProp.Name)}"] = subVal;
                        }
                    }
                    else
                    {
                        // Scalar (or True/False) — existing logic extended with bool support
                        var val = prop.Value.ValueKind == JsonValueKind.String
                            ? prop.Value.GetString() ?? ""
                            : prop.Value.ValueKind == JsonValueKind.True ? "Yes"
                            : prop.Value.ValueKind == JsonValueKind.False ? "No"
                            : prop.Value.ToString();
                        if (!string.IsNullOrWhiteSpace(val))
                            dto.Vals[FormatLabel(prop.Name)] = val;
                    }
                }
            }
            else if (section.Value.ValueKind == JsonValueKind.String ||
                     section.Value.ValueKind == JsonValueKind.Number ||
                     section.Value.ValueKind == JsonValueKind.True ||
                     section.Value.ValueKind == JsonValueKind.False)
            {
                var val = section.Value.ValueKind == JsonValueKind.String
                    ? section.Value.GetString() ?? ""
                    : section.Value.ValueKind == JsonValueKind.True ? "Yes"
                    : section.Value.ValueKind == JsonValueKind.False ? "No"
                    : section.Value.ToString();
                if (!string.IsNullOrWhiteSpace(val))
                    dto.Vals[FormatLabel(section.Name)] = val;
            }
            // Arrays (additional_coverages etc) — skip for now
        }

        // endorsements.schedule[].description → Includes
        if (covVal.TryGetProperty("endorsements", out var endt) &&
            endt.TryGetProperty("schedule", out var sched))
            foreach (var e in sched.EnumerateArray())
                if (e.TryGetProperty("description", out var desc) &&
                    desc.ValueKind == JsonValueKind.String)
                { var d = desc.GetString(); if (!string.IsNullOrEmpty(d)) dto.Includes.Add(d); }

        // exclusions.list[].name → Excludes
        if (covVal.TryGetProperty("exclusions", out var excl) &&
            excl.TryGetProperty("list", out var exclList))
            foreach (var ex in exclList.EnumerateArray())
                if (ex.TryGetProperty("name", out var nm) &&
                    nm.ValueKind == JsonValueKind.String)
                { var n = nm.GetString(); if (!string.IsNullOrEmpty(n)) dto.Excludes.Add(n); }

        // Premium from premium_summary
        if (covVal.TryGetProperty("premium_summary", out var ps) &&
            ps.TryGetProperty("total_premium", out var tp))
        {
            decimal p = 0;
            if (tp.ValueKind == JsonValueKind.Number) tp.TryGetDecimal(out p);
            else if (tp.ValueKind == JsonValueKind.String) decimal.TryParse(tp.GetString(), out p);
            if (p > 0) dto.Premium = p;
        }

        return dto;
    }

    private static CoverageDetailsDto BuildCoverageDetailsFromScraperJson(string resultJson, Quote q)
    {
        var dto = new CoverageDetailsDto { Id = q.Id.ToString(), Carrier = q.CarrierName };
        try
        {
            using var doc = JsonDocument.Parse(resultJson);
            var root = doc.RootElement;
            if (!root.TryGetProperty("result", out var result)) return dto;
            if (!result.TryGetProperty("results", out var results)) return dto;
            if (!results.TryGetProperty("coverages", out var coverages)) return dto;

            // Merge all coverage sections into one dto using BuildSlugDto (ADO#1164)
            foreach (var cov in coverages.EnumerateObject())
            {
                var slugDto = BuildSlugDto(cov.Name, cov.Value, q.Id.ToString(), q.CarrierName);

                // Merge Vals (namespaced with coverage key to avoid collisions)
                foreach (var kv in slugDto.Vals)
                    dto.Vals[$"{cov.Name}.{kv.Key}"] = kv.Value;

                // Merge Includes/Excludes (deduplicated)
                foreach (var inc in slugDto.Includes)
                    dto.Includes.Add(inc);
                foreach (var exc in slugDto.Excludes)
                    dto.Excludes.Add(exc);

                // Sum premiums (ADO#1149)
                dto.Premium += slugDto.Premium;
            }
        }
        catch { /* malformed JSON — return partial dto */ }
        return dto;
    }

    /// <summary>
    /// Builds a per-slug breakdown: each scraper coverage key → its own CoverageDetailsDto
    /// with scoped (non-namespaced) Vals, Includes, and Excludes.
    /// Uses BuildSlugDto to extract from actual scraper schema sections (ADO#1164).
    /// </summary>
    private static Dictionary<string, CoverageDetailsDto> BuildCoverageBySlug(string resultJson, Quote q)
    {
        var result = new Dictionary<string, CoverageDetailsDto>(StringComparer.OrdinalIgnoreCase);
        try
        {
            using var doc = JsonDocument.Parse(resultJson);
            var root = doc.RootElement;
            if (!root.TryGetProperty("result", out var res)) return result;
            if (!res.TryGetProperty("results", out var results)) return result;
            if (!results.TryGetProperty("coverages", out var coverages)) return result;

            foreach (var cov in coverages.EnumerateObject())
            {
                if (!ScraperCovKeyToSlug.TryGetValue(cov.Name, out var slug)) continue;

                var dto = BuildSlugDto(cov.Name, cov.Value, q.Id.ToString(), q.CarrierName);
                result[slug] = dto;
            }
        }
        catch { /* malformed JSON */ }
        return result;
    }

    private static DraftStateDto MapDraftToDto(ComparisonDraft d)
    {
        var opts = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };

        HashSet<string> checkedReqs = new();
        if (!string.IsNullOrWhiteSpace(d.ActiveRequirementSlugs))
        {
            try { checkedReqs = JsonSerializer.Deserialize<HashSet<string>>(d.ActiveRequirementSlugs, opts) ?? new(); }
            catch { }
        }

        Dictionary<string, Guid> pkgA = new();
        if (!string.IsNullOrWhiteSpace(d.PackageASelections))
        {
            try { pkgA = JsonSerializer.Deserialize<Dictionary<string, Guid>>(d.PackageASelections, opts) ?? new(); }
            catch { }
        }

        Dictionary<string, Guid> pkgB = new();
        if (!string.IsNullOrWhiteSpace(d.PackageBSelections))
        {
            try { pkgB = JsonSerializer.Deserialize<Dictionary<string, Guid>>(d.PackageBSelections, opts) ?? new(); }
            catch { }
        }

        HashSet<string> collapsed = new();
        if (!string.IsNullOrWhiteSpace(d.CollapsedBlocks))
        {
            try { collapsed = JsonSerializer.Deserialize<HashSet<string>>(d.CollapsedBlocks, opts) ?? new(); }
            catch { }
        }

        return new DraftStateDto
        {
            CheckedRequirements = checkedReqs,
            PackageASelections  = pkgA,
            PackageBSelections  = pkgB,
            ShowIncumbent       = d.ShowIncumbent,
            CollapsedBlocks     = collapsed,
        };
    }
}
