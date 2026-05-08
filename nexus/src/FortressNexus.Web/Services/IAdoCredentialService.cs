namespace FortressNexus.Web.Services;

public interface IAdoCredentialService
{
    Task<bool> HasCredentialAsync(string userUpn);
    Task SaveCredentialAsync(string userUpn, string rawPat);
    Task<string?> GetDecryptedPatAsync(string userUpn);
    Task<string?> GetPatHintAsync(string userUpn);
    Task DeleteCredentialAsync(string userUpn);
    Task<List<string>> GetProjectsAsync(string userUpn);   // calls ADO API with stored PAT
    Task<bool> ValidatePatAsync(string rawPat);             // calls GET /_apis/projects, returns true if 200
}
