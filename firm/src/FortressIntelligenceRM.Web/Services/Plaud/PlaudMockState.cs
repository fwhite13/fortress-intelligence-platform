using System.Globalization;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;

namespace FortressIntelligenceRM.Web.Services.Plaud;

/// <summary>
/// WI #9138 — DEV-ONLY in-memory state for <c>PlaudMockController</c> (the mock Plaud OAuth + MCP server
/// under /dev/plaud-mock). Registered as a singleton; holds issued codes, tokens, registered clients, MCP
/// sessions, scenario switches and the fixture recording catalog. Nothing is persisted: a restart (or a
/// second ECS task) forgets every token, which the real client then sees as invalid_grant / 401.
/// Holds no secrets of any real system; still, tokens are only ever shown masked.
/// </summary>
public class PlaudMockState
{
    public const string FakeUserId = "mock-user-1";
    public const string FakeUserEmail = "plaud.mock@example.com";
    public const string FakeUserName = "Plaud Mock";
    public const int DefaultExpiresIn = 3600;
    private const int MaxEntries = 2000;
    private static readonly TimeSpan CodeLifetime = TimeSpan.FromMinutes(10);

    /// <summary>
    /// WI #9138-b — the app configuration, captured when DI first builds this singleton (the first mock request).
    /// Read live by <see cref="IsMockEnabled"/>; null (so false) in a process where the mock was never resolved.
    /// </summary>
    private static volatile IConfiguration? s_config;

    public PlaudMockState(IConfiguration config) => s_config = config;

    /// <summary>
    /// True only while Firm:PlaudMock:Enabled is exactly true in this process's configuration. Gates
    /// <see cref="PlaudMcpClient.ReadPresignedUrl"/> accepting the mock's http://localhost audio link.
    /// </summary>
    public static bool IsMockEnabled =>
        s_config is { } c && c.GetValue<bool>("Firm:PlaudMock:Enabled");

    private readonly object _lock = new();
    private readonly Dictionary<string, MockClient> _clients = new(StringComparer.Ordinal);
    private readonly Dictionary<string, MockCode> _codes = new(StringComparer.Ordinal);
    private readonly Dictionary<string, AccessEntry> _access = new(StringComparer.Ordinal);
    private readonly Dictionary<string, RefreshEntry> _refresh = new(StringComparer.Ordinal);
    private readonly HashSet<string> _revokedGrants = new(StringComparer.Ordinal);
    private readonly Dictionary<string, DateTime> _sessions = new(StringComparer.Ordinal);
    private readonly List<AddedRecording> _added = new();
    private readonly Dictionary<string, int> _counters = new(StringComparer.Ordinal);

    // ── Scenario switches (POST /dev/plaud-mock/admin/scenario) ──────────
    public int ExpiresIn { get; private set; } = DefaultExpiresIn;
    public bool RotateRefresh { get; private set; }
    public bool FailTokenNext { get; private set; }
    public int RateLimit429Remaining { get; private set; }
    public bool ForceSse { get; private set; }

    public record MockClient(string ClientId, string? ClientSecret, IReadOnlyList<string> RedirectUris,
        string ClientName, string AuthMethod, DateTime RegisteredAt);
    private record MockCode(string ClientId, string RedirectUri, string CodeChallenge, DateTime ExpiresAt);
    private record AccessEntry(string ClientId, string GrantId, DateTime IssuedAt, DateTime ExpiresAt);
    private record RefreshEntry(string ClientId, string GrantId, DateTime IssuedAt);
    private record AddedRecording(string Id, string Name, DateTime StartAt, string Clip);

    public record TokenIssue(string AccessToken, string? RefreshToken, int ExpiresIn);

    /// <summary>One recording as list_files / get_file describe it. Dates are UTC.</summary>
    public record MockRecording(string Id, string Name, DateTime StartAt, DateTime CreatedAt, long DurationMs,
        string SerialNumber, string Clip);

    /// <summary>One fixture clip: the audio bytes (m4a) and its transcript lines.</summary>
    public record FixtureClip(string Name, byte[] Audio, long DurationMs, IReadOnlyList<Utterance> Utterances);
    public record Utterance(long StartMs, long EndMs, string Speaker, string Text);

