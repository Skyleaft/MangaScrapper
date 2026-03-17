using MongoDB.Bson;
using MongoDB.Bson.Serialization.Attributes;

namespace MangaScrapper.Infrastructure.Mongo.Collections;

public class UserProgressionDocument
{
    [BsonId]
    [BsonRepresentation(BsonType.String)]
    public Guid Id { get; set; } = Guid.CreateVersion7();

    [BsonRepresentation(BsonType.String)]
    public Guid UserId { get; set; }

    [BsonRepresentation(BsonType.String)]
    public Guid MangaId { get; set; }

    [BsonRepresentation(BsonType.String)]
    public Guid ChapterId { get; set; }

    public double ChapterNumber { get; set; }

    public int LastReadPage { get; set; }

    public int TotalPages { get; set; }

    public bool IsCompleted { get; set; }

    public int ReadingTimeSeconds { get; set; }

    [BsonDateTimeOptions(Kind = DateTimeKind.Utc)]
    public DateTime LastReadAt { get; set; } = DateTime.UtcNow;
}