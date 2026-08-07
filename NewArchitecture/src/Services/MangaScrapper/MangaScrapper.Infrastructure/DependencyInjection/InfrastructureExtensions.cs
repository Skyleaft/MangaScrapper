using Hangfire;
using Hangfire.Mongo;
using Hangfire.Mongo.Migration.Strategies;
using Hangfire.Mongo.Migration.Strategies.Backup;
using MangaScrapper.Domain.Repositories;
using MangaScrapper.Infrastructure.Configuration;
using MangaScrapper.Infrastructure.Persistence;
using MangaScrapper.Infrastructure.Repositories;
using MangaScrapper.Infrastructure.Scrapers;
using MangaScrapper.Application.Common.Abstractions;
using MangaScrapper.Infrastructure.Security;
using MangaScrapper.Infrastructure.Services;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using MongoDB.Bson.Serialization;
using MongoDB.Bson.Serialization.Conventions;
using MongoDB.Bson.Serialization.Serializers;
using MongoDB.Bson;
using MongoDB.Driver;
using NovaStack.Infrastructure.Persistence.MongoDb;

namespace MangaScrapper.Infrastructure.DependencyInjection;

public static class InfrastructureExtensions
{
    public static IServiceCollection AddMangaScrapperInfrastructure(
        this IServiceCollection services,
        IConfiguration configuration,
        bool includeHangfireServer = false)
    {
        services
            .AddMongoDb(configuration)
            .AddRepositories()
            .AddExternalServices(configuration)
            .AddScraperServices(configuration)
            .AddHangfireWithMongo(configuration, includeHangfireServer)
            .AddSecurityServices();

        return services;
    }

    private static IServiceCollection AddMongoDb(this IServiceCollection services, IConfiguration configuration)
    {
        var pack = new ConventionPack
        {
            new CamelCaseElementNameConvention(),
            new IgnoreIfNullConvention(true),
            new EnumRepresentationConvention(BsonType.String)
        };
        ConventionRegistry.Register("MangaScrapperConventions", pack, _ => true);

        try
        {
            BsonSerializer.RegisterSerializer(new GuidSerializer(GuidRepresentation.Standard));
        }
        catch (BsonSerializationException)
        {
            // Already registered
        }

        var mongoSettings = configuration.GetSection("MongoDB").Get<MongoSettings>()
                            ?? new MongoSettings();

        services.Configure<MongoSettings>(configuration.GetSection("MongoDB"));
        services.AddSingleton<IMongoClient>(_ => new MongoClient(mongoSettings.ConnectionString));
        services.AddScoped<MangaMongoDbContext>(sp =>
        {
            var client = sp.GetRequiredService<IMongoClient>();
            return new MangaMongoDbContext(client, mongoSettings.DatabaseName);
        });

        return services;
    }

    private static IServiceCollection AddRepositories(this IServiceCollection services)
    {
        services.AddScoped<IMangaRepository, MongoMangaRepository>();
        services.AddScoped<IUserRepository, MongoUserRepository>();
        services.AddScoped<IUserLibraryRepository, MongoUserLibraryRepository>();
        services.AddScoped<IUserProgressionRepository, MongoUserProgressionRepository>();

        services.AddScoped<IScrapperRepository, MongoScrapperRepository>();

        return services;
    }

    private static IServiceCollection AddExternalServices(this IServiceCollection services, IConfiguration configuration)
    {
        services.Configure<MeiliConfig>(configuration.GetSection("Meili"));
        services.Configure<QdrantConfig>(configuration.GetSection("Qdrant"));
        services.Configure<EmbeddingConfig>(configuration.GetSection("Embedding"));
        services.Configure<DiscordWebhookSettings>(configuration.GetSection("Discord"));
        services.Configure<DomainSettings>(configuration.GetSection("Domain"));
        services.Configure<ScrapperSettings>(configuration.GetSection("Scrapper"));
        services.Configure<FlareSolverrSettings>(configuration.GetSection("FlareSolverr"));

        services.AddScoped<MeilisearchService>();
        services.AddScoped<QdrantService>();
        services.AddScoped<StorageSyncService>();

        services.AddHttpClient<DiscordWebhookService>();

        return services;
    }

