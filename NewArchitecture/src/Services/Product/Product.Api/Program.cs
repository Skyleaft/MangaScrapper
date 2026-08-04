using NovaStack.Infrastructure.DependencyInjection;
using NovaStack.Infrastructure.Logging;
using NovaStack.Infrastructure.Observability;
using NovaStack.Infrastructure.Persistence.Options;
using Product.Application.DependencyInjection;
using Product.Infrastructure.DependencyInjection;
using Scalar.AspNetCore;
using Serilog;

// ── Bootstrap logger (captures startup errors) ───────────────────────────────
LoggingExtensions.BootstrapLogger();

try
{
    Log.Information("Starting Product.Api...");

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
        "Product.Api",
        otlpEndpoint: builder.Configuration["Observability:OtlpEndpoint"]);

    // ── Application Layer (MediatR, FluentValidation, Pipeline behaviors) ────
    builder.Services.AddProductApplication();
    builder.Services.AddNovaStackMappings(typeof(ApplicationExtensions).Assembly);

    // ── Infrastructure Layer (EF Core, Repos, MassTransit) ──────────────────
    builder.Services.AddProductInfrastructure(builder.Configuration);

    // ── CORS ─────────────────────────────────────────────────────────────────
    builder.Services.AddCors(options =>
    {
        options.AddDefaultPolicy(policy =>
        {
            policy.AllowAnyOrigin()
                .AllowAnyHeader()
                .AllowAnyMethod();
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
    app.UseCors();
    app.UseAuthentication();
    app.UseAuthorization();

    // ── Endpoints ─────────────────────────────────────────────────────────────
    // Scan Product.Application assembly for all IEndpointDefinition implementations
    app.MapProductEndpoints();

    // Health checks
    app.MapHealthChecks("/health");

    // ── Auto-migrate ─────────────────────────────────────────────────────────
    // MongoDB is schemaless — no EF migrations to run.
    var dbProvider = app.Configuration
        .GetSection(DatabaseOptions.SectionName)
        .GetValue<DatabaseProvider>(nameof(DatabaseOptions.Provider));

    if (dbProvider == DatabaseProvider.MongoDB)
        Log.Information("Database provider is MongoDB — skipping EF Core migration.");
    else
        await app.Services.MigrateProductDatabaseAsync();

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
    Log.Fatal(ex, "Product.Api terminated unexpectedly.");
}
finally
{
    Log.CloseAndFlush();
}

// Required for WebApplicationFactory in integration tests
namespace Product.Api
{
    public class Program
    {
    }
}