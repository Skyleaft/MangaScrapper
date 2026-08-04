using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using MongoDB.Driver;
using NovaStack.Infrastructure.DependencyInjection;
using NovaStack.Infrastructure.Messaging.Options;
using NovaStack.Infrastructure.Persistence.Options;
using NovaStack.SharedKernel.Abstractions;
using Product.Domain.Repositories;
using Product.Infrastructure.Persistence;
using Product.Infrastructure.Repositories;

namespace Product.Infrastructure.DependencyInjection;

/// <summary>
///     Registers all Product infrastructure services:
///     EF Core, Repository, Unit of Work, and MassTransit messaging.
/// </summary>
public static class InfrastructureExtensions
{
    public static IServiceCollection AddProductInfrastructure(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        services
            .AddProductDatabase(configuration)
            .AddProductMessaging(configuration)
            .AddProductRepositories();

        return services;
    }

    // ── Database ──────────────────────────────────────────────────────────
    private static IServiceCollection AddProductDatabase(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        var dbOptions = configuration
            .GetSection(DatabaseOptions.SectionName)
            .Get<DatabaseOptions>() ?? new DatabaseOptions();

        services.Configure<DatabaseOptions>(
            configuration.GetSection(DatabaseOptions.SectionName));

        if (dbOptions.Provider == DatabaseProvider.MongoDB)
        {
            // Register native MongoDB client (singleton — connection-pooled by the driver)
            services.AddSingleton<IMongoClient>(_ =>
                new MongoClient(dbOptions.ConnectionString));

            // Register the MongoDB context for the Product service (scoped)
            services.AddScoped<ProductMongoDbContext>(sp =>
                new ProductMongoDbContext(
                    sp.GetRequiredService<IMongoClient>(),
                    dbOptions.DatabaseName));

            return services;
        }

        // ── EF Core (PostgreSQL / SQL Server) ──────────────────────────────
        services.AddDbContext<ProductDbContext>(options =>
        {
            switch (dbOptions.Provider)
            {
                case DatabaseProvider.PostgreSQL:
                    options.UseNpgsql(dbOptions.ConnectionString, npgsql =>
                    {
                        npgsql.MigrationsHistoryTable("__ef_migrations_history", "products");
                        npgsql.EnableRetryOnFailure(3);
                    });
                    break;

                case DatabaseProvider.SqlServer:
                    options.UseSqlServer(dbOptions.ConnectionString, sql =>
                    {
                        sql.MigrationsHistoryTable("__ef_migrations_history", "products");
                        sql.EnableRetryOnFailure(3);
                    });
                    break;

                default:
                    throw new InvalidOperationException(
                        $"Unsupported database provider: {dbOptions.Provider}");
            }

            if (dbOptions.EnableDetailedErrors)
                options.EnableDetailedErrors();

            if (dbOptions.EnableSensitiveDataLogging)
                options.EnableSensitiveDataLogging();
        });

        // Register Unit of Work
        services.AddScoped<IUnitOfWork>(sp =>
            sp.GetRequiredService<ProductDbContext>());

        // Register SQL Connection Factory for Dapper queries
        services.AddScoped<ISqlConnectionFactory, SqlConnectionFactory>();

        return services;
    }

    // ── Repositories ──────────────────────────────────────────────────────
    private static IServiceCollection AddProductRepositories(
        this IServiceCollection services)
    {
        // Resolve the correct repository implementation based on the registered provider.
        // If ProductMongoDbContext is registered (MongoDB path), use MongoProductRepository;
        // otherwise fall back to the EF Core ProductRepository.
        services.AddScoped<IProductRepository>(sp =>
        {
            var mongoCtx = sp.GetService<ProductMongoDbContext>();
            if (mongoCtx is not null)
                return new MongoProductRepository(mongoCtx);

            return new ProductRepository(sp.GetRequiredService<ProductDbContext>());
        });

        return services;
    }

    // ── Messaging (Native Clients) ───────────────────────────────────────────
    private static IServiceCollection AddProductMessaging(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        var messagingOptions = configuration
            .GetSection(MessagingOptions.SectionName)
            .Get<MessagingOptions>() ?? new MessagingOptions();

        services.Configure<MessagingOptions>(
            configuration.GetSection(MessagingOptions.SectionName));

        switch (messagingOptions.Provider)
        {
            case MessagingProvider.RabbitMQ:
                services.AddNativeRabbitMqEventBus();
                break;

            case MessagingProvider.Kafka:
                services.AddNativeKafkaEventBus();
                break;

            default:
                services.AddNativeRabbitMqEventBus();
                break;
        }

        return services;
    }

    /// <summary>Runs EF Core migrations at startup if AutoMigrate is enabled. No-op for MongoDB.</summary>
    public static async Task MigrateProductDatabaseAsync(this IServiceProvider serviceProvider)
    {
        using var scope = serviceProvider.CreateScope();
        var dbOptions = scope.ServiceProvider
            .GetRequiredService<IOptions<DatabaseOptions>>().Value;

        // MongoDB has no EF migrations — schema is schemaless by design.
        if (!dbOptions.AutoMigrate || dbOptions.Provider == DatabaseProvider.MongoDB) return;

        var context = scope.ServiceProvider.GetRequiredService<ProductDbContext>();
        await context.Database.MigrateAsync();
    }
}