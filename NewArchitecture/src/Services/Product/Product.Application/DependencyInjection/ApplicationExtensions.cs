using FluentValidation;
using MediatR;
using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.DependencyInjection;
using Product.Application.Common.Abstractions;
using Product.Application.Common.Behaviors;

namespace Product.Application.DependencyInjection;

/// <summary>Registers Application layer services: MediatR, FluentValidation, pipeline behaviors.</summary>
public static class ApplicationExtensions
{
    public static IServiceCollection AddProductApplication(this IServiceCollection services)
    {
        var assembly = typeof(ApplicationExtensions).Assembly;

        // MediatR with pipeline behaviors
        services.AddMediatR(cfg =>
        {
            cfg.RegisterServicesFromAssembly(assembly);
            cfg.AddBehavior(typeof(IPipelineBehavior<,>), typeof(LoggingBehavior<,>));
            cfg.AddBehavior(typeof(IPipelineBehavior<,>), typeof(ValidationBehavior<,>));
        });

        // FluentValidation
        services.AddValidatorsFromAssembly(assembly);

        return services;
    }

    /// <summary>
    ///     Scans the Application assembly for all <see cref="IEndpointDefinition" /> implementations
    ///     and registers their routes. Call from app.UseEndpoints or after app.Build().
    /// </summary>
    public static WebApplication MapProductEndpoints(this WebApplication app)
    {
        var endpointDefinitions = typeof(ApplicationExtensions).Assembly
            .GetTypes()
            .Where(t => typeof(IEndpointDefinition).IsAssignableFrom(t)
                        && t is { IsInterface: false, IsAbstract: false })
            .Select(Activator.CreateInstance)
            .Cast<IEndpointDefinition>();

        foreach (var definition in endpointDefinitions)
            definition.DefineEndpoints(app);

        return app;
    }
}