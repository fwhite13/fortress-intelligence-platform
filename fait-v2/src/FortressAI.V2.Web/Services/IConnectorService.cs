namespace FortressAI.V2.Web.Services;

public interface IConnectorService
{
    /// <summary>List all active MCP connectors accessible to this user.</summary>
    Task<IReadOnlyList<ConnectorViewModel>> ListConnectorsAsync(string entraOid, CancellationToken ct = default);

    /// <summary>Get the user's connection status for a specific connector.</summary>
    Task<ConnectorStatus> GetConnectionStatusAsync(string entraOid, string serverName, CancellationToken ct = default);

    /// <summary>Revoke a user's OAuth token for a connector.</summary>
    Task RevokeConnectionAsync(string entraOid, string serverName, CancellationToken ct = default);
}

public record ConnectorViewModel(
    string Name,
    string DisplayName,
    string Description,
    bool IsConnected,
    bool CanRead,
    bool CanWrite,
    ConnectorAuthType AuthType,
    DateTime? ConnectedAt
);

public enum ConnectorAuthType { OAuthEntra, ApiKey, None }
public enum ConnectorStatus { Connected, NotConnected, TokenExpired }
