namespace MangaScrapper.Infrastructure.Configuration;

public class MongoSettings
{
    public string ConnectionString { get; set; } = "mongodb://localhost:27017";
    public string DatabaseName { get; set; } = "MangaScrapper";
}

public class ScrapperSettings
{
    public int MaxParallelDownloads { get; set; } = 5;
    public string ImageStoragePath { get; set; } = "images";
}

public class FlareSolverrSettings
{
    public bool Enabled { get; set; } = false;
    public string Host { get; set; } = "http://localhost:8191";
}

public class MeiliConfig
{
    public string Host { get; set; } = "http://localhost:7700";
    public string MasterKey { get; set; } = "mangas";
}

public class QdrantConfig
{
    public string Host { get; set; } = "http://localhost";
    public int Port { get; set; } = 6333;
    public string ApiKey { get; set; } = string.Empty;
}

public class EmbeddingConfig
{
    public string Host { get; set; } = string.Empty;
}

public class DiscordWebhookSettings
{
    public string WebhookUrl { get; set; } = string.Empty;
}

public class DomainSettings
{
    public string DomainUrl { get; set; } = string.Empty;
}
