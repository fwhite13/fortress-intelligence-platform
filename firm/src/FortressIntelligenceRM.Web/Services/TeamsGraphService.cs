using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using Amazon.BedrockRuntime;
using Amazon.BedrockRuntime.Model;
using FortressIntelligenceRM.Web.Data;
using Microsoft.EntityFrameworkCore;

namespace FortressIntelligenceRM.Web.Services;

public class TeamsGraphService : IHostedService, IDisposable
{
    private readonly IDbContextFactory<FirmDbContext> _dbFactory;
    private readonly IConfiguration _config;
    private readonly ILogger<TeamsGraphService> _logger;
    private readonly IAmazonBedrockRuntime _bedrockRuntime;
    private readonly IHttpClientFactory _httpClientFactory;

    private string? _accessToken;
    private DateTime _tokenExpiry = DateTime.MinValue;

    public TeamsGraphService(
        IDbContextFactory<FirmDbContext> dbFactory,
        IConfiguration config,
        ILogger<TeamsGraphService> logger,
        IAmazonBedrockRuntime bedrockRuntime,
        IHttpClientFactory httpClientFactory)
    {
        _dbFactory = dbFactory;
        _config = config;
        _logger = logger;
        _bedrockRuntime = bedrockRuntime;
        _httpClientFactory = httpClientFactory;
    }

