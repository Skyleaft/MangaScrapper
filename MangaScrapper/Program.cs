using System.Net;
using Hangfire;
using Hangfire.Mongo;
using Hangfire.Mongo.Migration.Strategies;
using Hangfire.Mongo.Migration.Strategies.Backup;
using FastEndpoints;
using FastEndpoints.Security;
using FastEndpoints.Swagger;
using MangaScrapper.Components;
using MangaScrapper.Features.ScrapperKomiku.Services;
using MangaScrapper.Features.ScrapperKiryuu;
using MangaScrapper.Features.ScrapperKiryuu.Services;
using MangaScrapper.Features.UserLibrary.Services;
using MangaScrapper.Features.UserProgression.Services;
using FirebaseAdmin;
using Google.Apis.Auth.OAuth2;
using MangaScrapper.Features.ScrapperKomikcast.Services;
using MangaScrapper.Infrastructure.Mongo;
using MangaScrapper.Infrastructure.Mongo.Collections;
using MangaScrapper.Infrastructure.Repositories;
using MangaScrapper.Infrastructure.Security;
using MangaScrapper.Infrastructure.Services;
using MangaScrapper.Infrastructure.BackgroundJobs;
using MangaScrapper.Infrastructure.Models;
using MongoDB.Bson;
using MongoDB.Bson.Serialization;
using MongoDB.Bson.Serialization.Conventions;
using MongoDB.Bson.Serialization.Serializers;
using MongoDB.Driver;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Options;
using OpenTelemetry.Resources;
using OpenTelemetry.Trace;
using OpenTelemetry.Metrics;
using OpenTelemetry.Logs;
using MangaScrapper.Infrastructure.Utils;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.DataProtection;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddRazorComponents()
    .AddInteractiveWebAssemblyComponents();

builder.Services.AddAuthentication(o =>
    {
        o.DefaultScheme = "CustomAuth";
        o.DefaultAuthenticateScheme = "CustomAuth";
        o.DefaultChallengeScheme = "CustomAuth";
    })
    .AddCookie(CookieAuthenticationDefaults.AuthenticationScheme, options =>
    {
        options.ExpireTimeSpan = TimeSpan.FromDays(3);
        options.SlidingExpiration = true;
        options.AccessDeniedPath = "/Forbidden/";
        options.LoginPath = "/";
        options.LogoutPath = "/api/auth/logout";
        options.Cookie.SameSite = SameSiteMode.Lax;
        options.Cookie.SecurePolicy = CookieSecurePolicy.SameAsRequest;
    })
    .AddScheme<CustomAuthSchemeOptions, CustomAuthValidation>("CustomAuth", null);
builder.Services.AddAuthorization();

builder.Services.AddFastEndpoints()
    .AddResponseCaching()
    .SwaggerDocument(o => o.AutoTagPathSegmentIndex = 2);

