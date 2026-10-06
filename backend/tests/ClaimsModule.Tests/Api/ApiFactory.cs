using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using ClaimsModule.Application.Abstractions;
using ClaimsModule.Tests.Application;
using ClaimsModule.Tests.Persistence;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;

namespace ClaimsModule.Tests.Api;

/// <summary>Hosts the real API in-process against the throw-away SQL Server database (Hangfire server off, jobs recorded).</summary>
internal sealed class ApiFactory : WebApplicationFactory<Program>
{
    private readonly SqlServerFixture _sql;

    public string UploadRoot { get; } = Path.Combine(Path.GetTempPath(), "claims-api-tests-" + Guid.NewGuid().ToString("N")[..8]);
    public RecordingScheduler Scheduler { get; } = new();

    public ApiFactory(SqlServerFixture sql) => _sql = sql;

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Testing");
        builder.UseSetting("ConnectionStrings:ClaimsDb", _sql.ConnectionString);
        builder.UseSetting("Jwt:SigningKey", "test-signing-key-0123456789-abcdefghijklmnopqrstuvwxyz");
        builder.UseSetting("Storage:Provider", "LocalFileSystem");
        builder.UseSetting("Storage:LocalFileSystem:SigningKey", "test-local-file-signing-key");
        builder.UseSetting("Storage:LocalFileSystem:RootPath", UploadRoot);
        builder.UseSetting("Storage:LocalFileSystem:PublicBaseUrl", "http://localhost");
        builder.UseSetting("Hangfire:Enabled", "false");
        builder.UseSetting("Database:MigrateOnStartup", "false");

        builder.ConfigureServices(services =>
        {
            services.AddSingleton<IBackgroundJobScheduler>(Scheduler);
        });
    }

    protected override void Dispose(bool disposing)
    {
        base.Dispose(disposing);
        if (disposing && Directory.Exists(UploadRoot))
        {
            Directory.Delete(UploadRoot, recursive: true);
        }
    }
}

internal static class ApiClientExtensions
{
    public static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web)
    {
        Converters = { new JsonStringEnumConverter() },
    };

    public static async Task<HttpClient> LoginAsync(this ApiFactory factory, string userName, string password)
    {
        var client = factory.CreateClient();
        var response = await client.PostAsJsonAsync("/api/auth/login", new { userName, password });
        response.EnsureSuccessStatusCode();
        var token = (await response.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("accessToken").GetString()!;
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return client;
    }

    public static Task<HttpClient> AsHandlerAsync(this ApiFactory f) => f.LoginAsync("handler", "Handler#2026");
    public static Task<HttpClient> AsHandler2Async(this ApiFactory f) => f.LoginAsync("handler2", "Handler#2026");
    public static Task<HttpClient> AsSupervisorAsync(this ApiFactory f) => f.LoginAsync("supervisor", "Supervisor#2026");
    public static Task<HttpClient> AsManagerAsync(this ApiFactory f) => f.LoginAsync("manager", "Manager#2026");

    public static async Task<JsonElement> JsonAsync(this HttpResponseMessage response) =>
        await response.Content.ReadFromJsonAsync<JsonElement>(Json);
}
