using FluentValidation;
using MangaScrapper.Application.Common.Abstractions;
using MangaScrapper.Application.Common.Behaviors;
using MediatR;
using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.DependencyInjection;

namespace MangaScrapper.Application.DependencyInjection;

public static class ApplicationExtensions
{
    public static IServiceCollection AddMangaScrapperApplication(this IServiceCollection services)
    {
        var assembly = typeof(ApplicationExtensions).Assembly;

        services.AddMediatR(cfg =>
        {
            cfg.RegisterServicesFromAssembly(assembly);
            cfg.AddBehavior(typeof(IPipelineBehavior<,>), typeof(LoggingBehavior<,>));
            cfg.AddBehavior(typeof(IPipelineBehavior<,>), typeof(ValidationBehavior<,>));
        });

        services.AddValidatorsFromAssembly(assembly);

        return services;
    }

    public static WebApplication MapMangaScrapperEndpoints(this WebApplication app)
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