    public Task StartAsync(CancellationToken cancellationToken)
    {
        _logger.LogInformation("[TeamsGraph] Webhook subscription mode removed. Transcript processing available via polling service.");
        return Task.CompletedTask;
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    public void Dispose() { }

    // ── Graph Auth ────────────────────────────────────────────────────────────

    private async Task<string> GetAccessTokenAsync(CancellationToken ct = default)
    {
        if (_accessToken != null && DateTime.UtcNow < _tokenExpiry.AddMinutes(-2))
            return _accessToken;

        var tenantId = _config["Firm:GraphTenantId"] ?? throw new InvalidOperationException("Firm:GraphTenantId not configured");
        var clientId = _config["Firm:GraphClientId"] ?? throw new InvalidOperationException("Firm:GraphClientId not configured");
        var clientSecret = _config["Firm:GraphClientSecret"] ?? throw new InvalidOperationException("Firm:GraphClientSecret not configured");

        var tokenUrl = $"https://login.microsoftonline.com/{tenantId}/oauth2/v2.0/token";
        var formContent = new FormUrlEncodedContent(new[]
        {
            new KeyValuePair<string, string>("grant_type", "client_credentials"),
            new KeyValuePair<string, string>("client_id", clientId),
            new KeyValuePair<string, string>("client_secret", clientSecret),
            new KeyValuePair<string, string>("scope", "https://graph.microsoft.com/.default")
        });

        var client = _httpClientFactory.CreateClient();
        var response = await client.PostAsync(tokenUrl, formContent, ct);
        response.EnsureSuccessStatusCode();

        var json = await response.Content.ReadAsStringAsync(ct);
        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;

        _accessToken = root.GetProperty("access_token").GetString()
            ?? throw new InvalidOperationException("access_token missing from token response");
        var expiresIn = root.TryGetProperty("expires_in", out var expProp) ? expProp.GetInt32() : 3600;
        _tokenExpiry = DateTime.UtcNow.AddSeconds(expiresIn);

        _logger.LogInformation("[TeamsGraph] Access token acquired, expires in {Seconds}s", expiresIn);
        return _accessToken;
    }

    // ── Public helpers ────────────────────────────────────────────────────────

    public Task<string> GetGraphAccessTokenAsync(CancellationToken ct = default) => GetAccessTokenAsync(ct);

    /// <summary>
    /// Polls Graph directly for the transcript using app token.
    /// Called by TranscriptPollingService for Mode A meetings.
    /// </summary>
    public async Task<bool> TryFetchTranscriptForMeetingAsync(long meetingId, string joinUrl, string entraOid, CancellationToken ct)
    {
        try
        {
            var token = await GetAccessTokenAsync(ct);
            var client = _httpClientFactory.CreateClient();

            // Step 1: Find the online meeting by joinWebUrl
            var filter = Uri.EscapeDataString($"joinWebUrl eq '{joinUrl}'");
            var meetingReq = new HttpRequestMessage(HttpMethod.Get,
                $"https://graph.microsoft.com/v1.0/users/{entraOid}/onlineMeetings?$filter={filter}");
            meetingReq.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", token);
            var meetingResp = await client.SendAsync(meetingReq, ct);

            if (!meetingResp.IsSuccessStatusCode)
            {
                _logger.LogInformation("[TranscriptPolling] Meeting not found for joinUrl {JoinUrl}, meeting {MeetingId}", joinUrl, meetingId);
                return false;
            }

            var meetingBody = await meetingResp.Content.ReadAsStringAsync(ct);
            using var meetingDoc = JsonDocument.Parse(meetingBody);
            var meetingItems = meetingDoc.RootElement.GetProperty("value");
            if (meetingItems.GetArrayLength() == 0)
            {
                _logger.LogInformation("[TranscriptPolling] No online meeting found for joinUrl, meeting {MeetingId}", meetingId);
                return false;
            }
            var graphMeetingId = meetingItems[0].GetProperty("id").GetString() ?? "";

            // Step 2: List transcripts
            var transcriptReq = new HttpRequestMessage(HttpMethod.Get,
                $"https://graph.microsoft.com/v1.0/users/{entraOid}/onlineMeetings/{graphMeetingId}/transcripts");
            transcriptReq.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", token);
            var transcriptListResp = await client.SendAsync(transcriptReq, ct);

            if (!transcriptListResp.IsSuccessStatusCode)
            {
                _logger.LogInformation("[TranscriptPolling] Transcript list returned {Status} for meeting {MeetingId}", transcriptListResp.StatusCode, meetingId);
                return false;
            }

            var transcriptListBody = await transcriptListResp.Content.ReadAsStringAsync(ct);
            using var transcriptListDoc = JsonDocument.Parse(transcriptListBody);
            var transcripts = transcriptListDoc.RootElement.GetProperty("value");
            if (transcripts.GetArrayLength() == 0)
            {
                _logger.LogInformation("[TranscriptPolling] Transcript not yet available for meeting {MeetingId} — will retry.", meetingId);
                return false;
            }

            // Get the latest transcript ID
            var latestTranscriptId = transcripts[transcripts.GetArrayLength() - 1].GetProperty("id").GetString() ?? "";

            // Step 3: Fetch VTT content
            var vttReq = new HttpRequestMessage(HttpMethod.Get,
                $"https://graph.microsoft.com/v1.0/users/{entraOid}/onlineMeetings/{graphMeetingId}/transcripts/{latestTranscriptId}/content?$format=text/vtt");
            vttReq.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", token);
            var vttResp = await client.SendAsync(vttReq, ct);

            if (!vttResp.IsSuccessStatusCode)
            {
                _logger.LogWarning("[TranscriptPolling] VTT fetch returned {Status} for meeting {MeetingId}", vttResp.StatusCode, meetingId);
                return false;
            }

            var vttContent = await vttResp.Content.ReadAsStringAsync(ct);
            if (string.IsNullOrWhiteSpace(vttContent))
            {
                _logger.LogInformation("[TranscriptPolling] Empty VTT content for meeting {MeetingId}", meetingId);
                return false;
            }

            await ProcessVttForMeetingAsync(meetingId, vttContent, ct);
            return true;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[TeamsGraph] TryFetchTranscriptForMeetingAsync failed for meeting {MeetingId}", meetingId);
            return false;
        }
    }

    private async Task ProcessVttForMeetingAsync(long meetingId, string vttContent, CancellationToken ct)
    {
        var segments = ParseVttSegments(vttContent);
        _logger.LogInformation("[TeamsGraph] Parsed {Count} VTT segments for meeting {MeetingId}.", segments.Count, meetingId);

        await using var db = await _dbFactory.CreateDbContextAsync(ct);

        await db.Database.ExecuteSqlRawAsync(
            "UPDATE firm_meetings SET status = 'Transcribing', updated_at = UTC_TIMESTAMP() WHERE id = {0}",
            meetingId);

        foreach (var seg in segments)
        {
            await db.Database.ExecuteSqlRawAsync(
                @"INSERT INTO firm_meeting_transcripts (meeting_id, speaker_name, text, start_time_ms, end_time_ms)
                  VALUES ({0}, {1}, {2}, {3}, {4})",
                meetingId, seg.SpeakerName ?? "Unknown", seg.Text, seg.StartTimeMs, seg.EndTimeMs);
        }

        await db.Database.ExecuteSqlRawAsync(
            "UPDATE firm_meetings SET status = 'Summarizing', updated_at = UTC_TIMESTAMP() WHERE id = {0}",
            meetingId);

        var sb = new System.Text.StringBuilder();
        foreach (var seg in segments)
        {
            var ts = seg.StartTimeMs.HasValue
                ? TimeSpan.FromMilliseconds(seg.StartTimeMs.Value).ToString(@"hh\:mm\:ss")
                : "00:00:00";
            sb.AppendLine($"[{ts}] {seg.SpeakerName ?? "Unknown"}: {seg.Text}");
        }
        var transcriptText = sb.ToString();

        var summary = await SummarizeAsync(transcriptText, meetingId, ct);
        if (summary != null)
        {
            await db.Database.ExecuteSqlRawAsync(
                @"INSERT INTO firm_meeting_summaries (meeting_id, summary_text, action_items_json, key_decisions_json, follow_ups_json, created_at)
                  VALUES ({0}, {1}, {2}, {3}, {4}, UTC_TIMESTAMP())
                  ON DUPLICATE KEY UPDATE summary_text = VALUES(summary_text),
                      action_items_json = VALUES(action_items_json),
                      key_decisions_json = VALUES(key_decisions_json),
                      follow_ups_json = VALUES(follow_ups_json)",
                meetingId, summary.SummaryText ?? "", summary.ActionItemsJson ?? "[]",
                summary.KeyDecisionsJson ?? "[]", summary.FollowUpsJson ?? "[]");
        }

        await db.Database.ExecuteSqlRawAsync(
            "UPDATE firm_meetings SET status = 'Complete', updated_at = UTC_TIMESTAMP() WHERE id = {0}",
            meetingId);

        await NotifyFaitMeetingCompleteAsync(meetingId, transcriptText, summary?.SummaryText ?? "", db, ct);
        _logger.LogInformation("[TeamsGraph] Completed processing transcript for meeting {MeetingId}.", meetingId);
    }

    // ── Transcript Fetch & Process ────────────────────────────────────────────

    public async Task FetchAndProcessTranscriptAsync(string resource, CancellationToken ct)
    {
        try
        {
            _logger.LogInformation("[TeamsGraph] Processing resource: {Resource}", resource);

            // Parse: communications/onlineMeetings/{meetingId}/transcripts/{transcriptId}
            var match = Regex.Match(resource,
                @"communications/onlineMeetings/([^/]+)/transcripts/([^/?]+)");
            if (!match.Success)
            {
                _logger.LogWarning("[TeamsGraph] Could not parse meetingId/transcriptId from resource: {Resource}", resource);
                return;
            }

            var graphMeetingId = match.Groups[1].Value;
            var graphTranscriptId = match.Groups[2].Value;

            _logger.LogInformation("[TeamsGraph] Meeting: {MeetingId}, Transcript: {TranscriptId}",
                graphMeetingId, graphTranscriptId);

            // Fetch VTT from Graph
            var token = await GetAccessTokenAsync(ct);
            var vttUrl = $"https://graph.microsoft.com/v1.0/communications/onlineMeetings/{graphMeetingId}/transcripts/{graphTranscriptId}/content?$format=text/vtt";

            var client = _httpClientFactory.CreateClient();
            var vttReq = new HttpRequestMessage(HttpMethod.Get, vttUrl);
            vttReq.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);

            var vttResponse = await client.SendAsync(vttReq, ct);
            if (!vttResponse.IsSuccessStatusCode)
            {
                _logger.LogError("[TeamsGraph] Failed to fetch VTT: {Status}", vttResponse.StatusCode);
                return;
            }

            var vttContent = await vttResponse.Content.ReadAsStringAsync(ct);
            var segments = ParseVttSegments(vttContent);
            _logger.LogInformation("[TeamsGraph] Parsed {Count} VTT segments.", segments.Count);

            // Look up FIRM meeting by graphMeetingId (stored in firm_meetings.graph_meeting_id if column exists)
            // Fall back to most recent meeting in WaitingTranscript status
            await using var db = await _dbFactory.CreateDbContextAsync(ct);
            long? meetingId = null;

            try
            {
                var rows = await db.Database
                    .SqlQueryRaw<MeetingIdRow>(
                        "SELECT id AS Id FROM firm_meetings WHERE graph_meeting_id = {0} ORDER BY created_at DESC LIMIT 1",
                        graphMeetingId)
                    .ToListAsync(ct);
                if (rows.Count > 0) meetingId = rows[0].Id;
            }
            catch
            {
                // column may not exist yet — fall through
            }

            if (meetingId == null)
            {
                // Fallback: most recent WaitingTranscript meeting
                var rows = await db.Database
                    .SqlQueryRaw<MeetingIdRow>(
                        "SELECT id AS Id FROM firm_meetings WHERE status = 'WaitingTranscript' ORDER BY created_at DESC LIMIT 1")
                    .ToListAsync(ct);
                if (rows.Count > 0) meetingId = rows[0].Id;
            }

            if (meetingId == null)
            {
                _logger.LogWarning("[TeamsGraph] No matching FIRM meeting found for graphMeetingId: {Id}", graphMeetingId);
                return;
            }

            // Update meeting status to Transcribing
            await db.Database.ExecuteSqlRawAsync(
                "UPDATE firm_meetings SET status = 'Transcribing', updated_at = UTC_TIMESTAMP() WHERE id = {0}",
                meetingId.Value);

            // Insert transcript segments
            foreach (var seg in segments)
            {
                await db.Database.ExecuteSqlRawAsync(
                    @"INSERT INTO firm_meeting_transcripts (meeting_id, speaker_name, text, start_time_ms, end_time_ms)
                      VALUES ({0}, {1}, {2}, {3}, {4})",
                    meetingId.Value,
                    seg.SpeakerName ?? "Unknown",
                    seg.Text,
                    seg.StartTimeMs,
                    seg.EndTimeMs);
            }

            _logger.LogInformation("[TeamsGraph] Inserted {Count} transcript segments for meeting {MeetingId}.", segments.Count, meetingId.Value);

            // Update status to Summarizing
            await db.Database.ExecuteSqlRawAsync(
                "UPDATE firm_meetings SET status = 'Summarizing', updated_at = UTC_TIMESTAMP() WHERE id = {0}",
                meetingId.Value);

            // Build full transcript text for summarization
            var transcriptSb = new StringBuilder();
            foreach (var seg in segments)
            {
                var ts = seg.StartTimeMs.HasValue
                    ? TimeSpan.FromMilliseconds(seg.StartTimeMs.Value).ToString(@"hh\:mm\:ss")
                    : "00:00:00";
                transcriptSb.AppendLine($"[{ts}] {seg.SpeakerName ?? "Unknown"}: {seg.Text}");
            }
            var transcriptText = transcriptSb.ToString();

            // Summarize via Bedrock
            var summary = await SummarizeAsync(transcriptText, meetingId.Value, ct);
            if (summary != null)
            {
                await db.Database.ExecuteSqlRawAsync(
                    @"INSERT INTO firm_meeting_summaries (meeting_id, summary_text, action_items_json, key_decisions_json, follow_ups_json, created_at)
                      VALUES ({0}, {1}, {2}, {3}, {4}, UTC_TIMESTAMP())
                      ON DUPLICATE KEY UPDATE summary_text = VALUES(summary_text),
                          action_items_json = VALUES(action_items_json),
                          key_decisions_json = VALUES(key_decisions_json),
                          follow_ups_json = VALUES(follow_ups_json)",
                    meetingId.Value,
                    summary.SummaryText ?? "",
                    summary.ActionItemsJson ?? "[]",
                    summary.KeyDecisionsJson ?? "[]",
                    summary.FollowUpsJson ?? "[]");
            }

            // Mark complete
            await db.Database.ExecuteSqlRawAsync(
                "UPDATE firm_meetings SET status = 'Complete', updated_at = UTC_TIMESTAMP() WHERE id = {0}",
                meetingId.Value);

            // Notify FAIT
            await NotifyFaitMeetingCompleteAsync(meetingId.Value, transcriptText, summary?.SummaryText ?? "", db, ct);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[TeamsGraph] FetchAndProcessTranscriptAsync failed for resource: {Resource}", resource);
        }
    }

