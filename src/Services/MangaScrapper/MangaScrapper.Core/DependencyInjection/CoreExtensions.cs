using FluentValidation;
using Hangfire;
using Hangfire.Mongo;
using Hangfire.Mongo.Migration.Strategies;
using Hangfire.Mongo.Migration.Strategies.Backup;
using MangaScrapper.Core.BackgroundJobs;
using MangaScrapper.Core.Common.Abstractions;
using MangaScrapper.Core.Common.Behaviors;
using MangaScrapper.Core.Configuration;
using MangaScrapper.Core.Messaging;
using MangaScrapper.Core.Persistence;
using MangaScrapper.Core.Repositories;
using MangaScrapper.Core.Scrapers;
using MangaScrapper.Core.Scrapers.Kiryuu;
using MangaScrapper.Core.Scrapers.Komikcast;
using MangaScrapper.Core.Scrapers.Komiku;
using MangaScrapper.Core.Scrapers.MangaDex;
using MangaScrapper.Core.Security;
using MangaScrapper.Core.Services;
using MediatR;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using MongoDB.Bson;
using MongoDB.Bson.Serialization;
using MongoDB.Bson.Serialization.Conventions;
using MongoDB.Bson.Serialization.Serializers;
using MongoDB.Driver;
using NovaStack.Contracts.IntegrationEvents;
using NovaStack.Infrastructure.DependencyInjection;
using NovaStack.Infrastructure.Messaging.Options;
using NovaStack.Infrastructure.Persistence.MongoDb;

namespace MangaScrapper.Core.DependencyInjection;

public static class CoreExtensions
{
    public static IServiceCollection AddMangaScrapperCore(
        this IServiceCollection services,
        IConfiguration configuration,
        bool includeHangfireServer = false,
        bool includeRabbitMqConsumer = false)
    {
        var assembly = typeof(CoreExtensions).Assembly;

        // MediatR & CQRS Behaviors
        services.AddMediatR(cfg =>
        {
            cfg.RegisterServicesFromAssembly(assembly);
            cfg.AddBehavior(typeof(IPipelineBehavior<,>), typeof(LoggingBehavior<,>));
            cfg.AddBehavior(typeof(IPipelineBehavior<,>), typeof(ValidationBehavior<,>));
        });

        // FluentValidation
        services.AddValidatorsFromAssembly(assembly);

        // Core Subsystems
        services
            .AddMongoDb(configuration)
            .AddRepositories()
            .AddExternalServices(configuration)
            .AddScraperServices(configuration)
            .AddHangfireWithMongo(configuration, includeHangfireServer)
            .AddRabbitMqMessaging(configuration, includeRabbitMqConsumer)
            .AddSecurityServices()
            .AddFirebaseApp(configuration);

        return services;
    }

    public static WebApplication MapMangaScrapperEndpoints(this WebApplication app)
    {
        var endpointDefinitions = typeof(CoreExtensions).Assembly
            .GetTypes()
            .Where(t => typeof(IEndpointDefinition).IsAssignableFrom(t)
                        && t is { IsInterface: false, IsAbstract: false })
            .Select(Activator.CreateInstance)
            .Cast<IEndpointDefinition>();

        foreach (var definition in endpointDefinitions)
            definition.DefineEndpoints(app);

        return app;
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

        services.AddScoped<IMangaExternalRepository, MangaExternalService>();
        services.AddScoped<IMangaMessagePublisher, MangaMessagePublisher>();

        services.AddHttpClient<DiscordWebhookService>();

        services.AddScoped<IScrapperSettingsProvider, ScrapperSettingsProvider>();
        services.AddScoped<IRecurringJobsService, RecurringJobsService>();

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

        services.AddKeyedScoped<IProviderScrapperService, KomikuService>("komiku");
        services.AddKeyedScoped<IProviderScrapperService, KiryuuService>("kiryuu");
        services.AddKeyedScoped<IProviderScrapperService, KomikcastService>("komikcast");
        services.AddKeyedScoped<IProviderScrapperService, MangaDexService>("mangadex");

        services.AddScoped<IScrapperQueueService, ScrapperQueueService>();

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

        services.AddTransient<MeiliSyncJob>();
        services.AddTransient<DeleteMangaJob>();
        services.AddTransient<LatestChapterScrapingJob>();

        return services;
    }

