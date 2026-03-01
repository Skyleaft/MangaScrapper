namespace MangaScrapper.Features.ScrapperKiryuu.Services;

public class KiryuuModel
{
    
}

public class KiryuuChapter
{
    public double? ChapterNumber { get; set; }
    public string? Url { get; set; }
    public int? View { get; set; }
    public DateTime DateAdded { get; set; }
    public List<KiryuuPage>Pages { get; set; } = new();
}

public class KiryuuResponse<T>
{
    public bool Success { get; set; }
    public List<T> Data { get; set; } = new List<T>();
}
public class KiryuuManga
{
    public int MangaId { get; set; }
    public string? Title { get; set; }
    public string? Link { get; set; }
    public List<KiryuuChapter>? Chapters { get; set; }
}

public class KiryuuPage
{
    public string? Alt { get; set; }
    public string? ImageUrl { get; set; }
}
