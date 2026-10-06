using ClaimsModule.Application.Abstractions;
using ClaimsModule.Persistence.Repositories;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace ClaimsModule.Persistence;

public static class DependencyInjection
{
    public static IServiceCollection AddPersistence(this IServiceCollection services, IConfiguration configuration)
    {
        var connectionString = configuration.GetConnectionString("ClaimsDb")
            ?? throw new InvalidOperationException("Connection string 'ClaimsDb' is not configured.");

        services.AddSingleton(TimeProvider.System);
        services.AddDbContext<ClaimsDbContext>(options => options.UseSqlServer(
            connectionString,
            sql =>
            {
                sql.MigrationsAssembly(typeof(ClaimsDbContext).Assembly.FullName);
                sql.EnableRetryOnFailure();
            }));
        services.AddScoped<IClaimNumberGenerator, ClaimNumberGenerator>();
        services.AddScoped<IUnitOfWork, UnitOfWork>();
        services.AddScoped<IClaimRepository, ClaimRepository>();
        services.AddScoped<IReferenceDataRepository, ReferenceDataRepository>();
        services.AddScoped<IAuditLogRepository, AuditLogRepository>();
        return services;
    }
}
