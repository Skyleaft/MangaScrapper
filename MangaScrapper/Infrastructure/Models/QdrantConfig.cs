namespace MangaScrapper.Infrastructure.Models;

public class QdrantConfig
{
    public string Host { get; set; } = "http://localhost";
    public int Port { get; set; } = 6333;
    public string ApiKey { get; set; } = "";
}
