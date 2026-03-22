using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using FamOs.Web.Data;
using FamOs.Web.Data.Dtos;
using FamOs.Web.Data.Entities;

namespace FamOs.Web.Services;

public class IncumbentPolicyService : IIncumbentPolicyService
{
    private readonly IDbContextFactory<FamOsDbContext> _dbFactory;

    public IncumbentPolicyService(IDbContextFactory<FamOsDbContext> dbFactory)
    {
        _dbFactory = dbFactory;
    }

    /// <summary>Returns incumbent policies keyed by LineOfBusinessId.</summary>
    public async Task<Dictionary<Guid, IncumbentPolicyDto>> GetIncumbentForAccountAsync(Guid accountId, int tenantId)
    {
        await using var db = await _dbFactory.CreateDbContextAsync();

        var policies = await db.IncumbentPolicies
            .Where(ip => ip.AccountId == accountId && ip.TenantId == tenantId)
            .ToListAsync();

        return policies.ToDictionary(ip => ip.LineOfBusinessId, ip => MapToDto(ip));
    }

    public async Task<IncumbentPolicyDto> UpsertIncumbentAsync(IncumbentUpsertDto dto)
    {
        await using var db = await _dbFactory.CreateDbContextAsync();

        var valsJson = JsonSerializer.Serialize(dto.Vals);

        var existing = await db.IncumbentPolicies
            .FirstOrDefaultAsync(ip =>
                ip.AccountId        == dto.AccountId &&
                ip.LineOfBusinessId == dto.LineOfBusinessId &&
                ip.TenantId         == dto.TenantId);

        if (existing != null)
        {
            existing.CarrierName    = dto.CarrierName;
            existing.PolicyNumber   = dto.PolicyNumber;
            existing.AnnualPremium  = dto.AnnualPremium;
            existing.EffectiveDate  = dto.EffectiveDate;
            existing.ExpirationDate = dto.ExpirationDate;
            existing.Vals           = valsJson;
            existing.SourceType     = dto.SourceType;
            existing.UpdatedAt      = DateTime.UtcNow;

            if (dto.UserId.HasValue)
            {
                existing.IsOverridden        = true;
                existing.OverriddenByUserId  = dto.UserId;
                existing.OverriddenAt        = DateTime.UtcNow;
            }

            await db.SaveChangesAsync();
            return MapToDto(existing);
        }
        else
        {
            var policy = new IncumbentPolicy
            {
                AccountId        = dto.AccountId,
                LineOfBusinessId = dto.LineOfBusinessId,
                TenantId         = dto.TenantId,
                CarrierName      = dto.CarrierName,
                PolicyNumber     = dto.PolicyNumber,
                AnnualPremium    = dto.AnnualPremium,
                EffectiveDate    = dto.EffectiveDate,
                ExpirationDate   = dto.ExpirationDate,
                Vals             = valsJson,
                SourceType       = dto.SourceType,
            };

            db.IncumbentPolicies.Add(policy);
            await db.SaveChangesAsync();
            return MapToDto(policy);
        }
    }

    // ── Mapping helpers ────────────────────────────────────────────────────────

    private static IncumbentPolicyDto MapToDto(IncumbentPolicy ip)
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
}
