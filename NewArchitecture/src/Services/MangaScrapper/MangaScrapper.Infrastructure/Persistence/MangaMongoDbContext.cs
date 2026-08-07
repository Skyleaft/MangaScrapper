using MangaScrapper.Infrastructure.Persistence.Documents;
using MongoDB.Driver;
using NovaStack.Infrastructure.Persistence.MongoDb;

namespace MangaScrapper.Infrastructure.Persistence;

public class MangaMongoDbContext : MongoDbContextBase
{
    public MangaMongoDbContext(IMongoClient client, string databaseName)
        : base(client, databaseName)
    {
    }

    public IMongoCollection<MangaDocument> Mangas => GetCollection<MangaDocument>("Mangas");
    public IMongoCollection<UserDocument> Users => GetCollection<UserDocument>("Users");
    public IMongoCollection<UserLibraryDocument> UserLibraries => GetCollection<UserLibraryDocument>("UserLibraries");
    public IMongoCollection<UserProgressionDocument> UserProgressions => GetCollection<UserProgressionDocument>("UserProgressions");
}
