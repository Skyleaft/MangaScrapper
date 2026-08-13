using Hangfire;
using MangaScrapper.Api.Components;
using MangaScrapper.Core.DependencyInjection;
using MangaScrapper.Core.Security;
using Microsoft.Extensions.FileProviders;
using NovaStack.Infrastructure.DependencyInjection;
using NovaStack.Infrastructure.Logging;
using NovaStack.Infrastructure.Observability;
using NovaStack.Infrastructure.Persistence.Options;
using Scalar.AspNetCore;
using Serilog;
using Serilog.Events;

// ── Bootstrap logger (captures startup errors) ───────────────────────────────
LoggingExtensions.BootstrapLogger();

try
{
    var asciiArt = @"
    __  ___                        _____                                              ______            _           
   /  |/  /___ _____  ____ _____ _/ ___/______________ _____  ____  ___  _____       / ____/___  ____ _(_)___  ___ 
  / /|_/ / __ `/ __ \/ __ `/ __ `/\__ \/ ___/ ___/ __ `/ __ \/ __ \/ _ \/ ___/      / __/ / __ \/ __ `/ / __ \/ _ \
 / /  / / /_/ / / / / /_/ / /_/ /___/ / /__/ /  / /_/ / /_/ / /_/ /  __/ /         / /___/ / / / /_/ / / / / /  __/
/_/  /_/\__,_/_/ /_/\__, /\__,_//____/\___/_/   \__,_/ .___/ .___/\___/_/         /_____/_/ /_/\__, /_/_/ /_/\___/ 
                   /____/                           /_/   /_/                                 /____/               
";
    Console.WriteLine(asciiArt);
    Log.Information("Starting MangaScrapper.Api Engine...");

    var builder = WebApplication.CreateBuilder(args);

    // ── Serilog ──────────────────────────────────────────────────────────────
    builder.Host.UseNovaStackSerilog();

    // ── OpenAPI / Swagger ────────────────────────────────────────────────────
    builder.Services.AddOpenApi();
    builder.Services.AddEndpointsApiExplorer();
    
    // ── Razor Component ──────────────────────────────────────────────────────
    builder.Services.AddRazorComponents()
        .AddInteractiveWebAssemblyComponents();

    // ── Shared Infrastructure ────────────────────────────────────────────────
    builder.Services.AddNovaStackAuth(builder.Configuration);
    builder.Services.AddNovaStackCache(builder.Configuration);
    builder.Services.AddNovaStackHealthChecks(builder.Configuration);

    // ── OpenTelemetry ────────────────────────────────────────────────────────
    builder.Services.AddNovaStackObservability(
        "MangaScrapper.Api",
        otlpEndpoint: builder.Configuration["Observability:OtlpEndpoint"]);

    // ── Core VSA Layer (CQRS, Scrapers, Repositories, Background Jobs, Messaging) ──
    builder.Services.AddMangaScrapperCore(builder.Configuration);
    builder.Services.AddNovaStackMappings(typeof(CoreExtensions).Assembly);

    // ── CORS ─────────────────────────────────────────────────────────────────
    var allowedOrigins = builder.Configuration.GetSection("Cors:AllowedOrigins").Get<string[]>()
        ?? new[] { "http://localhost:3000", "http://localhost:5000" };

    builder.Services.AddCors(options =>
    {
        options.AddPolicy("DefaultCors", policy =>
        {
            policy.WithOrigins(allowedOrigins)
                  .AllowAnyHeader()
                  .AllowAnyMethod()
                  .AllowCredentials();
        });
    });

    // ── Problem Details ──────────────────────────────────────────────────────
    builder.Services.AddProblemDetails();

    var app = builder.Build();

    // ── Middleware ────────────────────────────────────────────────────────────
    app.UseExceptionHandler();
    app.UseSerilogRequestLogging(options =>
    {
        options.GetLevel = (httpContext, elapsed, ex) =>
        {
            var path = httpContext.Request.Path.Value;

            if (string.IsNullOrEmpty(path))
                return LogEventLevel.Information;

            if (path.StartsWith("/images", StringComparison.OrdinalIgnoreCase) ||
                path.StartsWith("/hangfire", StringComparison.OrdinalIgnoreCase) ||
                path.StartsWith("/api/v1/images", StringComparison.OrdinalIgnoreCase) ||
                path.StartsWith("/scalar", StringComparison.OrdinalIgnoreCase))
            {
                return LogEventLevel.Verbose; 
            }

            if (path.EndsWith(".js", StringComparison.OrdinalIgnoreCase) ||
                path.EndsWith(".css", StringComparison.OrdinalIgnoreCase) ||
                path.EndsWith(".pdb", StringComparison.OrdinalIgnoreCase) ||
                path.EndsWith(".wasm", StringComparison.OrdinalIgnoreCase))
            {
                return LogEventLevel.Verbose;
            }

            if (ex != null || httpContext.Response.StatusCode >= 500)
                return LogEventLevel.Error;

            return LogEventLevel.Information;
        };
        options.MessageTemplate =
            "HTTP {RequestMethod} {RequestPath} responded {StatusCode} in {Elapsed:0.0000}ms";
    });
    app.MapPrometheusScrapingEndpoint();

    if (app.Environment.IsDevelopment())
    {
        app.UseWebAssemblyDebugging();
        app.MapOpenApi(); // Access via /openapi/v1.json
        app.MapScalarApiReference();
    }

    app.UseHttpsRedirection();
    app.UseStaticFiles();
    app.UseCors("DefaultCors");
    app.UseAuthentication();
    app.UseAuthorization();

    // ── Static File Serving (Local Image Storage) ────────────────────────────
    var imageStoragePath = builder.Configuration["Scrapper:ImageStoragePath"] ?? "images";
    var absoluteImagePath = Path.IsPathRooted(imageStoragePath)
        ? imageStoragePath
        : Path.Combine(builder.Environment.ContentRootPath, imageStoragePath);

    if (!Directory.Exists(absoluteImagePath))
    {
        Directory.CreateDirectory(absoluteImagePath);
    }

    app.UseStaticFiles(new StaticFileOptions
    {
        FileProvider = new PhysicalFileProvider(absoluteImagePath),
        RequestPath = "/images"
    });

    // ── Hangfire Dashboard ───────────────────────────────────────────────────
    app.UseHangfireDashboard("/hangfire", new DashboardOptions
    {
        Authorization = new[] { new HangfireAuthFilter() }
    });

    // ── Endpoints ─────────────────────────────────────────────────────────────
    // Scan MangaScrapper.Application assembly for all IEndpointDefinition implementations
    app.MapMangaScrapperEndpoints();

    // Health checks
    app.MapHealthChecks("/health");

    // Blazor WASM fallback route
    app.MapFallbackToFile("index.html");
    app.UseAntiforgery();
    app.MapStaticAssets();
    
    app.MapRazorComponents<App>()
        .AddInteractiveWebAssemblyRenderMode()
        .AddAdditionalAssemblies(typeof(MangaPanel.Client._Imports).Assembly);

    // ── Auto-migrate ─────────────────────────────────────────────────────────
    // MongoDB is schemaless — no EF migrations to run.
    var dbProvider = app.Configuration
        .GetSection(DatabaseOptions.SectionName)
        .GetValue<DatabaseProvider>(nameof(DatabaseOptions.Provider));

    if (dbProvider == DatabaseProvider.MongoDB)
        Log.Information("Database provider is MongoDB — skipping EF Core migration.");

    app.Lifetime.ApplicationStarted.Register(() =>
    {
        var urls = string.Join(", ", app.Urls);
        Log.Information("Application is running on: {Urls}", urls);
        if (app.Environment.IsDevelopment())
        {
            var firstUrl = app.Urls.FirstOrDefault() ?? "http://localhost:5191";
            Log.Information("Scalar API reference available at: {Url}/scalar/v1", firstUrl.TrimEnd('/'));
        }
    });

    app.Run();
}
catch (Exception ex) when (ex is not HostAbortedException)
{
    Log.Fatal(ex, "MangaScrapper.Api terminated unexpectedly.");
}
finally
{
    Log.CloseAndFlush();
}

// Required for WebApplicationFactory in integration tests
namespace MangaScrapper.Api
{
    public class Program
    {
    }
}

