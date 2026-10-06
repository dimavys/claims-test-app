using System.Security.Cryptography;
using System.Text;
using ClaimsModule.Application.Abstractions;
using ClaimsModule.Infrastructure.Options;
using Microsoft.Extensions.Options;

namespace ClaimsModule.Infrastructure.Storage;

/// <summary>
/// Development fallback (BR-D-03): files live under {RootPath}/{organisationId}/{claimId}/ and are downloaded through
/// short-lived HMAC-signed links served by the API's file endpoint.
/// </summary>
public sealed class LocalFileSystemStorageService : IStorageService
{
    public const string ContainerPrefix = "claim-documents/";

    private readonly string _root;
    private readonly string _baseUrl;
    private readonly byte[] _key;
    private readonly TimeProvider _time;

    public LocalFileSystemStorageService(IOptions<StorageOptions> options, TimeProvider time)
    {
        var local = options.Value.LocalFileSystem;
        if (string.IsNullOrWhiteSpace(local.SigningKey) || local.SigningKey.Length < 16)
        {
            throw new InvalidOperationException("Storage:LocalFileSystem:SigningKey must be set (at least 16 characters).");
        }

        _root = Path.GetFullPath(local.RootPath);
        _baseUrl = local.PublicBaseUrl.TrimEnd('/');
        _key = Encoding.UTF8.GetBytes(local.SigningKey);
        _time = time;
    }

    public async Task<string> UploadAsync(string relativePath, Stream content, string contentType, CancellationToken ct = default)
    {
        var fullPath = Resolve(relativePath);
        Directory.CreateDirectory(Path.GetDirectoryName(fullPath)!);

        await using var file = new FileStream(fullPath, FileMode.CreateNew, FileAccess.Write, FileShare.None);
        await content.CopyToAsync(file, ct);
        return ContainerPrefix + relativePath.Replace('\\', '/');
    }

    public Task<Uri> GetDownloadUrlAsync(string storedPath, TimeSpan timeToLive, CancellationToken ct = default)
    {
        var relative = StripPrefix(storedPath);
        var expires = _time.GetUtcNow().Add(timeToLive).ToUnixTimeSeconds();
        var signature = Sign(relative, expires);
        var url = $"{_baseUrl}/api/files/{string.Join('/', relative.Split('/').Select(Uri.EscapeDataString))}?expires={expires}&sig={signature}";
        return Task.FromResult(new Uri(url));
    }

    /// <summary>Validates a signed link and opens the file. Returns null when the link is expired, forged or the file is gone.</summary>
    public FileStream? OpenSigned(string relativePath, long expires, string signature)
    {
        relativePath = relativePath.Replace('\\', '/');
        if (_time.GetUtcNow().ToUnixTimeSeconds() > expires)
        {
            return null;
        }

        var expected = Encoding.ASCII.GetBytes(Sign(relativePath, expires));
        var provided = Encoding.ASCII.GetBytes(signature ?? string.Empty);
        if (!CryptographicOperations.FixedTimeEquals(expected, provided))
        {
            return null;
        }

        var fullPath = Resolve(relativePath);
        return File.Exists(fullPath) ? new FileStream(fullPath, FileMode.Open, FileAccess.Read, FileShare.Read) : null;
    }

    private string Sign(string relativePath, long expires)
    {
        var hash = HMACSHA256.HashData(_key, Encoding.UTF8.GetBytes($"{relativePath}|{expires}"));
        return Convert.ToHexString(hash).ToLowerInvariant();
    }

    private static string StripPrefix(string storedPath) =>
        storedPath.StartsWith(ContainerPrefix, StringComparison.OrdinalIgnoreCase) ? storedPath[ContainerPrefix.Length..] : storedPath;

    /// <summary>Maps a relative path into the storage root and refuses anything that escapes it.</summary>
    private string Resolve(string relativePath)
    {
        var full = Path.GetFullPath(Path.Combine(_root, relativePath.Replace('/', Path.DirectorySeparatorChar)));
        if (!full.StartsWith(_root + Path.DirectorySeparatorChar, StringComparison.Ordinal))
        {
            throw new InvalidOperationException("Invalid storage path.");
        }

        return full;
    }
}
