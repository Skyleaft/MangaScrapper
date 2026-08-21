namespace MangaScrapper.Core.Services;

public interface IEmbeddingService
{
    Task<float[]?> GenerateEmbeddingAsync(string text, CancellationToken ct = default);
}
