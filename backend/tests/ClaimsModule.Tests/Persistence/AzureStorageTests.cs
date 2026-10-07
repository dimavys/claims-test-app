using ClaimsModule.Infrastructure.Options;
using ClaimsModule.Infrastructure.Storage;
using FluentAssertions;
using Microsoft.Extensions.Options;

namespace ClaimsModule.Tests.Persistence;

public class AzureStorageTests
{
    // SAS generation is local signing with the account key, so the Azurite dev account needs no network.
    private static AzureBlobStorageService Service() => new(Options.Create(new StorageOptions
    {
        Provider = "AzureBlob",
        AzureBlob = new AzureBlobOptions { ConnectionString = "UseDevelopmentStorage=true", ContainerName = "claim-documents" },
    }));

    [Fact]
    public async Task Download_url_is_read_only_expires_in_an_hour_and_names_the_file_cleanly()
    {
        var url = await Service().GetDownloadUrlAsync("claim-documents/org-1/claim-1/ebf7473e-Police Report.pdf", TimeSpan.FromHours(1));

        var query = System.Web.HttpUtility.ParseQueryString(url.Query);
        query["sp"].Should().Be("r", "read-only");
        query["sr"].Should().Be("b");
        DateTimeOffset.Parse(query["se"]!).Should().BeCloseTo(DateTimeOffset.UtcNow.AddHours(1), TimeSpan.FromMinutes(2));
        query["rscd"].Should().Be("attachment; filename=\"Police Report.pdf\"", "users download the clean name, not the prefixed blob name");
        query["sig"].Should().NotBeNullOrEmpty();
        Uri.UnescapeDataString(url.AbsolutePath).Should().EndWith("/claim-documents/org-1/claim-1/ebf7473e-Police Report.pdf");
    }

    [Fact]
    public async Task A_path_without_the_container_prefix_is_accepted_too()
    {
        var url = await Service().GetDownloadUrlAsync("org-1/claim-1/0a1b2c3d-a.csv", TimeSpan.FromMinutes(5));

        url.AbsolutePath.Should().EndWith("/claim-documents/org-1/claim-1/0a1b2c3d-a.csv");
        System.Web.HttpUtility.ParseQueryString(url.Query)["rscd"].Should().Contain("filename=\"a.csv\"");
    }
}
