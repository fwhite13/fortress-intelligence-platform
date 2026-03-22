using Microsoft.EntityFrameworkCore;
using FamOs.Web.Data;
using FamOs.Web.Data.Dtos;
using FamOs.Web.Data.Entities;

namespace FamOs.Web.Services;

public class PackageService : IPackageService
{
    private readonly IDbContextFactory<FamOsDbContext> _dbFactory;

    public PackageService(IDbContextFactory<FamOsDbContext> dbFactory)
    {
        _dbFactory = dbFactory;
    }

    public async Task<PackageDto> SavePackageAsync(Guid accountId, Guid userId, int tenantId, PackageSaveDto dto)
    {
        await using var db = await _dbFactory.CreateDbContextAsync();

        Package package;

        if (dto.Id.HasValue)
        {
            package = await db.Packages
                .FirstOrDefaultAsync(p => p.Id == dto.Id.Value && p.AccountId == accountId && p.TenantId == tenantId)
                ?? throw new KeyNotFoundException($"Package {dto.Id} not found");

            package.Label                = dto.Label;
            package.LastModifiedByUserId = userId;
            package.UpdatedAt            = DateTime.UtcNow;

            // Remove existing selections and re-insert
            var existing = await db.PackageSelections
                .Where(s => s.PackageId == package.Id)
                .ToListAsync();
            db.PackageSelections.RemoveRange(existing);
        }
        else
        {
            package = new Package
            {
                AccountId         = accountId,
                TenantId          = tenantId,
                Label             = dto.Label,
                Status            = "draft",
                CreatedByUserId   = userId,
            };
            db.Packages.Add(package);
        }

        // Build selections with bundle rules applied
        var quoteIds = dto.Selections.Select(s => s.QuoteId).ToList();
        var quotes   = await db.Quotes
            .Where(q => quoteIds.Contains(q.Id))
            .ToListAsync();

        var quoteWithCoverage = quotes.Select(q => new QuoteWithCoverageDto
        {
            Id               = q.Id,
            LineOfBusinessId = q.LineOfBusinessId,
            CarrierName      = q.CarrierName,
            PremiumAmount    = q.PremiumAmount,
        }).ToList();

        var rules = await db.CarrierBundleRules
            .Where(r => r.TenantId == tenantId && r.IsActive)
            .ToListAsync();

        ApplyBundleRules(dto, quoteWithCoverage, rules);

        // Recalculate total premium from final selections
        var finalQuoteIds    = dto.Selections.Select(s => s.QuoteId).ToList();
        var finalQuotes      = await db.Quotes.Where(q => finalQuoteIds.Contains(q.Id)).ToListAsync();
        package.TotalPremium = finalQuotes.Sum(q => q.PremiumAmount);

        foreach (var sel in dto.Selections)
        {
            db.PackageSelections.Add(new PackageSelection
            {
                PackageId        = package.Id,
                LineOfBusinessId = sel.LineOfBusinessId,
                QuoteId          = sel.QuoteId,
                IsAutoBundle     = sel.IsAutoBundle,
                TenantId         = tenantId,
            });
        }

        await db.SaveChangesAsync();

        return new PackageDto
        {
            Id           = package.Id,
            AccountId    = package.AccountId,
            Label        = package.Label,
            Status       = package.Status,
            TotalPremium = package.TotalPremium,
            Selections   = dto.Selections.ToList(),
        };
    }

    public async Task<List<PackageDto>> GetPackagesForAccountAsync(Guid accountId, int tenantId)
    {
        await using var db = await _dbFactory.CreateDbContextAsync();

        var packages = await db.Packages
            .Where(p => p.AccountId == accountId && p.TenantId == tenantId)
            .OrderBy(p => p.Label)
            .ToListAsync();

        var packageIds = packages.Select(p => p.Id).ToList();
        var selections = await db.PackageSelections
            .Where(s => packageIds.Contains(s.PackageId))
            .ToListAsync();

        return packages.Select(p => new PackageDto
        {
            Id           = p.Id,
            AccountId    = p.AccountId,
            Label        = p.Label,
            Status       = p.Status,
            TotalPremium = p.TotalPremium,
            Selections   = selections
                .Where(s => s.PackageId == p.Id)
                .Select(s => new PackageSelectionDto
                {
                    LineOfBusinessId = s.LineOfBusinessId,
                    QuoteId          = s.QuoteId,
                    IsAutoBundle     = s.IsAutoBundle,
                })
                .ToList(),
        }).ToList();
    }

    public async Task DeletePackageAsync(Guid packageId, Guid userId, int tenantId)
    {
        await using var db = await _dbFactory.CreateDbContextAsync();

        var package = await db.Packages
            .FirstOrDefaultAsync(p => p.Id == packageId && p.TenantId == tenantId)
            ?? throw new KeyNotFoundException($"Package {packageId} not found");

        var selections = await db.PackageSelections
            .Where(s => s.PackageId == packageId)
            .ToListAsync();

        db.PackageSelections.RemoveRange(selections);
        db.Packages.Remove(package);

        await db.SaveChangesAsync();
    }

    /// <summary>
    /// Pure logic: for each selected primary line, auto-adds required lines from the same carrier
    /// if available in the provided quotes and not already selected. Mutates dto.Selections.
    /// </summary>
    public void ApplyBundleRules(PackageSaveDto package, List<QuoteWithCoverageDto> quotes, List<CarrierBundleRule> rules)
    {
        // Build a lookup: lineOfBusinessId -> lineSlug (we only have lobId on QuoteWithCoverageDto)
        // Bundle rules use slugs, so we match by carrier name
        var selectionsById = package.Selections.ToDictionary(s => s.LineOfBusinessId, s => s.QuoteId);

        var added = new List<PackageSelectionDto>();

        foreach (var sel in package.Selections.ToList())
        {
            var selectedQuote = quotes.FirstOrDefault(q => q.Id == sel.QuoteId);
            if (selectedQuote == null) continue;

            // Find rules triggered by this carrier for any primary line matching this LOB
            var applicableRules = rules.Where(r =>
                r.CarrierName.Equals(selectedQuote.CarrierName, StringComparison.OrdinalIgnoreCase));

            foreach (var rule in applicableRules)
            {
                // Find a quote from the same carrier for the required line slug
                // We match required line by carrier name; the required LOB quote must exist
                var requiredQuote = quotes.FirstOrDefault(q =>
                    q.CarrierName.Equals(rule.CarrierName, StringComparison.OrdinalIgnoreCase)
                    && q.LineOfBusinessId.HasValue
                    && !selectionsById.ContainsKey(q.LineOfBusinessId.Value)
                    && !added.Any(a => a.LineOfBusinessId == q.LineOfBusinessId!.Value));

                if (requiredQuote?.LineOfBusinessId == null) continue;

                added.Add(new PackageSelectionDto
                {
                    LineOfBusinessId = requiredQuote.LineOfBusinessId.Value,
                    QuoteId          = requiredQuote.Id,
                    IsAutoBundle     = true,
                });
            }
        }

        package.Selections.AddRange(added);
    }
}
