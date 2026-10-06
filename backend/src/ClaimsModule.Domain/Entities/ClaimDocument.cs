using ClaimsModule.Domain.Common;

namespace ClaimsModule.Domain.Entities;

public class ClaimDocument : BaseEntity
{
    private ClaimDocument() { }

    public Guid ClaimId { get; private set; }
    public string DocumentType { get; private set; } = string.Empty;
    public string DocumentName { get; private set; } = string.Empty;
    public string BlobPath { get; private set; } = string.Empty;
    public string ContentType { get; private set; } = string.Empty;
    public long FileSizeBytes { get; private set; }
    public DateTimeOffset UploadedAt { get; private set; }
    public Guid? UploadedByUserId { get; private set; }
    public string? Notes { get; private set; }

    internal static ClaimDocument Create(
        Guid claimId, string documentType, string documentName, string blobPath, string contentType,
        long fileSizeBytes, Guid? uploadedBy, string? notes, DateTimeOffset now)
    {
        if (string.IsNullOrWhiteSpace(documentName) || string.IsNullOrWhiteSpace(blobPath))
        {
            throw new DomainException("Document name and storage path are required.", field: "Document");
        }

        if (fileSizeBytes <= 0)
        {
            throw new DomainException("Document is empty.", field: "Document");
        }

        return new ClaimDocument
        {
            ClaimId = claimId,
            DocumentType = string.IsNullOrWhiteSpace(documentType) ? "Other" : documentType.Trim(),
            DocumentName = documentName,
            BlobPath = blobPath,
            ContentType = contentType,
            FileSizeBytes = fileSizeBytes,
            UploadedAt = now,
            UploadedByUserId = uploadedBy,
            Notes = notes,
        };
    }
}
