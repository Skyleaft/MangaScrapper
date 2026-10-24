using HtmlAgilityPack;
using MangaScrapper.Core.Utils;
using Xunit;

namespace UnitTests.MangaScrapper;

public class TextSanitizerTests
{
    [Theory]
    [InlineData("&#8220;Aku akan menjadi Raja Bajak Laut!&#8221;", "“Aku akan menjadi Raja Bajak Laut!”")]
    [InlineData("&#8220Aku akan menjadi Raja Bajak Laut!&#8221", "“Aku akan menjadi Raja Bajak Laut!”")]
    [InlineData("&amp;#8220;Double encoded quote&amp;#8221;", "“Double encoded quote”")]
    [InlineData("&#8216Single quote&#8217", "‘Single quote’")]
    [InlineData("Halo&nbsp;Dunia", "Halo Dunia")]
    [InlineData("Tamat&#8230", "Tamat…")]
    [InlineData("Action &amp; Adventure", "Action & Adventure")]
    public void SanitizeDescription_ShouldDecodeHtmlEntitiesEvenWithoutSemicolons(string input, string expected)
    {
        var result = TextSanitizer.SanitizeDescription(input);
        Assert.Equal(expected, result);
    }

    [Fact]
    public void SanitizeDescription_ShouldConvertParagraphsAndBreaksToNewlines()
    {
        var html = "<p>Paragraf pertama dengan &#8220;quote&#8221;.</p><p>Paragraf kedua.<br>Baris baru.</p>";
        var expected = "Paragraf pertama dengan “quote”.\n\nParagraf kedua.\nBaris baru.";

        var result = TextSanitizer.SanitizeDescription(html);
        Assert.Equal(expected, result);
    }

    [Fact]
    public void SanitizeDescription_FromHtmlNode_ShouldStripTagsAndDecodeEntities()
    {
        var doc = new HtmlDocument();
        doc.LoadHtml("""
            <div itemprop="description">
                <p>&#8220Ini adalah sinopsis komik&#8221</p>
                <script>alert('iklan');</script>
                <p>Kelanjutan cerita seru &amp; menarik.</p>
            </div>
            """);

        var node = doc.DocumentNode.SelectSingleNode("//div[@itemprop='description']");
        var result = TextSanitizer.SanitizeDescription(node);

        Assert.Equal("“Ini adalah sinopsis komik”\n\nKelanjutan cerita seru & menarik.", result);
    }

    [Fact]
    public void SanitizeDescription_ShouldHandleNullOrWhitespace()
    {
        Assert.Equal(string.Empty, TextSanitizer.SanitizeDescription((string?)null));
        Assert.Equal(string.Empty, TextSanitizer.SanitizeDescription("   "));
        Assert.Equal(string.Empty, TextSanitizer.SanitizeDescription((HtmlNode?)null));
    }
}
