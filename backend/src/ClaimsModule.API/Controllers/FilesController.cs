using ClaimsModule.Application.Common;
using ClaimsModule.Infrastructure.Storage;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.StaticFiles;

namespace ClaimsModule.API.Controllers;

/// <summary>
/// Serves documents for the LocalFileSystem storage provider only, via expiring HMAC-signed links. With Azure Blob
/// Storage downloads go straight to the SAS URL and never touch this endpoint.
/// </summary>
[ApiController]
public sealed class FilesController(LocalFileSystemStorageService localStorage, IConfiguration configuration) : ControllerBase
{
    private static readonly FileExtensionContentTypeProvider ContentTypes = new();

    [AllowAnonymous]
    [HttpGet("api/files/{**path}")]
    public IActionResult Download(string path, [FromQuery] long expires, [FromQuery] string? sig)
    {
        // Unknown, expired and forged links are indistinguishable on purpose.
        var stream = configuration["Storage:Provider"]?.Equals("AzureBlob", StringComparison.OrdinalIgnoreCase) == true &&
                     !string.IsNullOrWhiteSpace(configuration["Storage:AzureBlob:ConnectionString"])
            ? null
            : localStorage.OpenSigned(path, expires, sig ?? string.Empty);

        if (stream is null)
        {
            throw new NotFoundException("Document", path);
        }

        var name = Path.GetFileName(path);
        var display = System.Text.RegularExpressions.Regex.Replace(name, "^[0-9a-f]{8}-", string.Empty);
        if (!ContentTypes.TryGetContentType(display, out var contentType))
        {
            contentType = "application/octet-stream";
        }

        return File(stream, contentType, display);
    }
}
