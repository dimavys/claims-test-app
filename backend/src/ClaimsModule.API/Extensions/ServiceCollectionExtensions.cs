using System.Text.Json.Serialization;
using ClaimsModule.API.Infrastructure;
using ClaimsModule.Application.Abstractions;
using ClaimsModule.Infrastructure.Auth;
using ClaimsModule.Infrastructure.Options;
using Hangfire;
using Hangfire.SqlServer;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Authorization;
using Microsoft.IdentityModel.Tokens;
using Microsoft.OpenApi.Models;

namespace ClaimsModule.API.Extensions;

public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddApiServices(this IServiceCollection services)
    {
        services.AddHttpContextAccessor();
        services.AddMemoryCache();

        services.AddScoped<CorrelationContext>();
        services.AddScoped<ICorrelationContext>(sp => sp.GetRequiredService<CorrelationContext>());
        services.AddScoped<ICurrentUserService, CurrentUserService>();

        services.AddControllers(options =>
            {
                // Secure by default: only [AllowAnonymous] endpoints are open.
                options.Filters.Add(new AuthorizeFilter(new AuthorizationPolicyBuilder().RequireAuthenticatedUser().Build()));
            })
            .AddJsonOptions(o => o.JsonSerializerOptions.Converters.Add(new JsonStringEnumConverter()))
            .ConfigureApiBehaviorOptions(o =>
            {
                // Malformed bodies / binding errors use the same 422 error shape as business validation (FRS §10.4).
                o.InvalidModelStateResponseFactory = context =>
                {
                    var errors = context.ModelState
                        .Where(e => e.Value?.Errors.Count > 0)
                        .ToDictionary(
                            e => string.IsNullOrEmpty(e.Key) ? "Request" : e.Key.TrimStart('$', '.'),
                            e => e.Value!.Errors.Select(x => string.IsNullOrWhiteSpace(x.ErrorMessage) ? "The value is invalid." : x.ErrorMessage).ToArray());
                    var error = new ApiError
                    {
                        Type = "ValidationError", Title = "One or more validation errors occurred.",
                        Status = StatusCodes.Status422UnprocessableEntity, Errors = errors,
                        CorrelationId = CorrelationMiddleware.Get(context.HttpContext),
                    };
                    return new ObjectResult(error) { StatusCode = error.Status, ContentTypes = { "application/json" } };
                };
            });

        return services;
    }

    public static IServiceCollection AddJwtAuthentication(this IServiceCollection services, IConfiguration configuration)
    {
        var jwt = configuration.GetSection(JwtOptions.Section).Get<JwtOptions>() ?? new JwtOptions();
        if (jwt.SigningKey.Length < 32)
        {
            throw new InvalidOperationException(
                "Jwt:SigningKey must be configured with at least 32 characters (set the Jwt__SigningKey environment variable or a Key Vault secret).");
        }

        services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme).AddJwtBearer(o =>
        {
            o.MapInboundClaims = false; // keep short claim names: sub, name, role, org
            o.TokenValidationParameters = new TokenValidationParameters
            {
                ValidIssuer = jwt.Issuer,
                ValidAudience = jwt.Audience,
                IssuerSigningKey = JwtTokenService.KeyFor(jwt),
                NameClaimType = JwtTokenService.NameClaim,
                RoleClaimType = JwtTokenService.RoleClaim,
                ClockSkew = TimeSpan.FromSeconds(30),
            };
            o.Events = new JwtBearerEvents
            {
                OnChallenge = ctx =>
                {
                    ctx.HandleResponse();
                    return ApiErrors.WriteAsync(ctx.HttpContext, ApiErrors.Unauthorized());
                },
                OnForbidden = ctx => ApiErrors.WriteAsync(ctx.HttpContext, ApiErrors.Forbidden()),
            };
        });
        services.AddAuthorization();
        return services;
    }

    public static IServiceCollection AddSwagger(this IServiceCollection services)
    {
        services.AddEndpointsApiExplorer();
        services.AddSwaggerGen(c =>
        {
            c.SwaggerDoc("v1", new OpenApiInfo
            {
                Title = "Claims Module API",
                Version = "v1",
                Description = "FNOL intake and reserve management. Authenticate with POST /api/auth/login, then click Authorize and paste the token.",
            });
            c.AddSecurityDefinition("Bearer", new OpenApiSecurityScheme
            {
                Description = "JWT from /api/auth/login (paste the token only).",
                Name = "Authorization", In = ParameterLocation.Header, Type = SecuritySchemeType.Http, Scheme = "bearer", BearerFormat = "JWT",
            });
            c.AddSecurityRequirement(new OpenApiSecurityRequirement
            {
                [new OpenApiSecurityScheme { Reference = new OpenApiReference { Type = ReferenceType.SecurityScheme, Id = "Bearer" } }] = Array.Empty<string>(),
            });
            c.SupportNonNullableReferenceTypes();
            c.CustomSchemaIds(t => t.FullName!.Replace("ClaimsModule.", string.Empty).Replace('+', '.'));
        });
        return services;
    }

    public static IServiceCollection AddBackgroundProcessing(this IServiceCollection services, IConfiguration configuration)
    {
        var connectionString = configuration.GetConnectionString("ClaimsDb")!;

        services.AddHangfire(c => c
            .SetDataCompatibilityLevel(CompatibilityLevel.Version_180)
            .UseSimpleAssemblyNameTypeSerializer()
            .UseRecommendedSerializerSettings()
            .UseSqlServerStorage(connectionString, new SqlServerStorageOptions
            {
                SchemaName = "HangFire",
                PrepareSchemaIfNecessary = true, // Hangfire owns its own tables, separate from the EF-migrated schema
                QueuePollInterval = TimeSpan.FromSeconds(5),
                UseRecommendedIsolationLevel = true,
                DisableGlobalLocks = true,
            }));
        services.AddHangfireServer(o => o.WorkerCount = Math.Max(2, Environment.ProcessorCount));
        return services;
    }
}
