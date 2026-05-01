using MySqlConnector;

namespace RisePortal.Web.Services;

public class TokenStorageService
{
    private readonly IConfiguration _config;
    private readonly ILogger<TokenStorageService> _logger;

    public TokenStorageService(IConfiguration config, ILogger<TokenStorageService> logger)
    {
        _config = config;
        _logger = logger;
    }

    public async Task StoreTokenAsync(
        string entraOid,
        string accessToken,
        string? refreshToken,
        DateTime expiresAt,
        string? scopes)
    {
        try
        {
            var connectionString = _config.GetConnectionString("RnFip");
            if (string.IsNullOrEmpty(connectionString))
            {
                _logger.LogWarning("RISE: No connection string — cannot store token");
                return;
            }

            await using var conn = new MySqlConnection(connectionString);
            await conn.OpenAsync();

            const string sql = @"
                INSERT INTO user_microsoft_tokens (entra_oid, access_token, refresh_token, expires_at, scopes)
                VALUES (@oid, @accessToken, @refreshToken, @expiresAt, @scopes)
                ON DUPLICATE KEY UPDATE
                    access_token = @accessToken,
                    refresh_token = COALESCE(@refreshToken, refresh_token),
                    expires_at = @expiresAt,
                    scopes = @scopes,
                    updated_at = CURRENT_TIMESTAMP";

            await using var cmd = new MySqlCommand(sql, conn);
            cmd.Parameters.AddWithValue("@oid", entraOid);
            cmd.Parameters.AddWithValue("@accessToken", accessToken);
            cmd.Parameters.AddWithValue("@refreshToken", (object?)refreshToken ?? DBNull.Value);
            cmd.Parameters.AddWithValue("@expiresAt", expiresAt);
            cmd.Parameters.AddWithValue("@scopes", (object?)scopes ?? DBNull.Value);

            await cmd.ExecuteNonQueryAsync();
            _logger.LogInformation("RISE: Token stored for OID {Oid}", entraOid);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "RISE: Failed to store token for OID {Oid}", entraOid);
        }
    }
}