    // ── VTT Parsing ───────────────────────────────────────────────────────────

    private static List<VttSegment> ParseVttSegments(string vttContent)
    {
        var segments = new List<VttSegment>();
        var lines = vttContent.Split('\n', StringSplitOptions.None);

        // Regex for WebVTT timestamp: 00:00:00.000 --> 00:00:01.500
        var timestampRegex = new Regex(@"(\d+:\d+:\d+[\.,]\d+)\s+-->\s+(\d+:\d+:\d+[\.,]\d+)");
        // Regex for speaker tag: <v SpeakerName>text
        var speakerRegex = new Regex(@"<v\s+([^>]+)>(.*)", RegexOptions.Singleline);

        long? startMs = null;
        long? endMs = null;
        string? currentSpeaker = null;
        var textBuilder = new StringBuilder();

        void FlushSegment()
        {
            var text = textBuilder.ToString().Trim();
            if (!string.IsNullOrEmpty(text))
            {
                segments.Add(new VttSegment
                {
                    SpeakerName = currentSpeaker,
                    Text = text,
                    StartTimeMs = startMs,
                    EndTimeMs = endMs
                });
            }
            textBuilder.Clear();
            currentSpeaker = null;
            startMs = null;
            endMs = null;
        }

        foreach (var rawLine in lines)
        {
            var line = rawLine.TrimEnd('\r');

            var tsMatch = timestampRegex.Match(line);
            if (tsMatch.Success)
            {
                FlushSegment();
                startMs = ParseVttTimeToMs(tsMatch.Groups[1].Value);
                endMs = ParseVttTimeToMs(tsMatch.Groups[2].Value);
                continue;
            }

            if (startMs.HasValue && !string.IsNullOrWhiteSpace(line))
            {
                var spMatch = speakerRegex.Match(line);
                if (spMatch.Success)
                {
                    currentSpeaker = spMatch.Groups[1].Value.Trim();
                    textBuilder.Append(spMatch.Groups[2].Value.Trim());
                }
                else
                {
                    textBuilder.Append(line);
                }
            }
        }

        FlushSegment();
        return segments;
    }

