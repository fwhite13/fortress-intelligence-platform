namespace FamOs.Web.Services;

public interface IIntakeResponseService
{
    Task UpsertAsync(string opportunityId, string fieldCode, string value);
    Task<Dictionary<string, string>> LoadAllAsync(string opportunityId);
}