    // ── Clients (RFC 7591) ───────────────────────────────────────────────

    public MockClient RegisterClient(IReadOnlyList<string> redirectUris, string clientName, string authMethod)
    {
        var id = "mock_client_" + RandomToken(12);
        var secret = authMethod == "none" ? null : "mock_secret_" + RandomToken(24);
        var client = new MockClient(id, secret, redirectUris, clientName, authMethod, DateTime.UtcNow);
        lock (_lock)
        {
            Trim(_clients);
            _clients[id] = client;
            Count("register");
        }
        return client;
    }

    /// <summary>
    /// WI #9139-b — registers <paramref name="clientId"/> as a public client (no secret) with the single
    /// <paramref name="redirectUri"/>, unless it is already registered, in which case the existing client is
    /// returned unchanged and <paramref name="created"/> is false. The caller decides when this is allowed
    /// (only for Firm:Plaud:RedirectUri): FIRM caches its client_id in firm_oauth_clients, this mock forgets
    /// its clients on every restart.
    /// </summary>
    public MockClient RegisterImplicitClient(string clientId, string redirectUri, out bool created)
    {
        lock (_lock)
        {
            if (_clients.TryGetValue(clientId, out var existing))
            {
                created = false;
                return existing;
            }
            Trim(_clients);
            var client = new MockClient(clientId, null, new[] { redirectUri }, "", "none", DateTime.UtcNow);
            _clients[clientId] = client;
            Count("register_implicit");
            created = true;
            return client;
        }
    }

    public MockClient? FindClient(string? clientId)
    {
        if (string.IsNullOrEmpty(clientId)) return null;
        lock (_lock)
            return _clients.TryGetValue(clientId, out var c) ? c : null;
    }

    // ── Authorization codes ──────────────────────────────────────────────

    public string IssueCode(string clientId, string redirectUri, string codeChallenge)
    {
        var code = "mock_code_" + RandomToken(24);
        lock (_lock)
        {
            foreach (var expired in _codes.Where(kv => kv.Value.ExpiresAt < DateTime.UtcNow).Select(kv => kv.Key).ToList())
                _codes.Remove(expired);
            Trim(_codes);
            _codes[code] = new MockCode(clientId, redirectUri, codeChallenge, DateTime.UtcNow + CodeLifetime);
            Count("code_issued");
        }
        return code;
    }

    /// <summary>
    /// Single use: the code is removed before it is checked, so a second exchange of the same code (or a
    /// retry after a wrong verifier) gets invalid_grant. Returns the OAuth error code, or null on success.
    /// </summary>
    public string? RedeemCode(string code, string clientId, string redirectUri, string codeVerifier, out TokenIssue? tokens)
    {
        tokens = null;
        lock (_lock)
        {
            if (!_codes.Remove(code, out var entry) || entry.ExpiresAt < DateTime.UtcNow)
                return "invalid_grant";
            if (!string.Equals(entry.ClientId, clientId, StringComparison.Ordinal)
                || !string.Equals(entry.RedirectUri, redirectUri, StringComparison.Ordinal))
                return "invalid_grant";
            if (!VerifyS256(codeVerifier, entry.CodeChallenge))
                return "invalid_grant";

            var grantId = RandomToken(8);
            tokens = IssueTokens(clientId, grantId, withRefresh: true);
            Count("token_authorization_code");
            return null;
        }
    }

    /// <summary>RFC 7636 S256: BASE64URL(SHA256(ASCII(verifier))) == challenge; verifier 43–128 unreserved chars.</summary>
    public static bool VerifyS256(string verifier, string challenge)
    {
        if (verifier.Length is < 43 or > 128) return false;
        if (!verifier.All(c => char.IsAsciiLetterOrDigit(c) || c is '-' or '.' or '_' or '~')) return false;
        var hash = SHA256.HashData(Encoding.ASCII.GetBytes(verifier));
        var computed = Base64Url(hash);
        return CryptographicOperations.FixedTimeEquals(Encoding.ASCII.GetBytes(computed), Encoding.ASCII.GetBytes(challenge));
    }

