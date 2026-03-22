using FamOs.Web.Data.Dtos;
using FamOs.Web.Data.Entities;

namespace FamOs.Web.Services;

public interface IPackageService
{
    Task<PackageDto> SavePackageAsync(Guid accountId, Guid userId, int tenantId, PackageSaveDto dto);
    Task<List<PackageDto>> GetPackagesForAccountAsync(Guid accountId, int tenantId);
    Task DeletePackageAsync(Guid packageId, Guid userId, int tenantId);
    void ApplyBundleRules(PackageSaveDto package, List<QuoteWithCoverageDto> quotes, List<CarrierBundleRule> rules);
}
