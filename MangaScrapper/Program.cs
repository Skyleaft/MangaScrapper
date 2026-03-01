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

var builder = WebApplication.CreateBuilder(args);
builder.Services.AddFastEndpoints()
    .SwaggerDocument();

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

builder.Services.AddHttpClient<KomikuService>();
builder.Services.AddHttpClient<KiryuuService>();


// Add services to the container.
// Learn more about configuring OpenAPI at https://aka.ms/aspnet/openapi
builder.Services.AddOpenApi();

var app = builder.Build();

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

app.UseFastEndpoints();

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
