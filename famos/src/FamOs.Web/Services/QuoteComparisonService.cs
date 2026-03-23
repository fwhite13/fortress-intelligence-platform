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

    public async Task<ComparisonContextDto> GetComparisonContextAsync(Guid accountId, Guid userId, int tenantId)
    {
        await using var db = await _dbFactory.CreateDbContextAsync();

        var account = await db.Accounts.FirstOrDefaultAsync(a => a.Id == accountId)
            ?? throw new KeyNotFoundException($"Account {accountId} not found");

        ProgramVertical? vertical = null;
        List<LineOfBusiness> lines = new();
        List<Requirement> requirements = new();
        List<BenchmarkPremium> benchmarkList = new();

        if (account.ProgramVerticalId.HasValue)
        {
            vertical = await db.ProgramVerticals
                .FirstOrDefaultAsync(v => v.Id == account.ProgramVerticalId.Value && v.TenantId == tenantId);

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

                // Take latest benchmark per line
                benchmarkList = await db.BenchmarkPremiums
                    .Where(bp => bp.ProgramVerticalId == vertical.Id && bp.TenantId == tenantId)
                    .ToListAsync();
            }
        }

        // Quotes via opportunities linked by AffinityId
        var oppIds = await db.Opportunities
            .Where(o => o.AffinityId == account.AffinityId)
            .Select(o => o.Id)
            .ToListAsync();

        var rawQuotes = await db.Quotes
            .Where(q => oppIds.Contains(q.OpportunityId) && q.TenantId == tenantId)
            .ToListAsync();

        var quotes = rawQuotes.Select(MapToQuoteWithCoverageDto).ToList();

        // Incumbents keyed by LineOfBusinessId
        var incumbentList = await db.IncumbentPolicies
            .Where(ip => ip.AccountId == accountId && ip.TenantId == tenantId)
            .ToListAsync();

        var incumbents = incumbentList.ToDictionary(
            ip => ip.LineOfBusinessId,
            ip => MapToIncumbentPolicyDto(ip));

        // Latest benchmark per line
        var benchmarks = benchmarkList
            .GroupBy(bp => bp.LineOfBusinessId)
            .ToDictionary(
                g => g.Key,
                g => g.OrderByDescending(bp => bp.EffectiveDate).First().AnnualPremium);

        var bundleRules = await db.CarrierBundleRules
            .Where(r => r.TenantId == tenantId && r.IsActive)
            .ToListAsync();

        var draft = await db.ComparisonDrafts
            .FirstOrDefaultAsync(d => d.AccountId == accountId && d.UserId == userId && d.TenantId == tenantId);

        return new ComparisonContextDto
        {
            Account         = account,
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

        return rawQuotes.Select(MapToQuoteWithCoverageDto).ToList();
    }

    public async Task SaveDraftAsync(Guid accountId, Guid userId, int tenantId, DraftStateDto dto)
    {
        await using var db = await _dbFactory.CreateDbContextAsync();

        var existing = await db.ComparisonDrafts
            .FirstOrDefaultAsync(d => d.AccountId == accountId && d.UserId == userId && d.TenantId == tenantId);

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
                AccountId              = accountId,
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

    public async Task<Guid> BuildProposalAsync(Guid accountId, Guid userId, Guid packageId, int tenantId)
    {
        await using var db = await _dbFactory.CreateDbContextAsync();

        var package = await db.Packages
            .FirstOrDefaultAsync(p => p.Id == packageId && p.AccountId == accountId && p.TenantId == tenantId)
            ?? throw new KeyNotFoundException($"Package {packageId} not found for account {accountId}");

        var selections = await db.PackageSelections
            .Where(s => s.PackageId == packageId && s.TenantId == tenantId)
            .ToListAsync();

        if (selections.Count == 0)
            throw new InvalidOperationException("Package has no selections — cannot build proposal");

        // Find the opportunity for this account via AffinityId
        var account = await db.Accounts.FirstOrDefaultAsync(a => a.Id == accountId)
            ?? throw new KeyNotFoundException($"Account {accountId} not found");

        var opportunity = await db.Opportunities
            .Where(o => o.AffinityId == account.AffinityId && !o.IsClosed)
            .OrderByDescending(o => o.UpdatedAt)
            .FirstOrDefaultAsync()
            ?? throw new InvalidOperationException($"No active opportunity found for account {accountId}");

        var primaryQuoteId = selections.First().QuoteId;

        var proposal = new Proposal
        {
            OpportunityId     = opportunity.Id,
            RecommendedQuoteId = primaryQuoteId,
            Status            = "draft",
            ProposalDate      = DateTime.UtcNow,
        };

        db.Proposals.Add(proposal);
        await db.SaveChangesAsync();

        return proposal.Id;
    }

    public async Task<Guid?> ResolveAccountIdFromOpportunityAsync(Guid opportunityId, int tenantId)
    {
        await using var db = await _dbFactory.CreateDbContextAsync();
        var opp = await db.Opportunities.FindAsync(opportunityId);
        if (opp == null) return null;
        var account = await db.Accounts
            .FirstOrDefaultAsync(a => a.AffinityId == opp.AffinityId);
        return account?.Id;
    }

    // ── Mapping helpers ────────────────────────────────────────────────────────

    private static QuoteWithCoverageDto MapToQuoteWithCoverageDto(Quote q)
    {
        CoverageDetailsDto? details = null;
        if (!string.IsNullOrWhiteSpace(q.CoverageDetails))
        {
            try
            {
                details = JsonSerializer.Deserialize<CoverageDetailsDto>(q.CoverageDetails,
                    new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
            }
            catch { /* malformed JSON — leave null */ }
        }

        return new QuoteWithCoverageDto
        {
            Id               = q.Id,
            OpportunityId    = q.OpportunityId,
            LineOfBusinessId = q.LineOfBusinessId,
            CarrierName      = q.CarrierName,
            PremiumAmount    = q.PremiumAmount,
            CoverageDetails  = details,
            ReceivedAt       = q.ReceivedAt,
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
