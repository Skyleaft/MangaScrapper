using MangaScrapper.Core.Scrapers.DoujinDesu;

namespace UnitTests.MangaScrapper;

public class DoujinDesuDecryptorTests
{
    [Fact]
    public void Decrypt_ShouldSuccessfullyDecryptPayload_WhenValidEncryptedResponseProvided()
    {
        const string expectedJson = "{\"title\":\"Boku no Mizugi ga Kakusarete\",\"slug\":\"boku-no-mizugi-ga-kakusarete\"}";
        var encPayload = DoujinDesuDecryptor.Encrypt(expectedJson);

        var json = DoujinDesuDecryptor.Decrypt(encPayload);

        Assert.NotNull(json);
        Assert.Contains("Boku no Mizugi ga Kakusarete", json);
    }

    [Fact]
    public void DecryptToObject_ShouldDeserializeDtoCorrectly()
    {
        const string innerJson = "{\"title\":\"Boku no Mizugi ga Kakusarete\",\"slug\":\"boku-no-mizugi-ga-kakusarete\",\"id\":\"f4b67078-20e7-4ccf-aa0c-53c434d49f1a\",\"chapters\":[{\"title\":\"Chapter 1\"}]}";
        var encPayload = DoujinDesuDecryptor.Encrypt(innerJson);
        var rawEncResponse = $"{{\"_enc_resp_\": \"{encPayload}\"}}";

        var manga = DoujinDesuDecryptor.DecryptToObject<DoujinDesuMangaDto>(rawEncResponse);

        Assert.NotNull(manga);
        Assert.Equal("Boku no Mizugi ga Kakusarete", manga.Title);
        Assert.Equal("boku-no-mizugi-ga-kakusarete", manga.Slug);
        Assert.Equal("f4b67078-20e7-4ccf-aa0c-53c434d49f1a", manga.Id);
        Assert.Single(manga.Chapters!);
    }
}