    private static IServiceCollection AddRabbitMqMessaging(
        this IServiceCollection services,
        IConfiguration configuration,
        bool includeConsumer)
    {
        services.Configure<MessagingOptions>(configuration.GetSection(MessagingOptions.SectionName));

        services.AddNativeRabbitMqEventBus();

        services.AddScoped<ScrapChapterPagesHandler>();
        services.AddScoped<DeleteMangaHandler>();
        services.AddScoped<DeleteChapterHandler>();

        if (includeConsumer)
        {
            services.AddRabbitMqConsumer<ScrapChapterPagesIntegrationEvent, ScrapChapterPagesHandler>(
                "scrape-chapter-pages");
                
            services.AddRabbitMqConsumer<DeleteMangaIntegrationEvent, DeleteMangaHandler>(
                "delete-manga");
                
            services.AddRabbitMqConsumer<DeleteChapterIntegrationEvent, DeleteChapterHandler>(
                "delete-chapter");
        }

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

    private static IServiceCollection AddFirebaseApp(this IServiceCollection services, IConfiguration configuration)
    {
        services.Configure<FirebaseSettings>(configuration.GetSection("Firebase"));

        try
        {
            var credentialPath = configuration["Firebase:CredentialPath"];
            if (!string.IsNullOrEmpty(credentialPath) && File.Exists(credentialPath))
            {
                Environment.SetEnvironmentVariable("GOOGLE_APPLICATION_CREDENTIALS", credentialPath);
                if (FirebaseAdmin.FirebaseApp.DefaultInstance is null)
                {
                    FirebaseAdmin.FirebaseApp.Create();
                    Console.WriteLine($"FirebaseApp initialized with credentials from: {credentialPath}");
                }
            }
            else
            {
                string? fallbackPath = null;
                if (!string.IsNullOrEmpty(credentialPath))
                {
                    var directory = Path.GetDirectoryName(credentialPath);
                    if (!string.IsNullOrEmpty(directory) && Directory.Exists(directory))
                    {
                        fallbackPath = Directory.GetFiles(directory, "*.json").FirstOrDefault();
                    }
                }

                if (!string.IsNullOrEmpty(fallbackPath) && File.Exists(fallbackPath))
                {
                    Environment.SetEnvironmentVariable("GOOGLE_APPLICATION_CREDENTIALS", fallbackPath);
                    if (FirebaseAdmin.FirebaseApp.DefaultInstance is null)
                    {
                        FirebaseAdmin.FirebaseApp.Create();
                        Console.WriteLine($"FirebaseApp initialized with fallback credentials from: {fallbackPath}");
                    }
                }
                else
                {
                    var hasGcpDefault = !string.IsNullOrEmpty(Environment.GetEnvironmentVariable("GOOGLE_APPLICATION_CREDENTIALS")) ||
                                        !string.IsNullOrEmpty(Environment.GetEnvironmentVariable("GAE_INSTANCE")) ||
                                        !string.IsNullOrEmpty(Environment.GetEnvironmentVariable("K_SERVICE"));

                    if (hasGcpDefault)
                    {
                        if (FirebaseAdmin.FirebaseApp.DefaultInstance is null)
                        {
                            FirebaseAdmin.FirebaseApp.Create();
                            Console.WriteLine("FirebaseApp initialized with GCP default credentials.");
                        }
                    }
                    else
                    {
                        Console.WriteLine("FirebaseApp was NOT initialized: No credentials file found and not running on GCP.");
                    }
                }
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"FirebaseApp initialization failed: {ex.Message}");
        }

        return services;
    }
}

