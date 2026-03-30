using FortressNexus.Web.Models.Entities;
using Microsoft.AspNetCore.Components.Forms;

namespace FortressNexus.Web.Services;

public interface IFileStorageService
{
    Task<UploadedFile> UploadAsync(IBrowserFile file, string uploaderUpn);
    Task<Stream> DownloadAsync(string s3Key);
    Task<string> GetPresignedUrlAsync(string s3Key, int expiryMinutes = 15);
}
