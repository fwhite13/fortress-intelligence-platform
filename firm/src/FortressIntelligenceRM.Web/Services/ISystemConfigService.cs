namespace FortressIntelligenceRM.Web.Services;

public interface ISystemConfigService
{
    /// <summary>Returns the value for <paramref name="key"/>, or null if unset.</summary>
    Task<string?> GetAsync(string key);

    /// <summary>Inserts or updates the value for <paramref name="key"/>.</summary>
    Task SetAsync(string key, string value);

    /// <summary>Removes <paramref name="key"/> if present.</summary>
    Task RemoveAsync(string key);
}
