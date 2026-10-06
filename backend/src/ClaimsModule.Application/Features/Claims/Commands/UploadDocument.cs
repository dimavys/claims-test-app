using System.Text.RegularExpressions;
using AutoMapper;
using ClaimsModule.Application.Abstractions;
using ClaimsModule.Application.Common;
using ClaimsModule.Application.Features.Claims.Dtos;
using ClaimsModule.Domain.Common;
using FluentValidation;
using MediatR;

namespace ClaimsModule.Application.Features.Claims.Commands;

public static class DocumentRules
{
    public const long MaxFileSizeBytes = 50L * 1024 * 1024;

    /// <summary>MIME allowlist (FRS §13): PDF, JPEG, PNG, DOCX, XLSX, TXT, CSV.</summary>
    public static readonly IReadOnlyDictionary<string, string[]> AllowedTypes = new Dictionary<string, string[]>(StringComparer.OrdinalIgnoreCase)
    {
        ["application/pdf"] = new[] { ".pdf" },
        ["image/jpeg"] = new[] { ".jpg", ".jpeg" },
        ["image/png"] = new[] { ".png" },
        ["application/vnd.openxmlformats-officedocument.wordprocessingml.document"] = new[] { ".docx" },
        ["application/vnd.openxmlformats-officedocument.spreadsheetml.sheet"] = new[] { ".xlsx" },
        ["text/plain"] = new[] { ".txt" },
        ["text/csv"] = new[] { ".csv" },
    };

    private static readonly Regex Unsafe = new(@"[^A-Za-z0-9._\- ]", RegexOptions.Compiled);

    /// <summary>
    /// Strips directory components and every character that is not a plain letter, digit, dot, dash, underscore or space,
    /// so a name can never traverse out of the claim's storage folder (BR-D-01).
    /// </summary>
    public static string SanitizeFileName(string fileName)
    {
        // Treat both separators as separators regardless of the host OS.
        var name = fileName.Replace('\\', '/');
        name = name[(name.LastIndexOf('/') + 1)..];
        name = Unsafe.Replace(name, "_").Trim().Trim('.');
        while (name.Contains("..", StringComparison.Ordinal))
        {
            name = name.Replace("..", ".", StringComparison.Ordinal);
        }

        if (name.Length > 150)
        {
            var ext = Path.GetExtension(name);
            name = name[..(150 - ext.Length)] + ext;
        }

        return string.IsNullOrWhiteSpace(name) ? "document" : name;
    }
}

/// <summary>The content stream is consumed by the handler; the command is intentionally not logged or re-played.</summary>
public sealed record UploadClaimDocumentCommand(
    Guid ClaimId,
    string FileName,
    string ContentType,
    long Length,
    Stream Content,
    string? DocumentType = null,
    string? Notes = null) : ICommand<DocumentDto>;

public sealed class UploadClaimDocumentValidator : AbstractValidator<UploadClaimDocumentCommand>
{
    public UploadClaimDocumentValidator()
    {
        RuleFor(c => c.FileName).NotEmpty().WithMessage("A file is required.");
        RuleFor(c => c.Length).GreaterThan(0).WithMessage("The file is empty.")
            .LessThanOrEqualTo(DocumentRules.MaxFileSizeBytes).WithMessage("The file exceeds the 50 MB limit.");
        RuleFor(c => c.ContentType)
            .Must(t => t is not null && DocumentRules.AllowedTypes.ContainsKey(t.Split(';')[0].Trim()))
            .WithMessage("File type is not allowed. Allowed: PDF, JPEG, PNG, DOCX, XLSX, TXT, CSV.");
        RuleFor(c => c).Must(c => ExtensionMatches(c.FileName, c.ContentType))
            .WithMessage("The file extension does not match its content type.").WithName(nameof(UploadClaimDocumentCommand.FileName))
            .When(c => !string.IsNullOrWhiteSpace(c.FileName) && c.ContentType is not null && DocumentRules.AllowedTypes.ContainsKey(c.ContentType.Split(';')[0].Trim()));
        RuleFor(c => c.DocumentType).MaximumLength(100);
        RuleFor(c => c.Notes).MaximumLength(500);
    }

    private static bool ExtensionMatches(string fileName, string contentType) =>
        DocumentRules.AllowedTypes[contentType.Split(';')[0].Trim()]
            .Contains(Path.GetExtension(fileName), StringComparer.OrdinalIgnoreCase);
}

public sealed class UploadClaimDocumentHandler(
    IClaimRepository claims,
    IStorageService storage,
    ICurrentUserService currentUser,
    TimeProvider time,
    IMapper mapper) : IRequestHandler<UploadClaimDocumentCommand, DocumentDto>
{
    public async Task<DocumentDto> Handle(UploadClaimDocumentCommand cmd, CancellationToken ct)
    {
        var (userId, _) = currentUser.Require();
        var claim = await claims.LoadForUpdateAsync(cmd.ClaimId, ct, ClaimIncludes.None);

        // A short unique prefix keeps two uploads with the same file name from overwriting each other's bytes.
        var safeName = DocumentRules.SanitizeFileName(cmd.FileName);
        var relativePath = $"{currentUser.OrganisationId}/{claim.Id}/{SequentialGuid.Next().ToString("N")[..8]}-{safeName}";
        var contentType = cmd.ContentType.Split(';')[0].Trim().ToLowerInvariant();

        var storedPath = await storage.UploadAsync(relativePath, cmd.Content, contentType, ct);

        var document = claim.AddDocument(
            string.IsNullOrWhiteSpace(cmd.DocumentType) ? "Other" : cmd.DocumentType!,
            safeName, storedPath, contentType, cmd.Length, time.GetUtcNow(), userId, cmd.Notes);

        return mapper.Map<DocumentDto>(document);
    }
}
