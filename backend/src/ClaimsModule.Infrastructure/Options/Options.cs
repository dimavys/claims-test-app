using System.ComponentModel.DataAnnotations;
using ClaimsModule.Domain.Enums;

namespace ClaimsModule.Infrastructure.Options;

public sealed class TenantOptions
{
    public const string Section = "Tenant";

    /// <summary>The single organisation this deployment serves (matches the seeded reference data).</summary>
    public Guid OrganisationId { get; set; }
}

public sealed class StorageOptions
{
    public const string Section = "Storage";

    /// <summary>AzureBlob | LocalFileSystem. AzureBlob without a connection string falls back to LocalFileSystem (BR-D-03).</summary>
    public string Provider { get; set; } = "LocalFileSystem";

    public AzureBlobOptions AzureBlob { get; set; } = new();
    public LocalFileSystemOptions LocalFileSystem { get; set; } = new();
}

public sealed class AzureBlobOptions
{
    public string? ConnectionString { get; set; }
    public string ContainerName { get; set; } = "claim-documents";
}

public sealed class LocalFileSystemOptions
{
    public string RootPath { get; set; } = "uploads";

    /// <summary>Origin of this API, used to build download links (e.g. http://localhost:5080).</summary>
    public string PublicBaseUrl { get; set; } = "http://localhost:5080";

    /// <summary>HMAC key signing the expiring download links. Keep it secret in real deployments.</summary>
    public string? SigningKey { get; set; }
}

public sealed class JwtOptions
{
    public const string Section = "Jwt";

    public string Issuer { get; set; } = "claims-module";
    public string Audience { get; set; } = "claims-module-ui";

    [MinLength(32)]
    public string SigningKey { get; set; } = string.Empty;

    public int ExpiryMinutes { get; set; } = 480;
}

public sealed class MockUser
{
    public Guid Id { get; set; }
    public string UserName { get; set; } = string.Empty;
    public string DisplayName { get; set; } = string.Empty;
    public UserRole Role { get; set; }

    /// <summary>Plain text on purpose: this is a mock identity provider for the assessment, not real authentication.</summary>
    public string Password { get; set; } = string.Empty;
}

public sealed class MockUsersOptions
{
    public const string Section = "MockUsers";

    public List<MockUser> Users { get; set; } = new();
}