    // ── Tokens ───────────────────────────────────────────────────────────

    /// <summary>refresh_token grant. Returns the OAuth error code, or null on success.</summary>
    public string? Refresh(string refreshToken, string clientId, out TokenIssue? tokens)
    {
        tokens = null;
        lock (_lock)
        {
            if (!_refresh.TryGetValue(refreshToken, out var entry) || _revokedGrants.Contains(entry.GrantId))
                return "invalid_grant";
            if (!string.Equals(entry.ClientId, clientId, StringComparison.Ordinal))
                return "invalid_grant";

            if (RotateRefresh)
            {
                // Rotation: the old refresh token is dead from now on (like Plaud's "dead refresh" cache).
                _refresh.Remove(refreshToken);
                tokens = IssueTokens(clientId, entry.GrantId, withRefresh: true);
            }
            else
            {
                // No rotation: no refresh_token in the response; the client must keep the old one.
                tokens = IssueTokens(clientId, entry.GrantId, withRefresh: false);
            }
            Count("token_refresh");
            return null;
        }
    }

    private TokenIssue IssueTokens(string clientId, string grantId, bool withRefresh)
    {
        var now = DateTime.UtcNow;
        var access = "mock_at_" + RandomToken(32);
        foreach (var expired in _access.Where(kv => kv.Value.ExpiresAt < now).Select(kv => kv.Key).ToList())
            _access.Remove(expired);
        Trim(_access);
        _access[access] = new AccessEntry(clientId, grantId, now, now.AddSeconds(ExpiresIn));
        string? refresh = null;
        if (withRefresh)
        {
            refresh = "mock_rt_" + RandomToken(32);
            Trim(_refresh);
            _refresh[refresh] = new RefreshEntry(clientId, grantId, now);
        }
        return new TokenIssue(access, refresh, ExpiresIn);
    }

    /// <summary>True when the bearer token was issued by this mock, is unexpired and its grant is not revoked.</summary>
    public bool ValidateAccessToken(string? token)
    {
        if (string.IsNullOrEmpty(token)) return false;
        lock (_lock)
            return _access.TryGetValue(token, out var e) && e.ExpiresAt > DateTime.UtcNow && !_revokedGrants.Contains(e.GrantId);
    }

    /// <summary>failToken: true once (and clears itself) when the next token call must return 500.</summary>
    public bool ConsumeFailToken()
    {
        lock (_lock)
        {
            if (!FailTokenNext) return false;
            FailTokenNext = false;
            Count("token_forced_500");
            return true;
        }
    }

    /// <summary>rateLimit429: true (and decrements) while list_files must answer 429.</summary>
    public bool ConsumeRateLimit()
    {
        lock (_lock)
        {
            if (RateLimit429Remaining <= 0) return false;
            RateLimit429Remaining--;
            Count("list_files_429");
            return true;
        }
    }

    // ── MCP sessions ─────────────────────────────────────────────────────

    public string NewSession()
    {
        var id = RandomToken(16);
        lock (_lock)
        {
            Trim(_sessions);
            _sessions[id] = DateTime.UtcNow;
        }
        return id;
    }

    public bool HasSession(string id)
    {
        lock (_lock)
            return _sessions.ContainsKey(id);
    }

    public void EndSession(string id)
    {
        lock (_lock)
            _sessions.Remove(id);
    }

    public void Count(string name)
    {
        lock (_lock)
            _counters[name] = _counters.GetValueOrDefault(name) + 1;
    }

    // ── Scenario ─────────────────────────────────────────────────────────

    public record ScenarioRequest(int? ExpiresIn, bool? RotateRefresh, bool? RevokeGrants, bool? FailToken,
        int? RateLimit429, bool? AddRecording, string? AddRecordingName, bool? ForceSse, bool? Reset);

