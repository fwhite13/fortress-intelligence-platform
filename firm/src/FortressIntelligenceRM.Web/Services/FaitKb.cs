using Microsoft.Extensions.Configuration;

namespace FortressIntelligenceRM.Web.Services;

/// <summary>
/// WI #9743: the one master switch for the FAIT knowledge-base integration.
/// Config key <c>Firm:FaitKbEnabled</c> (env <c>Firm__FaitKbEnabled</c>), bool, default <c>true</c>
/// so FIRM behaviour is unchanged. Refuge Notetaker (RN) sets it to <c>false</c> in its task def:
/// then FIRM makes no outbound call to FAIT at all (meeting-complete, resolve-user, calendar proxy,
/// user-teams, manual KB pushes) and renders no KB UI. Every guard reads the key through this helper.
/// </summary>
public static class FaitKb
{
    public const string ConfigKey = "Firm:FaitKbEnabled";

    /// <summary>The historical default for <c>FIP:FaitApiUrl</c> (Fortress DEV FAIT). Reachable only
    /// through <see cref="ApiUrlOrDefault"/>, i.e. only when the switch is on.</summary>
    private const string FortressDevFaitUrl = "https://fait.dev.fortressam.ai";

    /// <summary>True when <c>Firm:FaitKbEnabled</c> is missing/empty (FIRM default) or "true"/"1".
    /// "false"/"0" turn it off; any other non-empty value also counts as off (fail closed: a value
    /// was set but cannot be read, so no meeting content leaves for FAIT).</summary>
    public static bool IsEnabled(IConfiguration config)
    {
        var raw = config[ConfigKey]?.Trim();
        if (string.IsNullOrEmpty(raw)) return true;
        if (bool.TryParse(raw, out var enabled)) return enabled;
        return raw == "1";
    }

    /// <summary>
    /// <c>FIP:FaitApiUrl</c> with the historical Fortress-dev fallback (same null-only fallback as before),
    /// or null when the switch is off. Callers that used <c>?? "https://fait.dev.fortressam.ai"</c>
    /// use this, so the fallback can never be reached with the switch off (fail closed).
    /// </summary>
    public static string? ApiUrlOrDefault(IConfiguration config)
    {
        if (!IsEnabled(config)) return null;
        return config["FIP:FaitApiUrl"] ?? FortressDevFaitUrl;
    }

    /// <summary>
    /// <c>FIP:FaitApiUrl</c> with no fallback (empty when unset), or empty when the switch is off.
    /// For callers that already treated a missing URL as "integration not configured".
    /// </summary>
    public static string ApiUrlOrEmpty(IConfiguration config)
        => IsEnabled(config) ? (config["FIP:FaitApiUrl"] ?? "") : "";

    public const string DisabledMessage = "FAIT knowledge base is disabled";
}
