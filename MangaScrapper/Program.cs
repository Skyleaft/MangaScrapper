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
using MangaScrapper.Infrastructure.Mongo;
using MangaScrapper.Infrastructure.Mongo.Collections;
using MangaScrapper.Infrastructure.Repositories;
using MangaScrapper.Infrastructure.Services;
using MangaScrapper.Infrastructure.BackgroundJobs;
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

builder.Services.AddAuthenticationJwtBearer(s => s.SigningKey = builder.Configuration["JwtSigningKey"] ?? "a_very_secret_key_that_is_at_least_32_chars_long!!");
builder.Services.AddAuthentication(CookieAuthenticationDefaults.AuthenticationScheme)
    .AddCookie(options =>
    {
        options.ExpireTimeSpan = TimeSpan.FromDays(3);
        options.SlidingExpiration = true;
        options.AccessDeniedPath = "/Forbidden/";
        options.LoginPath = "/";
        options.LogoutPath = "/api/auth/logout";
        options.Cookie.SameSite = SameSiteMode.Lax;
        options.Cookie.SecurePolicy = CookieSecurePolicy.SameAsRequest;
    });
builder.Services.AddAuthorization();

builder.Services.AddFastEndpoints()
    .AddResponseCaching()
    .SwaggerDocument(o => o.AutoTagPathSegmentIndex = 2);

var keysFolder = Path.Combine(builder.Environment.ContentRootPath, "temp-keys");

builder.Services.AddDataProtection()
    .PersistKeysToFileSystem(new DirectoryInfo(keysFolder))
    .SetApplicationName("MangaScrapper");

// CORS configuration from appsettings.json (section: Cors)
var allowedOrigins = builder.Configuration.GetSection("Cors:AllowedOrigins").Get<string[]>() ?? Array.Empty<string>();
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
builder.Services.AddSingleton<MongoContext>();
builder.Services.AddSingleton(sp => 
{
    var settings = sp.GetRequiredService<IOptions<ScrapperSettings>>().Value;
    return new SemaphoreSlim(settings.MaxParallelDownloads);
});
builder.Services.AddScoped<IMangaRepository, MangaRepository>();



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

// Register ChapterScrapingJob for Hangfire
builder.Services.AddTransient<ChapterScrapingJob>();

builder.Services.AddHttpClient<ScrapperService>(c => c.Timeout = TimeSpan.FromMinutes(5));
builder.Services.AddHttpClient<KomikuService>(c => c.Timeout = TimeSpan.FromMinutes(5));
builder.Services.AddHttpClient<KiryuuService>(c => c.Timeout = TimeSpan.FromMinutes(5));

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
    .WithTracing(tracing => tracing
        .AddSource("MongoDB.Driver.Core.Extensions.DiagnosticSources")
        .AddAspNetCoreInstrumentation()
        .AddHttpClientInstrumentation()
        .AddOtlpExporter())
    .WithMetrics(metrics => metrics
        .AddAspNetCoreInstrumentation()
        .AddHttpClientInstrumentation()
        .AddRuntimeInstrumentation()
        .AddProcessInstrumentation()
        .AddOtlpExporter()
        .AddPrometheusExporter());

builder.Logging.AddOpenTelemetry(logging => 
{
    logging.IncludeFormattedMessage = true;
    logging.IncludeScopes = true;
    logging.AddOtlpExporter();
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
}

app.UseAuthentication()
   .UseAuthorization();

// Note: Hangfire Dashboard URL will be available if Hangfire.Dashboard is installed
app.MapHangfireDashboard().RequireAuthorization();


app.UseResponseCaching().UseFastEndpoints();

// Apply CORS policy
app.UseCors("ConfiguredCors");

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