    public void Apply(ScenarioRequest s)
    {
        lock (_lock)
        {
            if (s.Reset == true)
            {
                ExpiresIn = DefaultExpiresIn;
                RotateRefresh = false;
                FailTokenNext = false;
                RateLimit429Remaining = 0;
                ForceSse = false;
                _added.Clear();
            }
            if (s.ExpiresIn is int e)
                ExpiresIn = Math.Clamp(e, 10, 86400);
            if (s.RotateRefresh is bool r)
                RotateRefresh = r;
            if (s.FailToken is bool f)
                FailTokenNext = f;
            if (s.RateLimit429 is int n)
                RateLimit429Remaining = Math.Clamp(n, 0, 1000);
            if (s.ForceSse is bool sse)
                ForceSse = sse;
            if (s.RevokeGrants == true)
            {
                // Every grant issued so far is revoked: its access tokens get 401 and its refresh tokens
                // invalid_grant. A new Connect (authorization_code) starts a fresh, valid grant.
                foreach (var g in _access.Values.Select(a => a.GrantId).Concat(_refresh.Values.Select(r2 => r2.GrantId)))
                    _revokedGrants.Add(g);
                Count("revoke_grants");
            }
            if (s.AddRecording == true)
            {
                var n2 = _added.Count + 1;
                var clips = new[] { "weekly-sync", "client-call", "older-note-a", "older-note-b" };
                var name = string.IsNullOrWhiteSpace(s.AddRecordingName)
                    ? $"Mock: Added recording {n2}"
                    : s.AddRecordingName.Trim()[..Math.Min(s.AddRecordingName.Trim().Length, 120)];
                _added.Add(new AddedRecording($"mock-added-{n2:D4}-{RandomToken(4).ToLowerInvariant()}", name,
                    DateTime.UtcNow, clips[(n2 - 1) % clips.Length]));
            }
        }
    }

    /// <summary>Admin view: switches, counts and masked tokens only (never a full token or code).</summary>
    public object Snapshot()
    {
        lock (_lock)
        {
            var now = DateTime.UtcNow;
            return new
            {
                switches = new { expiresIn = ExpiresIn, rotateRefresh = RotateRefresh, failToken = FailTokenNext,
                    rateLimit429 = RateLimit429Remaining, forceSse = ForceSse },
                clients = _clients.Values.Select(c => new { clientId = Mask(c.ClientId), c.ClientName, c.AuthMethod,
                    redirectUriCount = c.RedirectUris.Count, c.RegisteredAt }),
                pendingCodes = _codes.Count(kv => kv.Value.ExpiresAt > now),
                accessTokens = _access.OrderByDescending(kv => kv.Value.IssuedAt).Take(50).Select(kv => new
                {
                    token = Mask(kv.Key), clientId = Mask(kv.Value.ClientId), grant = Mask(kv.Value.GrantId),
                    kv.Value.IssuedAt, kv.Value.ExpiresAt, expired = kv.Value.ExpiresAt <= now,
                    revoked = _revokedGrants.Contains(kv.Value.GrantId),
                }),
                refreshTokens = _refresh.OrderByDescending(kv => kv.Value.IssuedAt).Take(50).Select(kv => new
                {
                    token = Mask(kv.Key), clientId = Mask(kv.Value.ClientId), grant = Mask(kv.Value.GrantId),
                    kv.Value.IssuedAt, revoked = _revokedGrants.Contains(kv.Value.GrantId),
                }),
                mcpSessions = _sessions.Count,
                addedRecordings = _added.Select(a => new { a.Id, a.StartAt }),
                counters = new Dictionary<string, int>(_counters),
            };
        }
    }

    // ── Recordings (dates relative to now on every call) ─────────────────

