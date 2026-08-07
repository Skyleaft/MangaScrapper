using Hangfire;
using MangaScrapper.Application.DependencyInjection;
using MangaScrapper.Infrastructure.DependencyInjection;
using MangaScrapper.Infrastructure.Security;
using Microsoft.Extensions.FileProviders;
using NovaStack.Infrastructure.DependencyInjection;
using NovaStack.Infrastructure.Logging;
using NovaStack.Infrastructure.Observability;
using NovaStack.Infrastructure.Persistence.Options;
using Scalar.AspNetCore;
using Serilog;

// ── Bootstrap logger (captures startup errors) ───────────────────────────────
LoggingExtensions.BootstrapLogger();

try
{
    Log.Information("Starting MangaScrapper.Api...");

    var builder = WebApplication.CreateBuilder(args);

    // ── Serilog ──────────────────────────────────────────────────────────────
    builder.Host.UseNovaStackSerilog();

    // ── OpenAPI / Swagger ────────────────────────────────────────────────────
    builder.Services.AddOpenApi();
    builder.Services.AddEndpointsApiExplorer();

    // ── Shared Infrastructure ────────────────────────────────────────────────
    builder.Services.AddNovaStackAuth(builder.Configuration);
    builder.Services.AddNovaStackCache(builder.Configuration);
    builder.Services.AddNovaStackHealthChecks(builder.Configuration);

    // ── OpenTelemetry ────────────────────────────────────────────────────────
    builder.Services.AddNovaStackObservability(
        "MangaScrapper.Api",
        otlpEndpoint: builder.Configuration["Observability:OtlpEndpoint"]);

    // ── Application Layer (MediatR, FluentValidation, Pipeline behaviors) ────
    builder.Services.AddMangaScrapperApplication();
    builder.Services.AddNovaStackMappings(typeof(ApplicationExtensions).Assembly);

    // ── Infrastructure Layer (EF Core, Repos, MassTransit) ──────────────────
    builder.Services.AddMangaScrapperInfrastructure(builder.Configuration);

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
        options.MessageTemplate =
            "HTTP {RequestMethod} {RequestPath} responded {StatusCode} in {Elapsed:0.0000}ms";
    });
    app.MapPrometheusScrapingEndpoint();

    if (app.Environment.IsDevelopment())
    {
        app.MapOpenApi(); // Access via /openapi/v1.json
        app.MapScalarApiReference();
    }

    app.UseHttpsRedirection();
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
