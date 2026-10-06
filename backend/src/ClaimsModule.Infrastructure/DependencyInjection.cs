using ClaimsModule.Application.Abstractions;
using ClaimsModule.Infrastructure.Auth;
using ClaimsModule.Infrastructure.Jobs;
using ClaimsModule.Infrastructure.Options;
using ClaimsModule.Infrastructure.Storage;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace ClaimsModule.Infrastructure;

public static class DependencyInjection
{
    public static IServiceCollection AddInfrastructure(this IServiceCollection services, IConfiguration configuration, bool backgroundJobsEnabled = true)
    {
        services.Configure<TenantOptions>(configuration.GetSection(TenantOptions.Section));
        services.Configure<StorageOptions>(configuration.GetSection(StorageOptions.Section));
        services.Configure<JwtOptions>(configuration.GetSection(JwtOptions.Section));
        services.Configure<MockUsersOptions>(configuration.GetSection(MockUsersOptions.Section));

        services.AddSingleton(TimeProvider.System);

        // --- storage: provider chosen in appsettings; AzureBlob without a connection string falls back to local files.
        services.AddSingleton<LocalFileSystemStorageService>();
        services.AddSingleton<IStorageService>(sp =>
        {
            var options = sp.GetRequiredService<IOptions<StorageOptions>>().Value;
            var azure = options.Provider.Equals("AzureBlob", StringComparison.OrdinalIgnoreCase);

            if (azure && !string.IsNullOrWhiteSpace(options.AzureBlob.ConnectionString))
            {
                return new AzureBlobStorageService(sp.GetRequiredService<IOptions<StorageOptions>>());
            }

            if (azure)
            {
                sp.GetRequiredService<ILoggerFactory>().CreateLogger("Storage").LogWarning(
                    "Storage:Provider is AzureBlob but no connection string is configured; falling back to the local file system");
            }

            return sp.GetRequiredService<LocalFileSystemStorageService>();
        });

        // --- identity (mock) and user directory
        services.AddSingleton<MockUserStore>();
        services.AddSingleton<IUserDirectory>(sp => sp.GetRequiredService<MockUserStore>());
        services.AddSingleton<JwtTokenService>();

        // --- background processing
        services.AddScoped<BackgroundExecutionContext>();
        services.AddTransient<PostGlReserveChangeJob>();
        services.AddTransient<SlaMonitoringJob>();
        if (backgroundJobsEnabled)
        {
            services.AddSingleton<IBackgroundJobScheduler, HangfireJobScheduler>();
        }
        else
        {
            services.AddSingleton<IBackgroundJobScheduler, NullJobScheduler>();
        }

        return services;
    }
}
