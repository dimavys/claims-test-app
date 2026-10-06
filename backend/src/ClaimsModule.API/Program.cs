using ClaimsModule.API.Extensions;
using ClaimsModule.API.Infrastructure;
using ClaimsModule.Application;
using ClaimsModule.Infrastructure;
using ClaimsModule.Infrastructure.Jobs;
using ClaimsModule.Infrastructure.Options;
using ClaimsModule.Persistence;
using Hangfire;
using Microsoft.EntityFrameworkCore;
using Serilog;

var builder = WebApplication.CreateBuilder(args);

builder.Host.UseSerilog((context, services, logger) => logger
    .ReadFrom.Configuration(context.Configuration)
    .Enrich.FromLogContext()
    .WriteTo.Console(outputTemplate: "[{Timestamp:HH:mm:ss} {Level:u3}] {CorrelationId} {SourceContext}: {Message:lj}{NewLine}{Exception}"));

var configuration = builder.Configuration;
var backgroundJobsEnabled = configuration.GetValue("Hangfire:Enabled", true);

if (configuration.GetSection(TenantOptions.Section).Get<TenantOptions>()?.OrganisationId == Guid.Empty ||
    configuration.GetSection(TenantOptions.Section).Get<TenantOptions>() is null)
{
    throw new InvalidOperationException("Tenant:OrganisationId must be configured.");
}

builder.Services
    .AddApplication()
    .AddPersistence(configuration)
    .AddInfrastructure(configuration, backgroundJobsEnabled)
    .AddApiServices()
    .AddJwtAuthentication(configuration)
    .AddSwagger();

if (backgroundJobsEnabled)
{
    builder.Services.AddBackgroundProcessing(configuration);
}

builder.Services.AddCors(o => o.AddPolicy("Frontend", policy => policy
    .WithOrigins(configuration.GetSection("Cors:AllowedOrigins").Get<string[]>() ?? Array.Empty<string>())
    .AllowAnyHeader()
    .AllowAnyMethod()
    .WithExposedHeaders(CorrelationMiddleware.HeaderName, "Idempotent-Replayed", "Location", "Content-Disposition")));

var app = builder.Build();

// Migrations only (reference data is seeded by the migrations themselves, never by startup code).
if (configuration.GetValue("Database:MigrateOnStartup", false))
{
    await using var scope = app.Services.CreateAsyncScope();
    await scope.ServiceProvider.GetRequiredService<ClaimsDbContext>().Database.MigrateAsync();
}

app.UseMiddleware<CorrelationMiddleware>();
app.UseSerilogRequestLogging();                      // outside the handler so it logs the final status (422, not 500)
app.UseMiddleware<ExceptionHandlingMiddleware>();

// Swagger stays on in every environment: reviewers use it against the deployed API.
app.UseSwagger();
app.UseSwaggerUI(c =>
{
    c.SwaggerEndpoint("/swagger/v1/swagger.json", "Claims Module API v1");
    c.DocumentTitle = "Claims Module API";
});

app.UseCors("Frontend");
app.UseAuthentication();
app.UseAuthorization();
app.UseMiddleware<IdempotencyMiddleware>();

app.MapControllers();
app.MapGet("/health", async (ClaimsDbContext db, CancellationToken ct) =>
        await db.Database.CanConnectAsync(ct)
            ? Results.Ok(new { status = "Healthy" })
            : Results.Json(new { status = "Unhealthy" }, statusCode: StatusCodes.Status503ServiceUnavailable))
    .AllowAnonymous();

if (backgroundJobsEnabled)
{
    app.UseHangfireDashboard("/hangfire", new DashboardOptions
    {
        DashboardTitle = "Claims Module jobs",
        Authorization = new[] { new HangfireDashboardAuthorization(configuration.GetValue("Hangfire:AllowAnonymousDashboard", false)) },
    });

    // Both jobs are registered at startup: the SLA scan is recurring; GL posting is enqueued per approved reserve.
    app.Services.GetRequiredService<IRecurringJobManager>()
        .AddOrUpdate<SlaMonitoringJob>(SlaMonitoringJob.JobId, j => j.Execute(), SlaMonitoringJob.CronEvery15Minutes);
}

app.Run();

/// <summary>Exposed so integration tests can host the API in-process.</summary>
public partial class Program;
