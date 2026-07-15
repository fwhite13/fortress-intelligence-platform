using Amazon.SimpleEmailV2;
using Amazon.SimpleEmailV2.Model;
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

        var request = new SendEmailRequest
        {
            FromEmailAddress = FromAddress,
            Destination = new Destination { ToAddresses = new List<string> { toEmail } },
            Content = new EmailContent
            {
                Simple = new Message
                {
                    Subject = new Content { Data = $"{meetingTitle} — Meeting Summary" },
                    Body = new Body
                    {
                        Text = new Content { Data = "Your meeting summary is attached as a PDF." }
                    },
                    Attachments = new List<Attachment>
                    {
                        new Attachment
                        {
                            FileName = attachmentName,
                            ContentType = "application/pdf",
                            ContentDisposition = "ATTACHMENT",
                            RawContent = new MemoryStream(pdfBytes)
                        }
                    }
                }
            }
        };

        _logger.LogInformation("FIRM: EmailService sending meeting summary email to {ToEmail} for meeting '{MeetingTitle}'", toEmail, meetingTitle);
        await _ses.SendEmailAsync(request);
    }
}
