using FastEndpoints;
using FastEndpoints.Swagger;
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

var builder = WebApplication.CreateBuilder(args);
builder.Services.AddFastEndpoints()
    .AddResponseCaching()
    .SwaggerDocument(o => o.AutoTagPathSegmentIndex = 2);

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

builder.Services.AddSingleton<IBackgroundTaskQueue>(_ => new BackgroundTaskQueue(100));
builder.Services.AddHostedService<BackgroundWorker>();

builder.Services.AddHttpClient<ScrapperService>(c => c.Timeout = TimeSpan.FromMinutes(5));
builder.Services.AddHttpClient<KomikuService>(c => c.Timeout = TimeSpan.FromMinutes(5));
builder.Services.AddHttpClient<KiryuuService>(c => c.Timeout = TimeSpan.FromMinutes(5));

builder.Services.AddOpenTelemetry()
    .WithTracing(tracing => tracing
        .AddSource(Telemetry.ServiceName)
        .AddSource("MongoDB.Driver.Core.Extensions.DiagnosticSources")
        .AddAspNetCoreInstrumentation()
        .AddHttpClientInstrumentation()
        .AddOtlpExporter()
        .AddConsoleExporter())
    .WithMetrics(metrics => metrics
        .AddMeter(Telemetry.ServiceName)
        .AddAspNetCoreInstrumentation()
        .AddHttpClientInstrumentation()
        .AddRuntimeInstrumentation()
        .AddOtlpExporter()
        .AddConsoleExporter()
        .AddPrometheusExporter());

builder.Logging.AddOpenTelemetry(logging => 
{
    logging.IncludeFormattedMessage = true;
    logging.IncludeScopes = true;
    logging.AddOtlpExporter();
    logging.AddConsoleExporter();
});

builder.Services.ConfigureOpenTelemetryTracerProvider((sp, builder) =>
    builder.SetResourceBuilder(ResourceBuilder.CreateDefault().AddService(Telemetry.ServiceName)));
builder.Services.ConfigureOpenTelemetryMeterProvider((sp, builder) =>
    builder.SetResourceBuilder(ResourceBuilder.CreateDefault().AddService(Telemetry.ServiceName)));


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

app.UseResponseCaching().UseFastEndpoints();

// Configure the HTTP request pipeline.
// if (app.Environment.IsDevelopment())
// {
    
// }
app.MapOpenApi();
app.UseSwaggerGen();

app.UseHttpsRedirection();

app.UseStaticFiles(new StaticFileOptions
{
    FileProvider = new PhysicalFileProvider(
        Path.IsPathRooted(builder.Configuration["ScrapperSettings:ImageStoragePath"]) 
            ? builder.Configuration["ScrapperSettings:ImageStoragePath"]! 
            : Path.Combine(builder.Environment.ContentRootPath, builder.Configuration["ScrapperSettings:ImageStoragePath"] ?? "images")),
    RequestPath = "/images"
});

app.Run();
