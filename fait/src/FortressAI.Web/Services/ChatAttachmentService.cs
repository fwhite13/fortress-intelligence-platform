using Amazon.S3;
using Amazon.S3.Model;
using Microsoft.EntityFrameworkCore;
using FortressAI.Shared.Models;
using FortressAI.Web.Data;
using System.Text;

namespace FortressAI.Web.Services;

public class ChatAttachmentService
{
    private readonly IDbContextFactory<AppDbContext> _contextFactory;
    private readonly IAmazonS3 _s3;
    private readonly ILogger<ChatAttachmentService> _logger;
    private const string BucketName = "fortress-tools";

    // Supported file extensions for Phase 1
    private static readonly HashSet<string> TextExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".txt", ".md", ".csv", ".json", ".xml", ".html", ".py", ".cs", ".js", ".ts",
        ".yaml", ".yml", ".sh", ".sql", ".log", ".ini", ".toml", ".env"
    };
    private static readonly HashSet<string> ImageExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".png", ".jpg", ".jpeg", ".gif", ".webp"
    };

    public ChatAttachmentService(
        IDbContextFactory<AppDbContext> contextFactory,
        IAmazonS3 s3,
        ILogger<ChatAttachmentService> logger)
    {
        _contextFactory = contextFactory;
        _s3 = s3;
        _logger = logger;
    }

    /// <summary>
    /// Upload file to S3 and save metadata. Returns the ChatAttachment record.
    /// </summary>
    public async Task<ChatAttachment> UploadAttachmentAsync(
        Guid conversationId,
        Guid userId,
        string filename,
        string contentType,
        Stream fileStream,
        long fileSize)
    {
        var attachmentId = Guid.NewGuid();
        var stagingMessageId = Guid.Empty; // placeholder until message is created

        using var buffer = new MemoryStream();
        await fileStream.CopyToAsync(buffer);
        buffer.Position = 0;

        var s3Key = $"chat-attachments/{conversationId}/{attachmentId}/{filename}";

        await _s3.PutObjectAsync(new PutObjectRequest
        {
            BucketName = BucketName,
            Key = s3Key,
            InputStream = buffer,
            ContentType = contentType
        });

        _logger.LogInformation("[ATTACHMENT] Uploaded {Filename} to s3://{Bucket}/{Key}", filename, BucketName, s3Key);

        // Estimate tokens (rough: 4 chars per token for text, 1000 tokens for images/PDFs)
        int? tokenEstimate = null;
        var ext = Path.GetExtension(filename).ToLowerInvariant();
        if (TextExtensions.Contains(ext))
        {
            buffer.Position = 0;
            using var reader = new StreamReader(buffer, Encoding.UTF8, leaveOpen: true);
            var text = await reader.ReadToEndAsync();
            tokenEstimate = Math.Max(1, text.Length / 4);
        }
        else if (ImageExtensions.Contains(ext))
        {
            tokenEstimate = 1000; // typical for vision models
        }
        else if (ext == ".pdf")
        {
            tokenEstimate = (int)(fileSize / 500); // rough estimate
        }

        var attachment = new ChatAttachment
        {
            Id = attachmentId,
            ConversationId = conversationId,
            MessageId = stagingMessageId,
            UserId = userId,
            Filename = filename,
            ContentType = contentType,
            S3Key = s3Key,
            SizeBytes = fileSize,
            TokenEstimate = tokenEstimate,
            CreatedAt = DateTime.UtcNow
        };

        await using var db = await _contextFactory.CreateDbContextAsync();
        db.ChatAttachments.Add(attachment);
        await db.SaveChangesAsync();

        return attachment;
    }

    /// <summary>
    /// Link attachment to a real message ID after the message is saved.
    /// </summary>
    public async Task LinkAttachmentToMessageAsync(Guid attachmentId, Guid messageId)
    {
        await using var db = await _contextFactory.CreateDbContextAsync();
        var attachment = await db.ChatAttachments.FindAsync(attachmentId);
        if (attachment != null)
        {
            attachment.MessageId = messageId;
            await db.SaveChangesAsync();
        }
    }

    /// <summary>
    /// Get all attachments for a conversation (for display).
    /// </summary>
    public async Task<List<ChatAttachment>> GetConversationAttachmentsAsync(Guid conversationId)
    {
        await using var db = await _contextFactory.CreateDbContextAsync();
        return await db.ChatAttachments
            .Where(a => a.ConversationId == conversationId)
            .OrderBy(a => a.CreatedAt)
            .ToListAsync();
    }

    /// <summary>
    /// Get attachments for a specific message.
    /// </summary>
    public async Task<List<ChatAttachment>> GetMessageAttachmentsAsync(Guid messageId)
    {
        await using var db = await _contextFactory.CreateDbContextAsync();
        return await db.ChatAttachments
            .Where(a => a.MessageId == messageId)
            .OrderBy(a => a.CreatedAt)
            .ToListAsync();
    }

    /// <summary>
    /// Extract text/content from an attachment stored in S3.
    /// Returns context string to inject into the message.
    /// Returns null if the file type is not text-extractable.
    /// </summary>
    public async Task<string?> ExtractAttachmentContentAsync(ChatAttachment attachment)
    {
        var ext = Path.GetExtension(attachment.Filename).ToLowerInvariant();

        try
        {
            var getReq = new GetObjectRequest { BucketName = BucketName, Key = attachment.S3Key };
            using var response = await _s3.GetObjectAsync(getReq);
            using var ms = new MemoryStream();
            await response.ResponseStream.CopyToAsync(ms);
            ms.Position = 0;

            if (TextExtensions.Contains(ext))
            {
                using var reader = new StreamReader(ms, Encoding.UTF8);
                var content = await reader.ReadToEndAsync();
                return $"[File: {attachment.Filename}]\n{content}";
            }
            else if (ImageExtensions.Contains(ext))
            {
                // Return as data URI — BedrockService handles multimodal injection
                var base64 = Convert.ToBase64String(ms.ToArray());
                var mediaType = GetImageMediaType(ext);
                return $"data:{mediaType};base64,{base64}";
            }
            else if (ext == ".pdf")
            {
                // Return as PDF data URI — BedrockService handles document blocks
                var base64 = Convert.ToBase64String(ms.ToArray());
                return $"data:application/pdf;base64,{base64}";
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "[ATTACHMENT] Failed to extract content from {Filename}", attachment.Filename);
        }

        return null;
    }

    private static string GetImageMediaType(string ext) => ext switch
    {
        ".jpg" or ".jpeg" => "image/jpeg",
        ".png" => "image/png",
        ".gif" => "image/gif",
        ".webp" => "image/webp",
        _ => "image/jpeg"
    };

    public static bool IsTextFile(string filename)
    {
        var ext = Path.GetExtension(filename).ToLowerInvariant();
        return TextExtensions.Contains(ext);
    }

    public static bool IsImageFile(string filename)
    {
        var ext = Path.GetExtension(filename).ToLowerInvariant();
        return ImageExtensions.Contains(ext);
    }

    public static bool IsPdfFile(string filename)
    {
        return Path.GetExtension(filename).ToLowerInvariant() == ".pdf";
    }

    public static bool IsSupportedFile(string filename)
    {
        var ext = Path.GetExtension(filename).ToLowerInvariant();
        return TextExtensions.Contains(ext) || ImageExtensions.Contains(ext) || ext == ".pdf";
    }

    public static string FormatFileSize(long bytes)
    {
        if (bytes < 1024) return $"{bytes} B";
        if (bytes < 1024 * 1024) return $"{bytes / 1024.0:F1} KB";
        return $"{bytes / (1024.0 * 1024):F1} MB";
    }
}
