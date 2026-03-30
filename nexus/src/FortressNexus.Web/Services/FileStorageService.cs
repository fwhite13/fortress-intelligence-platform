using FortressNexus.Web.Models.Entities;
using Microsoft.AspNetCore.Components.Forms;

namespace FortressNexus.Web.Services;

public class FileStorageService : IFileStorageService
{
    private readonly ILogger<FileStorageService> _logger;

    public FileStorageService(ILogger<FileStorageService> logger)
    {
        _logger = logger;
    }

    public Task<UploadedFile> UploadAsync(IBrowserFile file, string uploaderUpn) =>
        throw new NotImplementedException("WI-2");

    public Task<Stream> DownloadAsync(string s3Key) =>
        throw new NotImplementedException("WI-2");

    public Task<string> GetPresignedUrlAsync(string s3Key, int expiryMinutes = 15) =>
        throw new NotImplementedException("WI-2");
}
