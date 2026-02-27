namespace MangaScrapper.Infrastructure.Mongo;

public class MongoSettings
{
    public string ConnectionString { get; set; } = default!;
    public string DatabaseName { get; set; } = default!;
    public string CollectionName { get; set; } = default!;
    public int CacheExpirationMinutes { get; set; } = 5;
    public bool UseCache => CacheExpirationMinutes > 0;
    
}