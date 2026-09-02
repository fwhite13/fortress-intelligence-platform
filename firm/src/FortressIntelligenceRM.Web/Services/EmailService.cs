using Amazon.SimpleEmailV2;
using Amazon.SimpleEmailV2.Model;
using System.Text;
using System.Text.RegularExpressions;

namespace FortressIntelligenceRM.Web.Services;

public interface IEmailService
{
    Task SendMeetingSummaryAsync(string toEmail, string meetingTitle, byte[] pdfBytes);
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
        mimeBody.AppendLine($"Subject: {subject}");
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
