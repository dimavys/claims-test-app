using ClaimsModule.Application.Abstractions;
using ClaimsModule.Application.Behaviors;
using ClaimsModule.Application.Mapping;
using ClaimsModule.Application.Services;
using FluentValidation;
using Microsoft.Extensions.DependencyInjection;

namespace ClaimsModule.Application;

public static class DependencyInjection
{
    public static IServiceCollection AddApplication(this IServiceCollection services)
    {
        var assembly = typeof(DependencyInjection).Assembly;

        services.AddMediatR(cfg =>
        {
            cfg.RegisterServicesFromAssembly(assembly);

            // Registration order is execution order: logging → validation → transaction (commands only).
            cfg.AddOpenBehavior(typeof(LoggingBehavior<,>));
            cfg.AddOpenBehavior(typeof(ValidationBehavior<,>));
            cfg.AddOpenBehavior(typeof(TransactionBehavior<,>));
        });

        services.AddValidatorsFromAssembly(assembly);
        services.AddAutoMapper(cfg => cfg.AddMaps(assembly));
        services.AddTransient<UserNameResolver>();
        services.AddTransient<ActorNameResolver>(); // AutoMapper resolves value resolvers from the container
        services.AddScoped<IAuditLogService, AuditLogService>();
        services.AddScoped<IAfterCommitActions, AfterCommitActions>();
        return services;
    }
}
