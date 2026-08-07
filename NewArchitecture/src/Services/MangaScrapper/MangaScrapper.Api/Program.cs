using Hangfire;
using MangaScrapper.Application.Common.Abstractions;
using MangaScrapper.Application.DependencyInjection;
using MangaScrapper.Infrastructure.DependencyInjection;
using MangaScrapper.Infrastructure.Security;
using Microsoft.Extensions.FileProviders;
using NovaStack.Infrastructure.Observability;

var builder = WebApplication.CreateBuilder(args);

// 1. Core Architecture Services
builder.Services.AddMangaScrapperApplication();
builder.Services.AddMangaScrapperInfrastructure(builder.Configuration);
builder.Services.AddNovaStackObservability("MangaScrapper.Api");

// 2. OpenAPI / Swagger Setup
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen(c =>
{
    c.SwaggerDoc("v1", new Microsoft.OpenApi.Models.OpenApiInfo
    {
        Title = "MangaScrapper API",
        Version = "v1",
        Description = "NovaStack Monolith API for MangaScrapper"
    });
});

// 3. CORS Policy Setup
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

var app = builder.Build();

// 4. HTTP Middleware Pipeline
if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI(c =>
    {
        c.SwaggerEndpoint("/swagger/v1/swagger.json", "MangaScrapper API v1");
    });
}

app.UseCors("DefaultCors");
app.UseAuthentication();
app.UseAuthorization();

// 5. Static File Serving (Local Image Storage)
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

// 6. Hangfire Dashboard
app.UseHangfireDashboard("/hangfire", new DashboardOptions
{
    Authorization = new[] { new HangfireAuthFilter() }
});

// 7. Minimal API Endpoint Registration
using (var scope = app.Services.CreateScope())
{
    var endpoints = scope.ServiceProvider.GetServices<IEndpointDefinition>();
    foreach (var endpoint in endpoints)
    {
        endpoint.DefineEndpoints(app);
    }
}

app.Run();
