using System.Text;
using ClaimsModule.Application.Common;
using ClaimsModule.Application.Features.Claims.Commands;
using ClaimsModule.Application.Features.Claims.Queries;
using ClaimsModule.Application.Features.Reference;
using ClaimsModule.Domain.Entities;
using ClaimsModule.Domain.Enums;
using ClaimsModule.Tests.Persistence;
using FluentAssertions;
using static ClaimsModule.Tests.Application.Users;

namespace ClaimsModule.Tests.Application;

public class DocumentRulesTests
{
    [Theory]
    [InlineData("report.pdf", "report.pdf")]
    [InlineData("../../etc/passwd", "passwd")]
    [InlineData("..\\..\\windows\\system32\\cmd.exe", "cmd.exe")]
    [InlineData("/abs/path/photo.png", "photo.png")]
    [InlineData("my report (final) #2.pdf", "my report _final_ _2.pdf")]
    [InlineData("a/../b.txt", "b.txt")]
    [InlineData("....", "document")]
    [InlineData("   ", "document")]
    [InlineData("na\0me.csv", "na_me.csv")]
    public void File_names_are_sanitised_against_path_traversal(string input, string expected) =>
        DocumentRules.SanitizeFileName(input).Should().Be(expected);

    [Theory]
    [InlineData("ebf7473e-report.txt", "report.txt")]
    [InlineData("claim-documents/org/claim/ebf7473e-Police Report.pdf", "Police Report.pdf")]
    [InlineData("org\\claim\\0a1b2c3d-a-b.csv", "a-b.csv")]
    [InlineData("report.txt", "report.txt")]          // no prefix: unchanged
    [InlineData("zzzzzzzz-report.txt", "zzzzzzzz-report.txt")] // not hex: not our prefix
    public void Display_name_strips_the_internal_uniqueness_prefix(string stored, string expected) =>
        DocumentRules.DisplayName(stored).Should().Be(expected);

    [Fact]
    public void Very_long_names_are_truncated_keeping_the_extension() =>
        DocumentRules.SanitizeFileName(new string('a', 400) + ".pdf").Should().HaveLength(150).And.EndWith(".pdf");

    [Theory]
    [InlineData("application/pdf", "a.pdf", true)]
    [InlineData("image/jpeg", "a.JPG", true)]
    [InlineData("text/csv", "a.csv", true)]
    [InlineData("application/x-msdownload", "a.exe", false)]
    [InlineData("text/html", "a.html", false)]
    [InlineData("application/pdf", "a.exe", false)]
    public async Task Validator_enforces_the_mime_allowlist_and_extension(string contentType, string name, bool valid)
    {
        var cmd = new UploadClaimDocumentCommand(Guid.NewGuid(), name, contentType, 10, new MemoryStream(new byte[10]));

        var result = await new UploadClaimDocumentValidator().ValidateAsync(cmd);

        result.IsValid.Should().Be(valid);
    }

    [Fact]
    public async Task Validator_rejects_empty_and_oversized_files()
    {
        var validator = new UploadClaimDocumentValidator();
        (await validator.ValidateAsync(new UploadClaimDocumentCommand(Guid.NewGuid(), "a.pdf", "application/pdf", 0, Stream.Null))).IsValid.Should().BeFalse();
        (await validator.ValidateAsync(new UploadClaimDocumentCommand(Guid.NewGuid(), "a.pdf", "application/pdf", DocumentRules.MaxFileSizeBytes + 1, Stream.Null))).IsValid.Should().BeFalse();
        (await validator.ValidateAsync(new UploadClaimDocumentCommand(Guid.NewGuid(), "a.pdf", "application/pdf", DocumentRules.MaxFileSizeBytes, Stream.Null))).IsValid.Should().BeTrue();
    }
}

[Collection(SqlServerCollection.Name)]
public sealed class DocumentFlowTests : IDisposable
{
    private readonly SqlServerFixture _sql;
    private readonly AppHarness _app;

    public DocumentFlowTests(SqlServerFixture sql)
    {
        _sql = sql;
        _app = new AppHarness(sql);
    }

    public void Dispose() => _app.Dispose();