try
{
    var credentialPath = builder.Configuration["Firebase:CredentialPath"];
    if (!string.IsNullOrEmpty(credentialPath) && File.Exists(credentialPath))
    {
        Environment.SetEnvironmentVariable("GOOGLE_APPLICATION_CREDENTIALS", credentialPath);
        FirebaseApp.Create();
        Console.WriteLine($"FirebaseApp initialized with credentials from: {credentialPath}");
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
            FirebaseApp.Create();
            Console.WriteLine($"FirebaseApp initialized with fallback credentials from: {fallbackPath}");
        }
        else
        {
            // Do not call FirebaseApp.Create() blindly if running locally without GCP credentials,
            // as it will block/hang trying to retrieve credentials from the GCE metadata server (169.254.169.254).
            var hasGcpDefault = !string.IsNullOrEmpty(Environment.GetEnvironmentVariable("GOOGLE_APPLICATION_CREDENTIALS")) ||
                                !string.IsNullOrEmpty(Environment.GetEnvironmentVariable("GAE_INSTANCE")) ||
                                !string.IsNullOrEmpty(Environment.GetEnvironmentVariable("K_SERVICE"));

            if (hasGcpDefault)
            {
                FirebaseApp.Create();
                Console.WriteLine("FirebaseApp initialized with GCP default credentials.");
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

var keysFolder = Path.Combine(builder.Environment.ContentRootPath, "temp-keys");

builder.Services.AddDataProtection()
    .PersistKeysToFileSystem(new DirectoryInfo(keysFolder))
    .SetApplicationName("MangaScrapper");

// CORS configuration from appsettings.json or environment variables (section: Cors)
var allowedOrigins = builder.Configuration.GetSection("Cors:AllowedOrigins").Get<string[]>()
                     ?? builder.Configuration.GetValue<string>("Cors:AllowedOrigins")?.Split(',', StringSplitOptions.RemoveEmptyEntries)
                     ?? Array.Empty<string>();
var allowCredentials = builder.Configuration.GetValue<bool?>("Cors:AllowCredentials") ?? false;

builder.Services.AddCors(options =>
{
    options.AddPolicy("ConfiguredCors", policy =>
    {
        if (allowedOrigins.Length == 0 || allowedOrigins.Contains("*"))
        {
            // Allow any origin when none specified or wildcard provided
            policy.AllowAnyOrigin()
                  .AllowAnyHeader()
                  .AllowAnyMethod();
            // Note: AllowCredentials cannot be used with AllowAnyOrigin
        }
        else
        {
            policy.WithOrigins(allowedOrigins)
                  .AllowAnyHeader()
                  .AllowAnyMethod();

            if (allowCredentials)
            {
                policy.AllowCredentials();
            }
        }
    });
});

var conventionPack = new ConventionPack { new CamelCaseElementNameConvention() };
ConventionRegistry.Register("camelCase", conventionPack, t => true);

BsonSerializer.RegisterSerializer(new GuidSerializer(GuidRepresentation.Standard));

builder.Services.Configure<MongoSettings>(builder.Configuration.GetSection("MongoSettings"));
builder.Services.Configure<ScrapperSettings>(builder.Configuration.GetSection("ScrapperSettings"));
builder.Services.Configure<MeiliConfig>(builder.Configuration.GetSection("MeiliSettings"));
builder.Services.Configure<QdrantConfig>(builder.Configuration.GetSection("QdrantSettings"));
builder.Services.Configure<EmbeddingConfig>(builder.Configuration.GetSection("EmbeddingSettings"));
builder.Services.AddSingleton<MongoContext>();
builder.Services.AddSingleton(sp =>
{
    var settings = sp.GetRequiredService<IOptions<ScrapperSettings>>().Value;
    return new SemaphoreSlim(settings.MaxParallelDownloads);
});
builder.Services.AddScoped<IMangaRepository, MangaRepository>();
builder.Services.AddScoped<IUserLibraryRepository, UserLibraryRepository>();
builder.Services.AddScoped<IUserProgressionRepository, UserProgressionRepository>();
builder.Services.AddScoped<IUserLibraryService, UserLibraryService>();
builder.Services.AddScoped<IUserProgressionService, UserProgressionService>();


// Configure Hangfire with MongoDB
var mongoSettings = builder.Configuration.GetSection("MongoSettings").Get<MongoSettings>();
var mongoStorageOptions = new MongoStorageOptions
{
    Prefix = "hangfire.mongo",
    CheckConnection = true,
    MigrationOptions = new MongoMigrationOptions
    {
        MigrationStrategy = new MigrateMongoMigrationStrategy(),
        BackupStrategy = new CollectionMongoBackupStrategy()
    }
};

// Add Hangfire server

builder.Services.AddHangfire(configuration => configuration
    .SetDataCompatibilityLevel(CompatibilityLevel.Version_180)
    .UseSimpleAssemblyNameTypeSerializer()
    .UseRecommendedSerializerSettings()
    .UseMongoStorage(mongoSettings!.ConnectionString, mongoSettings.DatabaseName, mongoStorageOptions));

builder.Services.AddHangfireServer();

builder.Services.AddHttpContextAccessor();

// Register ChapterScrapingJob for Hangfire
builder.Services.AddTransient<ChapterScrapingJob>();
builder.Services.AddTransient<MeiliSyncJob>();
builder.Services.AddScoped<MeilisearchService>();
builder.Services.AddScoped<QdrantService>();
builder.Services.AddScoped<StorageSyncService>();

//setting httpclient
builder.Services.AddHttpClient<ScrapperService>(HttpConfig.ConfigureClient)
    .ConfigurePrimaryHttpMessageHandler(HttpConfig.CreateHandler);

builder.Services.AddHttpClient<KomikuService>(HttpConfig.ConfigureClient)
    .ConfigurePrimaryHttpMessageHandler(HttpConfig.CreateHandler);

builder.Services.AddHttpClient<KiryuuService>(HttpConfig.ConfigureClient)
    .ConfigurePrimaryHttpMessageHandler(HttpConfig.CreateHandler);

builder.Services.AddHttpClient<KomikcastService>(HttpConfig.ConfigureClient)
    .ConfigurePrimaryHttpMessageHandler(HttpConfig.CreateHandler);

// Named client for the image proxy endpoint – spoofs a browser User-Agent
// so external providers (Komiku, etc.) don't reject the server-side request.
builder.Services.AddHttpClient("ImageProxy", client =>
{
    client.DefaultRequestHeaders.Add("User-Agent",
        "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/124.0.0.0 Safari/537.36");
    client.DefaultRequestHeaders.Add("Accept", "image/avif,image/webp,image/apng,image/*,*/*;q=0.8");
    client.Timeout = TimeSpan.FromSeconds(15);
}).ConfigurePrimaryHttpMessageHandler(() => new HttpClientHandler
{
    AllowAutoRedirect = true,
    MaxAutomaticRedirections = 5,
});


var otelEndpoint = builder.Configuration["OTEL_EXPORTER_OTLP_ENDPOINT"] ?? Environment.GetEnvironmentVariable("OTEL_EXPORTER_OTLP_ENDPOINT");
var enableOtlp = !string.IsNullOrEmpty(otelEndpoint);

builder.Services.AddOpenTelemetry()
    .ConfigureResource(resource => resource.AddService(
            serviceName: "MangaScrapper",
            serviceVersion: System.Reflection.Assembly.GetEntryAssembly()?.GetName().Version?.ToString(3) // SemVer
        )
        .AddAttributes(new Dictionary<string, object>
        {
            { "host.name", Environment.MachineName }
        })
    )
    .WithTracing(tracing =>
    {
        tracing
            .AddSource("MongoDB.Driver.Core.Extensions.DiagnosticSources")
            .AddAspNetCoreInstrumentation()
            .AddHttpClientInstrumentation();

        if (enableOtlp)
        {
            tracing.AddOtlpExporter();
        }
    })
    .WithMetrics(metrics =>
    {
        metrics
            .AddAspNetCoreInstrumentation()
            .AddHttpClientInstrumentation()
            .AddRuntimeInstrumentation()
            .AddProcessInstrumentation()
            .AddPrometheusExporter();

        if (enableOtlp)
        {
            metrics.AddOtlpExporter();
        }
    });

builder.Logging.AddOpenTelemetry(logging =>
{
    logging.IncludeFormattedMessage = true;
    logging.IncludeScopes = true;

    if (enableOtlp)
    {
        logging.AddOtlpExporter();
    }
});


// Add services to the container.
// Learn more about configuring OpenAPI at https://aka.ms/aspnet/openapi
builder.Services.AddOpenApi();

var app = builder.Build();

app.UseOpenTelemetryPrometheusScrapingEndpoint();

using (var scope = app.Services.CreateScope())
{
    var mongoContext = scope.ServiceProvider.GetRequiredService<MongoContext>();
    await mongoContext.Mangas.Indexes.CreateOneAsync(
        new CreateIndexModel<MangaDocument>(
            Builders<MangaDocument>.IndexKeys.Ascending(m => m.Title),
            new CreateIndexOptions { Unique = true }
        )
    );

    await mongoContext.UserLibraries.Indexes.CreateOneAsync(
        new CreateIndexModel<UserLibraryDocument>(
            Builders<UserLibraryDocument>.IndexKeys.Ascending(ul => ul.UserId).Ascending(ul => ul.MangaId),
            new CreateIndexOptions { Unique = true }
        )
    );

    await mongoContext.UserProgressions.Indexes.CreateOneAsync(
        new CreateIndexModel<UserProgressionDocument>(
            Builders<UserProgressionDocument>.IndexKeys.Ascending(up => up.UserId).Ascending(up => up.MangaId).Ascending(up => up.ChapterId),
            new CreateIndexOptions { Unique = true }
        )
    );
}

// Apply CORS policy before Authentication/Authorization and Endpoints
app.UseCors("ConfiguredCors");

app.UseAuthentication()
   .UseAuthorization();

// Note: Hangfire Dashboard URL will be available if Hangfire.Dashboard is installed
app.MapHangfireDashboard("/hangfire", new DashboardOptions()
{
    Authorization = new[] { new HangfireAuthFillter() }
}).RequireAuthorization();


app.UseResponseCaching().UseFastEndpoints();

// Configure the HTTP request pipeline.
if (app.Environment.IsDevelopment())
{
    app.UseWebAssemblyDebugging();
}
else
{
    app.UseExceptionHandler("/Error", createScopeForErrors: true);
    // The default HSTS value is 30 days. You may want to change this for production scenarios, see https://aka.ms/aspnetcore-hsts.
    app.UseHsts();
}

app.UseStatusCodePagesWithReExecute("/not-found", createScopeForStatusCodePages: true);

app.MapOpenApi();
app.UseSwaggerGen();

app.UseAntiforgery();

app.MapStaticAssets();

app.UseStaticFiles(new StaticFileOptions
{
    FileProvider = new PhysicalFileProvider(
        Path.IsPathRooted(builder.Configuration["ScrapperSettings:ImageStoragePath"])
            ? builder.Configuration["ScrapperSettings:ImageStoragePath"]!
            : Path.Combine(builder.Environment.ContentRootPath, builder.Configuration["ScrapperSettings:ImageStoragePath"] ?? "images")),
    RequestPath = "/images"
});

app.MapRazorComponents<App>()
    .AddInteractiveWebAssemblyRenderMode()
    .AddAdditionalAssemblies(typeof(MangaPanel.Client._Imports).Assembly);

app.Run();
