using FortressAI.V2.Web.Data.Models;

namespace FortressAI.V2.Web.Services;

public interface IV1ProjectQueryService
{
    /// <summary>
    /// Read FAIT v1 projects from fait_dev schema on the same Aurora cluster.
    /// Filtered to projects owned by or accessible to the given entra OID user.
    /// </summary>
    Task<List<FaitV1Project>> GetV1ProjectsForUserAsync(string entraOid, CancellationToken ct = default);
}
