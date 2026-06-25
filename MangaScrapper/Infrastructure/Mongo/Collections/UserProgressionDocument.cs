using System;
using System.Collections.Generic;
using System.Linq;
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

    [BsonIgnore]
    public Guid ChapterId => ChapterLogs.OrderByDescending(x => x.LastReadAt).FirstOrDefault()?.ChapterId ?? Guid.Empty;

    [BsonIgnore]
    public double ChapterNumber => ChapterLogs.OrderByDescending(x => x.LastReadAt).FirstOrDefault()?.ChapterNumber ?? 0;

    [BsonIgnore]
    public int LastReadPage => ChapterLogs.OrderByDescending(x => x.LastReadAt).FirstOrDefault()?.LastReadPage ?? 0;

    [BsonIgnore]
    public int TotalPages => ChapterLogs.OrderByDescending(x => x.LastReadAt).FirstOrDefault()?.TotalPages ?? 0;

    [BsonIgnore]
    public bool IsCompleted => ChapterLogs.OrderByDescending(x => x.LastReadAt).FirstOrDefault()?.IsCompleted ?? false;

    [BsonIgnore]
    public int ReadingTimeSeconds => ChapterLogs.OrderByDescending(x => x.LastReadAt).FirstOrDefault()?.ReadingTimeSeconds ?? 0;

    [BsonDateTimeOptions(Kind = DateTimeKind.Utc)]
    public DateTime LastReadAt { get; set; } = DateTime.UtcNow;
    public List<UserChapterLogDocument> ChapterLogs { get; set; } = new();
    public int TotalReadingTime { get; set; }

    [BsonExtraElements]
    public BsonDocument? ExtraElements { get; set; }

    public void MigrateIfNeeded()
    {
        if (ChapterLogs == null)
        {
            ChapterLogs = new List<UserChapterLogDocument>();
        }

        if (ChapterLogs.Count == 0 && ExtraElements != null)
        {
            if (ExtraElements.Contains("ChapterId"))
            {
                var chapterIdVal = ExtraElements["ChapterId"];
                Guid chapterId = Guid.Empty;
                if (chapterIdVal.IsBsonBinaryData)
                {
                    chapterId = chapterIdVal.AsGuid;
                }
                else
                {
                    Guid.TryParse(chapterIdVal.ToString(), out chapterId);
                }

                if (chapterId != Guid.Empty)
                {
                    double chapterNumber = 0;
                    if (ExtraElements.Contains("ChapterNumber"))
                    {
                        var val = ExtraElements["ChapterNumber"];
                        if (val.IsDouble) chapterNumber = val.AsDouble;
                        else if (val.IsInt32) chapterNumber = val.AsInt32;
                        else if (val.IsInt64) chapterNumber = val.AsInt64;
                        else double.TryParse(val.ToString(), out chapterNumber);
                    }

                    int lastReadPage = 0;
                    if (ExtraElements.Contains("LastReadPage"))
                    {
                        var val = ExtraElements["LastReadPage"];
                        if (val.IsInt32) lastReadPage = val.AsInt32;
                        else if (val.IsInt64) lastReadPage = (int)val.AsInt64;
                        else if (val.IsDouble) lastReadPage = (int)val.AsDouble;
                        else int.TryParse(val.ToString(), out lastReadPage);
                    }

                    int totalPages = 0;
                    if (ExtraElements.Contains("TotalPages"))
                    {
                        var val = ExtraElements["TotalPages"];
                        if (val.IsInt32) totalPages = val.AsInt32;
                        else if (val.IsInt64) totalPages = (int)val.AsInt64;
                        else if (val.IsDouble) totalPages = (int)val.AsDouble;
                        else int.TryParse(val.ToString(), out totalPages);
                    }

                    bool isCompleted = false;
                    if (ExtraElements.Contains("IsCompleted"))
                    {
                        var val = ExtraElements["IsCompleted"];
                        if (val.IsBoolean) isCompleted = val.AsBoolean;
                        else bool.TryParse(val.ToString(), out isCompleted);
                    }

                    int readingTimeSeconds = 0;
                    if (ExtraElements.Contains("ReadingTimeSeconds"))
                    {
                        var val = ExtraElements["ReadingTimeSeconds"];
                        if (val.IsInt32) readingTimeSeconds = val.AsInt32;
                        else if (val.IsInt64) readingTimeSeconds = (int)val.AsInt64;
                        else if (val.IsDouble) readingTimeSeconds = (int)val.AsDouble;
                        else int.TryParse(val.ToString(), out readingTimeSeconds);
                    }

                    var chapterLog = new UserChapterLogDocument
                    {
                        ChapterId = chapterId,
                        ChapterNumber = chapterNumber,
                        LastReadPage = lastReadPage,
                        TotalPages = totalPages,
                        IsCompleted = isCompleted,
                        ReadingTimeSeconds = readingTimeSeconds,
                        LastReadAt = LastReadAt
                    };

                    ChapterLogs.Add(chapterLog);
                    TotalReadingTime = readingTimeSeconds;

                    ExtraElements.Remove("ChapterId");
                    ExtraElements.Remove("ChapterNumber");
                    ExtraElements.Remove("LastReadPage");
                    ExtraElements.Remove("TotalPages");
                    ExtraElements.Remove("IsCompleted");
                    ExtraElements.Remove("ReadingTimeSeconds");
                }
            }
        }
    }
}

public class UserChapterLogDocument
{
    [BsonId]
    [BsonRepresentation(BsonType.String)]
    public Guid Id { get; set; } = Guid.CreateVersion7();
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