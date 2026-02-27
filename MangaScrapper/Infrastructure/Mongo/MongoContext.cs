using MangaScrapper.Infrastructure.Mongo.Collections;
using Microsoft.Extensions.Options;
using MongoDB.Driver;

namespace MangaScrapper.Infrastructure.Mongo;

public class MongoContext
{
    public IMongoDatabase Database { get; }
    public IMongoCollection<MangaDocument> Mangas { get; }

    public MongoContext(IOptions<MongoSettings> settings)
    {
        var client = new MongoClient(settings.Value.ConnectionString);
        Database = client.GetDatabase(settings.Value.DatabaseName);
        Mangas = Database.GetCollection<MangaDocument>("mangas");
    }
}