    private async Task<Guid> NewClaim()
    {
        Skip.IfNot(_sql.Available, "SQL Server not available");
        var policy = (await _app.Send(new SearchPoliciesQuery("POL-2024-001001"))).Single().Id;
        return (await _app.Send(new CreateClaimCommand(
            policy, DateTimeOffset.UtcNow.AddDays(-1), "Cargo stolen from locked trailer overnight", "COL-THEFT",
            Parties: new[] { new PartyInput(PartyRole.Claimant, PartyType.Person, "Ada", "Lovelace") }))).Id;
    }

    private static UploadClaimDocumentCommand Upload(Guid claim, string name, string type, string body = "hello") =>
        new(claim, name, type, Encoding.UTF8.GetByteCount(body), new MemoryStream(Encoding.UTF8.GetBytes(body)), "PoliceReport", "scanned copy");

    [SkippableFact]
    public async Task Upload_stores_the_bytes_saves_metadata_audits_and_lists_a_one_hour_url()
    {
        var claim = await NewClaim();

        var doc = await _app.Send(Upload(claim, "../../Police Report.pdf", "application/pdf", "%PDF-fake"), Handler);

        doc.DocumentName.Should().Be("Police Report.pdf");
        doc.UploadedByName.Should().Be(Handler.Name);
        var stored = _app.Storage.Files.Single(f => f.Key.Contains($"/{claim}/") && f.Key.EndsWith("-Police Report.pdf"));
        stored.Key.Should().StartWith("claim-documents/").And.NotContain("..");
        Encoding.UTF8.GetString(stored.Value.Bytes).Should().Be("%PDF-fake");

        var audit = (await _app.Send(new GetClaimAuditQuery(claim))).Items.Single(a => a.EventType == AuditEventTypes.DocumentUploaded);
        audit.RelatedEntityId.Should().Be(doc.Id);
        audit.RelatedEntityType.Should().Be(nameof(ClaimDocument));

        var listed = (await _app.Send(new ListClaimDocumentsQuery(claim))).Should().ContainSingle().Subject;
        listed.DownloadUrl.Should().StartWith("https://storage.test/claim-documents/").And.Contain("ttl=3600");
        listed.DownloadUrlExpiresAt.Should().BeCloseTo(DateTimeOffset.UtcNow.AddHours(1), TimeSpan.FromMinutes(1));
        listed.FileSizeBytes.Should().Be(9);
        (await _app.Send(new GetClaimDetailQuery(claim))).Documents.Should().ContainSingle();
    }

    [SkippableFact]
    public async Task Two_uploads_with_the_same_name_never_overwrite_each_other()
    {
        var claim = await NewClaim();

        await _app.Send(Upload(claim, "invoice.pdf", "application/pdf", "first"));
        await _app.Send(Upload(claim, "invoice.pdf", "application/pdf", "second"));

        var paths = (await _app.Send(new ListClaimDocumentsQuery(claim))).Count;
        paths.Should().Be(2);
        _app.Storage.Files.Where(f => f.Key.Contains($"/{claim}/")).Select(f => Encoding.UTF8.GetString(f.Value.Bytes))
            .Should().BeEquivalentTo("first", "second");
    }

    [SkippableFact]
    public async Task Disallowed_file_types_are_rejected_before_anything_is_stored()
    {
        var claim = await NewClaim();
        var storedBefore = _app.Storage.Files.Count;

        await FluentActions.Invoking(() => _app.Send(Upload(claim, "malware.exe", "application/x-msdownload")))
            .Should().ThrowAsync<RequestValidationException>();

        _app.Storage.Files.Count.Should().Be(storedBefore);
        (await _app.Send(new ListClaimDocumentsQuery(claim))).Should().BeEmpty();
    }

    [SkippableFact]
    public async Task Upload_to_an_unknown_claim_is_not_found()
    {
        Skip.IfNot(_sql.Available, "SQL Server not available");

        await FluentActions.Invoking(() => _app.Send(Upload(Guid.NewGuid(), "a.pdf", "application/pdf")))
            .Should().ThrowAsync<NotFoundException>();
    }
}