    private static long? ParseVttTimeToMs(string timeStr)
    {
        // Handle both 00:00:00.000 and 00:00:00,000
        timeStr = timeStr.Replace(',', '.');
        if (TimeSpan.TryParse(timeStr, out var ts))
            return (long)ts.TotalMilliseconds;
        return null;
    }

    // ── Bedrock Summarization ─────────────────────────────────────────────────

    internal async Task<BedrockSummaryResult?> SummarizeAsync(string transcriptText, long meetingId, CancellationToken ct = default)
    {
        try
        {
            _logger.LogInformation("[TeamsGraph] Summarizing transcript for meeting {MeetingId} via Bedrock.", meetingId);

            var prompt = $@"You are an expert meeting analyst. Analyze the following transcript and produce a structured markdown meeting summary.

Output EXACTLY this JSON structure (no markdown wrapper, no code fences):
{{
  ""summaryText"": ""...(the full structured markdown summary — see format below)..."",
  ""actionItemsJson"": ""[{{""description"": ""..."", ""owner"": ""..."", ""deadline"": ""...""}}]"",
  ""keyDecisionsJson"": ""[""Decision text here""]"",
  ""followUpsJson"": ""[""Follow-up item here""]""
}}

The summaryText field must contain this EXACT markdown structure:

# Meeting Summary: {{MEETING_TITLE}}

**Date:** {{DATE}}
**Duration:** {{DURATION}}
**Participants:** {{name (role), name (role), ...}}

---

## Executive Summary
{{2-3 sentence high-level summary of what the meeting accomplished}}

---

## Key Topics Discussed
### {{Topic 1}}
- {{bullet point}}
- {{bullet point}}

### {{Topic 2}}
- {{bullet point}}

---

## Decisions Made
| Decision | Context | Owner |
|----------|---------|-------|
| {{decision text}} | {{why/background}} | {{who decided or owns it}} |

---

## Action Items
| Action | Owner | Deadline |
|--------|-------|----------|
| {{action text}} | {{name}} | {{date or ""TBD""}} |

---

## Notable Quotes
> ""{{verbatim quote}}"" — {{Speaker Name}}

> ""{{verbatim quote}}"" — {{Speaker Name}}

> ""{{verbatim quote}}"" — {{Speaker Name}}

_(include 3–6 total quotes)_

---

*Summary generated by FIRM AI • {DateTime.UtcNow:yyyy-MM-dd HH:mm} UTC*

INSTRUCTIONS:
- Extract real participant names and roles from the transcript. If roles are unclear, use ""Participant"".
- For MEETING_TITLE: derive a descriptive title from the transcript topics if not obvious from context.
- For DATE: extract from transcript timestamps (format: the date portion of the [HH:mm:ss] timestamps, or infer from context). If unavailable, use ""Not recorded"".
- For DURATION: calculate from first to last transcript timestamp. Format as ""X hours Y minutes"" or ""X minutes"".
- For Decisions Made: identify CONCRETE decisions that were made (not just discussed). Use ""None identified"" row if no clear decisions.
- For Action Items: extract specific tasks assigned to named owners. Include deadlines only if explicitly stated. Use ""TBD"" for deadline when not stated.
- For Notable Quotes: select 3-6 verbatim quotes that are insightful, memorable, decision-relevant, or capture key moments. Use exact words from transcript. Each quote MUST have a speaker attribution.
- If any section has no content, use ""None identified"" rather than omitting the section or fabricating content.
- Keep Executive Summary to 2-3 sentences maximum.

Transcript:
{transcriptText}";

            var requestBody = JsonSerializer.Serialize(new
            {
                anthropic_version = "bedrock-2023-05-31",
                max_tokens = 4096,
                messages = new[] { new { role = "user", content = prompt } }
            });

            var bodyBytes = Encoding.UTF8.GetBytes(requestBody);
            var response = await _bedrockRuntime.InvokeModelAsync(new InvokeModelRequest
            {
                ModelId = _config.GetValue<string>("Bedrock:SummaryModelId", "anthropic.claude-3-sonnet-20240229-v1:0")!,
                ContentType = "application/json",
                Accept = "application/json",
                Body = new MemoryStream(bodyBytes)
            }, ct);

            var responseJson = await new StreamReader(response.Body).ReadToEndAsync(ct);
            using var doc = JsonDocument.Parse(responseJson);
            var root = doc.RootElement;

            // Claude Bedrock response: { "content": [{ "type": "text", "text": "..." }] }
            string? text = null;
            if (root.TryGetProperty("content", out var contentArr))
            {
                foreach (var item in contentArr.EnumerateArray())
                {
                    if (item.TryGetProperty("type", out var typeEl) && typeEl.GetString() == "text")
                    {
                        text = item.TryGetProperty("text", out var textEl) ? textEl.GetString() : null;
                        break;
                    }
                }
            }

            if (string.IsNullOrEmpty(text))
            {
                _logger.LogWarning("[TeamsGraph] Bedrock returned empty text for meeting {MeetingId}", meetingId);
                return null;
            }

            // Parse the JSON in the text
            // Strip possible markdown code fences
            var jsonText = Regex.Replace(text.Trim(), @"^```json?\s*|```$", "", RegexOptions.Multiline).Trim();

            using var summaryDoc = JsonDocument.Parse(jsonText);
            var summaryRoot = summaryDoc.RootElement;

            return new BedrockSummaryResult
            {
                SummaryText = summaryRoot.TryGetProperty("summaryText", out var st) ? st.GetString() : null,
                ActionItemsJson = summaryRoot.TryGetProperty("actionItemsJson", out var ai) ? ai.GetString() : null,
                KeyDecisionsJson = summaryRoot.TryGetProperty("keyDecisionsJson", out var kd) ? kd.GetString() : null,
                FollowUpsJson = summaryRoot.TryGetProperty("followUpsJson", out var fu) ? fu.GetString() : null,
                ModelUsed = _config.GetValue<string>("Bedrock:SummaryModelId", "anthropic.claude-3-sonnet-20240229-v1:0")!
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[TeamsGraph] Bedrock summarization failed for meeting {MeetingId}", meetingId);
            return null;
        }
    }

    // ── FAIT Notification ─────────────────────────────────────────────────────

    private async Task NotifyFaitMeetingCompleteAsync(long meetingId, string transcriptText, string summaryText,
        FirmDbContext db, CancellationToken ct)
    {
        try
        {
            var faitApiUrl = _config["FIP:FaitApiUrl"] ?? "https://fait.dev.fortressam.ai";
            var sharedSecret = _config["Firm:SharedSecret"] ?? "";

            // Get meeting + user info
            var meetingRows = await db.Database
                .SqlQueryRaw<MeetingUserRow>(
                    @"SELECT m.id AS MeetingId, u.entra_oid AS EntraOid
                      FROM firm_meetings m
                      JOIN firm_users u ON u.id = m.created_by
                      WHERE m.id = {0} LIMIT 1",
                    meetingId)
                .ToListAsync(ct);

            if (meetingRows.Count == 0)
            {
                _logger.LogWarning("[TeamsGraph] No meeting/user found for FAIT notify, meetingId: {Id}", meetingId);
                return;
            }

            var notifyBody = JsonSerializer.Serialize(new
            {
                entraOid = meetingRows[0].EntraOid,
                meetingId = meetingId,
                transcriptText = transcriptText,
                summaryText = summaryText
            });

            var client = _httpClientFactory.CreateClient();
            var req = new HttpRequestMessage(HttpMethod.Post, $"{faitApiUrl}/api/firm/meeting-complete");
            req.Content = new StringContent(notifyBody, Encoding.UTF8, "application/json");
            if (!string.IsNullOrEmpty(sharedSecret))
                req.Headers.Add("X-Firm-Secret", sharedSecret);

            var response = await client.SendAsync(req, ct);
            _logger.LogInformation("[TeamsGraph] FAIT meeting-complete notified, status: {Status}", response.StatusCode);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[TeamsGraph] FAIT meeting-complete notification failed for meeting {MeetingId}", meetingId);
        }
    }

    // ── Private helpers / DTOs ────────────────────────────────────────────────

    private class VttSegment
    {
        public string? SpeakerName { get; set; }
        public string Text { get; set; } = "";
        public long? StartTimeMs { get; set; }
        public long? EndTimeMs { get; set; }
    }

    internal class BedrockSummaryResult
    {
        public string? SummaryText { get; set; }
        // TODO: Notable Quotes are currently embedded in SummaryText markdown.
        // If a separate QuotesJson field is needed in the future, update the prompt
        // to output a "quotesJson" key and add QuotesJson to FirmMeetingSummary + DB.
        public string? ActionItemsJson { get; set; }
        public string? KeyDecisionsJson { get; set; }
        public string? FollowUpsJson { get; set; }
        public string? ModelUsed { get; set; }
    }

    private class MeetingIdRow
    {
        public long Id { get; set; }
    }

    private class MeetingUserRow
    {
        public long MeetingId { get; set; }
        public string EntraOid { get; set; } = "";
    }
}
