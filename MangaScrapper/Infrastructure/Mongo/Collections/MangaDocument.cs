using MongoDB.Bson;
using MongoDB.Bson.Serialization.Attributes;

namespace MangaScrapper.Infrastructure.Mongo.Collections;

public class MangaDocument
{
    [BsonId]
    [BsonRepresentation(BsonType.String)]
    public Guid Id { get; set; }
    public int MalID { get; set; }
    
    public string Title { get; set; } = string.Empty;
    
    public string Author { get; set; } = string.Empty;
    
    public string Type { get; set; } = string.Empty;
    public double Rating { get; set; }
    
    [BsonIgnoreIfNull]
    public List<string>? Genres { get; set; }
    
    [BsonIgnoreIfNull]
    public string? Description { get; set; }
    
    [BsonIgnoreIfNull]
    public string? ImageUrl { get; set; }
    public string? LocalImageUrl { get; set; }
    
    [BsonIgnoreIfNull]
    public string? Status { get; set; }
    public DateTime ReleaseDate { get; set; }
    
    [BsonDateTimeOptions(Kind = DateTimeKind.Utc)]
    public DateTime CreatedAt { get; set; }
    
    [BsonDateTimeOptions(Kind = DateTimeKind.Utc)]
    public DateTime UpdatedAt { get; set; }
    
    [BsonIgnoreIfNull]
    public string? Url { get; set; }
    
    public List<ChapterDocument> Chapters { get; set; } = new();
}

public class ChapterDocument
{
    [BsonRepresentation(BsonType.String)]
    public Guid Id { get; set; } = Guid.CreateVersion7();
    public double Number { get; set; }
    [BsonIgnoreIfNull]
    public string? Link { get; set; }
    public string? ChapterProvider { get; set; }
    public int TotalView { get; set; }
    [BsonDateTimeOptions(Kind = DateTimeKind.Utc)]
    public DateTime UploadDate { get; set; }
    public List<PageDocument> Pages { get; set; } = new();
}

public class PageDocument
{
    [BsonRepresentation(BsonType.String)]
    public Guid Id { get; set; } = Guid.CreateVersion7();
    
    public string ImageUrl { get; set; } = default!;
    
    [BsonIgnoreIfNull]
    public string? LocalImageUrl { get; set; }
}