using Amazon.SimpleEmailV2;
using Amazon.SimpleEmailV2.Model;
using System.Text;
using System.Text.RegularExpressions;

namespace FortressIntelligenceRM.Web.Services;

public interface IEmailService
{
    Task SendMeetingSummaryAsync(string toEmail, string meetingTitle, byte[] pdfBytes);

    /// <summary>WI #9644 — a short notification (plain + html alternatives, no attachment) from the same
    /// sender as the recap email (Branding:SummaryEmailFrom).</summary>
    Task SendNotificationAsync(string toEmail, string subject, string textBody, string htmlBody);
}

public class EmailService : IEmailService
{
    private readonly IAmazonSimpleEmailServiceV2 _ses;
    private readonly IConfiguration _config;
    private readonly ILogger<EmailService> _logger;

    public EmailService(IAmazonSimpleEmailServiceV2 ses, IConfiguration config, ILogger<EmailService> logger)
    {
        _ses = ses;
        _config = config;
        _logger = logger;
    }

    private string FromAddress => _config["Branding:SummaryEmailFrom"] ?? "rn@refugems.ai";

    public async Task SendMeetingSummaryAsync(string toEmail, string meetingTitle, byte[] pdfBytes)
    {
        var slug = Regex.Replace((meetingTitle ?? "").ToLowerInvariant(), @"[^a-z0-9]+", "-").Trim('-');
        if (string.IsNullOrEmpty(slug)) slug = "meeting";
        var attachmentName = $"{slug}-summary.pdf";
        var subject = $"{meetingTitle} — Meeting Summary";

        var boundary = $"----=_Part_{Guid.NewGuid():N}";
        var base64Pdf = Convert.ToBase64String(pdfBytes);
        var wrappedBase64 = WrapBase64(base64Pdf, 76);

        var mimeBody = new StringBuilder();
        mimeBody.AppendLine($"From: {FromAddress}");
        mimeBody.AppendLine($"To: {toEmail}");
        mimeBody.AppendLine($"Subject: {EncodeSubjectHeader(subject)}");
        mimeBody.AppendLine("MIME-Version: 1.0");
        mimeBody.AppendLine($"Content-Type: multipart/mixed; boundary=\"{boundary}\"");
        mimeBody.AppendLine();
        mimeBody.AppendLine($"--{boundary}");
        mimeBody.AppendLine("Content-Type: text/plain; charset=UTF-8");
        mimeBody.AppendLine();
        mimeBody.AppendLine("Your meeting summary is attached as a PDF.");
        mimeBody.AppendLine();
        mimeBody.AppendLine($"--{boundary}");
        mimeBody.AppendLine("Content-Type: application/pdf");
        mimeBody.AppendLine($"Content-Disposition: attachment; filename=\"{attachmentName}\"");
        mimeBody.AppendLine("Content-Transfer-Encoding: base64");
        mimeBody.AppendLine();
        mimeBody.AppendLine(wrappedBase64);
        mimeBody.AppendLine($"--{boundary}--");

        var request = new SendEmailRequest
        {
            FromEmailAddress = FromAddress,
            Destination = new Destination { ToAddresses = new List<string> { toEmail } },
            Content = new EmailContent
            {
                Raw = new RawMessage
                {
                    Data = new MemoryStream(Encoding.ASCII.GetBytes(mimeBody.ToString()))
                }
            }
        };

        _logger.LogInformation("FIRM: EmailService sending meeting summary email to {ToEmail} for meeting '{MeetingTitle}'", toEmail, meetingTitle);
        await _ses.SendEmailAsync(request);
    }

    /// <summary>
    /// WI #9644 — SES "Simple" content with both Text and Html bodies, which SES sends as a
    /// multipart/alternative message (UTF-8, headers encoded by SES). Same SES client and FromAddress as
    /// SendMeetingSummaryAsync. Never logs the recipient, subject or body; callers log the outcome.
    /// </summary>
    public async Task SendNotificationAsync(string toEmail, string subject, string textBody, string htmlBody)
    {
        var request = new SendEmailRequest
        {
            FromEmailAddress = FromAddress,
            Destination = new Destination { ToAddresses = new List<string> { toEmail } },
            Content = new EmailContent
            {
                Simple = new Message
                {
                    Subject = new Content { Data = subject, Charset = "UTF-8" },
                    Body = new Body
                    {
                        Text = new Content { Data = textBody, Charset = "UTF-8" },
                        Html = new Content { Data = htmlBody, Charset = "UTF-8" }
                    }
                }
            }
        };
        await _ses.SendEmailAsync(request);
    }

    /// <summary>
    /// WI #9972 — the value for a raw "Subject: " header line. Every run of CR/LF is replaced by one space
    /// so a title cannot start a new header. A pure-ASCII subject is returned as-is (plain header).
    /// Otherwise it is written as RFC 2047 "B" encoded-words (=?UTF-8?B?...?=), each at most 75 characters,
    /// folded with CRLF + space. Words split only on Unicode scalar boundaries (never inside a UTF-8
    /// sequence or a surrogate pair). The first word is shorter so the line "Subject: " + word stays within 76.
    /// </summary>
    public static string EncodeSubjectHeader(string? subject)
    {
        var clean = Regex.Replace(subject ?? "", @"[\r\n]+", " ");

        var isAscii = true;
        foreach (var c in clean)
        {
            if (c > 0x7E || (c < 0x20 && c != '\t')) { isAscii = false; break; }
        }
        if (isAscii) return clean;

        const string prefix = "=?UTF-8?B?";
        const string suffix = "?=";
        // 75-char word: 63 chars of base64 room -> 60 usable (multiple of 4) -> 45 bytes.
        // First word: "Subject: " (9) + word <= 76 -> 67-char word -> 52 base64 chars -> 39 bytes.
        const int firstMaxBytes = 39;
        const int nextMaxBytes = 45;

        var words = new List<string>();
        var chunk = new List<byte>();
        var maxBytes = firstMaxBytes;
        Span<byte> runeBytes = stackalloc byte[4];
        foreach (var rune in clean.EnumerateRunes()) // lone surrogates come back as U+FFFD
        {
            var n = rune.EncodeToUtf8(runeBytes);
            if (chunk.Count + n > maxBytes)
            {
                words.Add(prefix + Convert.ToBase64String(chunk.ToArray()) + suffix);
                chunk.Clear();
                maxBytes = nextMaxBytes;
            }
            for (var i = 0; i < n; i++) chunk.Add(runeBytes[i]);
        }
        if (chunk.Count > 0) words.Add(prefix + Convert.ToBase64String(chunk.ToArray()) + suffix);

        return string.Join("\r\n ", words);
    }

    private static string WrapBase64(string base64String, int lineLength)
    {
        var sb = new StringBuilder();
        for (int i = 0; i < base64String.Length; i += lineLength)
        {
            int length = Math.Min(lineLength, base64String.Length - i);
            sb.AppendLine(base64String.Substring(i, length));
        }
        return sb.ToString().TrimEnd();
    }
}