    private static IServiceCollection AddScraperServices(this IServiceCollection services, IConfiguration configuration)
    {
        var scrapperSettings = configuration.GetSection("Scrapper").Get<ScrapperSettings>() ?? new ScrapperSettings();
        var flareSolverrSettings = configuration.GetSection("FlareSolverr").Get<FlareSolverrSettings>() ?? new FlareSolverrSettings();

        services.AddSingleton(_ => new SemaphoreSlim(scrapperSettings.MaxParallelDownloads, scrapperSettings.MaxParallelDownloads));

        services.AddHttpClient("FlareSolverr", client =>
        {
            client.BaseAddress = new Uri(flareSolverrSettings.Host);
            client.Timeout = TimeSpan.FromSeconds(120);
        });

        services.AddScoped<FlareSolverrService>(sp =>
        {
            var factory = sp.GetRequiredService<IHttpClientFactory>();
            var http = factory.CreateClient("FlareSolverr");
            return new FlareSolverrService(http, factory, flareSolverrSettings.Enabled);
        });

        services.AddHttpClient<KomikuService>();
        services.AddHttpClient<KiryuuService>();
        services.AddHttpClient<KomikcastService>();
        services.AddHttpClient<MangaDexService>();

        services.AddScoped<KomikuService>();
        services.AddScoped<KiryuuService>();
        services.AddScoped<KomikcastService>();
        services.AddScoped<MangaDexService>();

        services.AddKeyedScoped<IScrapperService, KomikuService>("komiku");
        services.AddKeyedScoped<IScrapperService, KiryuuService>("kiryuu");
        services.AddKeyedScoped<IScrapperService, KomikcastService>("komikcast");
        services.AddKeyedScoped<IScrapperService, MangaDexService>("mangadex");

        return services;
    }

    private static IServiceCollection AddHangfireWithMongo(
        this IServiceCollection services,
        IConfiguration configuration,
        bool includeHangfireServer = false)
    {
        var mongoSettings = configuration.GetSection("MongoDB").Get<MongoSettings>() ?? new MongoSettings();

        services.AddHangfire((sp, config) =>
        {
            var mongoClient = sp.GetRequiredService<IMongoClient>();
            config.UseSimpleAssemblyNameTypeSerializer()
                  .UseRecommendedSerializerSettings()
                  .UseMongoStorage(mongoClient, mongoSettings.DatabaseName, new MongoStorageOptions
                  {
                      Prefix = "hangfire.mongo",
                      CheckConnection = true,
                      MigrationOptions = new MongoMigrationOptions
                      {
                          MigrationStrategy = new MigrateMongoMigrationStrategy(),
                          BackupStrategy = new CollectionMongoBackupStrategy()
                      },
                      CheckQueuedJobsStrategy = CheckQueuedJobsStrategy.TailNotificationsCollection
                  });
        });

        if (includeHangfireServer)
        {
            services.AddHangfireServer(opts =>
            {
                opts.Queues = new[] { "default" };
                opts.WorkerCount = Environment.ProcessorCount * 2;
            });
        }

        services.AddTransient<BackgroundJobs.ChapterScrapingJob>();
        services.AddTransient<BackgroundJobs.MeiliSyncJob>();
        services.AddTransient<BackgroundJobs.DeleteMangaJob>();
        services.AddTransient<BackgroundJobs.LatestChapterScrapingJob>();

        return services;
    }

    private static IServiceCollection AddSecurityServices(this IServiceCollection services)
    {
        services.AddAuthentication(options =>
        {
            options.DefaultScheme = "CustomAuth";
            options.DefaultChallengeScheme = "CustomAuth";
        })
        .AddCookie(CookieAuthenticationDefaults.AuthenticationScheme, opts =>
        {
            opts.Cookie.Name = "MangaScrapper.Auth";
            opts.Cookie.HttpOnly = true;
            opts.Cookie.SameSite = Microsoft.AspNetCore.Http.SameSiteMode.Strict;
            opts.ExpireTimeSpan = TimeSpan.FromDays(30);
            opts.Events.OnRedirectToLogin = ctx =>
            {
                ctx.Response.StatusCode = 401;
                return Task.CompletedTask;
            };
            opts.Events.OnRedirectToAccessDenied = ctx =>
            {
                ctx.Response.StatusCode = 403;
                return Task.CompletedTask;
            };
        })
        .AddScheme<CustomAuthSchemeOptions, CustomAuthValidation>("CustomAuth", _ => { });

        services.AddRouting();
        services.AddAuthorization();

        services.AddScoped<IAuthTokenService, JwtAuthTokenService>();

        services.AddDataProtection()
            .SetApplicationName("MangaScrapper");

        return services;
    }
}
