namespace MangaScrapper.Features.Manga.GetAllChapters;

public class Response
{
    public Guid Id { get; set; }
    public double Number { get; set; }
    public int TotalView { get; set; }
    public DateTime UploadDate { get; set; }
    public int PageCount { get; set; }
    public bool IsChapterAvailable { get; set; }
}