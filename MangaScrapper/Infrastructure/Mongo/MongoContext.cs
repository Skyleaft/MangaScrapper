using MangaScrapper.Infrastructure.Mongo.Collections;
using Microsoft.Extensions.Options;
using MongoDB.Driver;
using MongoDB.Driver.Core.Extensions.DiagnosticSources;

namespace MangaScrapper.Infrastructure.Mongo;

public class MongoContext
{
    public IMongoDatabase Database { get; }
    public IMongoCollection<MangaDocument> Mangas { get; }
    public IMongoCollection<UserDocument> Users { get; }

    public MongoContext(IOptions<MongoSettings> settings)
    {
        var mongoClientSettings = MongoClientSettings.FromConnectionString(settings.Value.ConnectionString);
        mongoClientSettings.ClusterConfigurator = cb => cb.Subscribe(new DiagnosticsActivityEventSubscriber());
        var client = new MongoClient(mongoClientSettings);
        Database = client.GetDatabase(settings.Value.DatabaseName);
        Mangas = Database.GetCollection<MangaDocument>("mangas");
        Users = Database.GetCollection<UserDocument>("users");
    }
}