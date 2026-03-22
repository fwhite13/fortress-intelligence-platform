using FamOs.Web.Data.Dtos;

namespace FamOs.Web.Services;

public interface IIncumbentPolicyService
{
    Task<Dictionary<Guid, IncumbentPolicyDto>> GetIncumbentForAccountAsync(Guid accountId, int tenantId);
    Task<IncumbentPolicyDto> UpsertIncumbentAsync(IncumbentUpsertDto dto);
}
