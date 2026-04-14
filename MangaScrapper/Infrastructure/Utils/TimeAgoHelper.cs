namespace MangaScrapper.Infrastructure.Utils;

public static class TimeAgoHelper
{
    public static string ToTimeAgo(this DateTime dateTime)
    {
        var now = DateTime.UtcNow;
        var diff = now - dateTime.ToUniversalTime();

        if (diff.TotalSeconds < 60)
            return $"{(int)diff.TotalSeconds}s ago";

        if (diff.TotalMinutes < 60)
            return $"{(int)diff.TotalMinutes}m ago";

        if (diff.TotalHours < 24)
            return $"{(int)diff.TotalHours}h ago";

        if (diff.TotalDays < 7)
            return $"{(int)diff.TotalDays}d ago";

        if (diff.TotalDays < 30)
            return $"{(int)(diff.TotalDays / 7)}w ago";

        if (diff.TotalDays < 365)
            return $"{(int)(diff.TotalDays / 30)}mo ago";

        return $"{(int)(diff.TotalDays / 365)}y ago";
    }
}