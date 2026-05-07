namespace FortressAI.V2.Web.Services;

public interface IFipTokenProvider
{
    Task<string?> GetAccessTokenAsync();
}