    public IReadOnlyList<MockRecording> Recordings()
    {
        var now = DateTime.UtcNow;
        long Dur(string clip) => Clip(clip)?.DurationMs ?? 60_000;
        MockRecording Started(string id, string name, DateTime start, long dur, string clip, string sn) =>
            new(id, name, start, start.AddMilliseconds(dur), dur, sn, clip);

        var list = new List<MockRecording>
        {
            // Started about an hour ago, ~60 s of audio.
            Started("mock-0001-weekly-sync", "Mock: Weekly sync (recent)", now.AddHours(-1), Dur("weekly-sync"), "weekly-sync", "MOCK-NP-0001"),
            // Created about 30 hours ago: > 24 h old, so D14 suppresses email and push.
            new("mock-0002-client-call", "Mock: Client call (yesterday)", now.AddHours(-30).AddMilliseconds(-Dur("client-call")),
                now.AddHours(-30), Dur("client-call"), "MOCK-NP-0001", "client-call"),
            // 5 hours: over MaxDurationMinutes (240), so the importer marks it too_long from the list.
            Started("mock-0003-all-day-workshop", "Mock: All-day workshop", now.AddHours(-8), 5L * 60 * 60 * 1000, "weekly-sync", "MOCK-NP-0002"),
            // Two older notes, 2 days old, short audio: shows the 3-day window and the in-flight caps.
            Started("mock-0004-older-note-a", "Mock: Older note A", now.AddDays(-2), Dur("older-note-a"), "older-note-a", "MOCK-NP-0001"),
            Started("mock-0005-older-note-b", "Mock: Older note B", now.AddDays(-2).AddHours(-2), Dur("older-note-b"), "older-note-b", "MOCK-NP-0002"),
        };
        lock (_lock)
            list.AddRange(_added.Select(a => Started(a.Id, a.Name, a.StartAt, Dur(a.Clip), a.Clip, "MOCK-NP-0001")));
        return list.OrderByDescending(r => r.StartAt).ToList();
    }

    public MockRecording? FindRecording(string? id) =>
        string.IsNullOrEmpty(id) ? null : Recordings().FirstOrDefault(r => r.Id == id);

    // ── Fixture clips (embedded resources PlaudMock.<name>.m4a / .transcript.tsv) ──

    private static readonly Lazy<Dictionary<string, FixtureClip>> Clips = new(LoadClips);

    public static FixtureClip? Clip(string name) => Clips.Value.TryGetValue(name, out var c) ? c : null;

    private static Dictionary<string, FixtureClip> LoadClips()
    {
        var asm = Assembly.GetExecutingAssembly();
        var result = new Dictionary<string, FixtureClip>(StringComparer.Ordinal);
        foreach (var res in asm.GetManifestResourceNames().Where(n => n.StartsWith("PlaudMock.") && n.EndsWith(".m4a")))
        {
            var name = res["PlaudMock.".Length..^".m4a".Length];
            using var audioStream = asm.GetManifestResourceStream(res)!;
            using var ms = new MemoryStream();
            audioStream.CopyTo(ms);

            long duration = 0;
            var utterances = new List<Utterance>();
            using var tsvStream = asm.GetManifestResourceStream($"PlaudMock.{name}.transcript.tsv");
            if (tsvStream != null)
            {
                using var reader = new StreamReader(tsvStream, Encoding.UTF8);
                while (reader.ReadLine() is { } line)
                {
                    var parts = line.Split('\t');
                    if (parts.Length == 2 && parts[0] == "#duration_ms")
                        long.TryParse(parts[1], NumberStyles.Integer, CultureInfo.InvariantCulture, out duration);
                    else if (parts.Length == 4
                             && long.TryParse(parts[0], NumberStyles.Integer, CultureInfo.InvariantCulture, out var s)
                             && long.TryParse(parts[1], NumberStyles.Integer, CultureInfo.InvariantCulture, out var e))
                        utterances.Add(new Utterance(s, e, parts[2], parts[3]));
                }
            }
            if (duration <= 0)
                duration = utterances.Count > 0 ? utterances[^1].EndMs : 60_000;
            result[name] = new FixtureClip(name, ms.ToArray(), duration, utterances);
        }
        return result;
    }

    // ── Helpers ──────────────────────────────────────────────────────────

    public static string RandomToken(int bytes) => Base64Url(RandomNumberGenerator.GetBytes(bytes));

    public static string Base64Url(byte[] data) =>
        Convert.ToBase64String(data).TrimEnd('=').Replace('+', '-').Replace('/', '_');

    /// <summary>First 12 characters and an ellipsis; never the whole value.</summary>
    public static string Mask(string value) => value.Length <= 12 ? new string('*', value.Length) : value[..12] + "…";

    private static void Trim<T>(Dictionary<string, T> dict)
    {
        if (dict.Count < MaxEntries) return;
        foreach (var key in dict.Keys.Take(dict.Count - MaxEntries + 1).ToList())
            dict.Remove(key);
    }
}
