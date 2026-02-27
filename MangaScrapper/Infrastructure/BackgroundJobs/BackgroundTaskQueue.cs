using System.Threading.Channels;

namespace MangaScrapper.Infrastructure.BackgroundJobs;

public record QueueItem(Guid Id, string MangaTitle, double ChapterNumber, string Status, DateTime QueuedAt);

public interface IBackgroundTaskQueue
{
    ValueTask QueueBackgroundWorkItemAsync(string mangaTitle, double chapterNumber, Func<CancellationToken, ValueTask> workItem);

    ValueTask<(Func<CancellationToken, ValueTask> WorkItem, Guid Id)> DequeueAsync(CancellationToken cancellationToken);

    List<QueueItem> GetQueueItems();
    
    void UpdateStatus(Guid id, string status);
    
    void Remove(Guid id);
}

public class BackgroundTaskQueue : IBackgroundTaskQueue
{
    private readonly Channel<(Func<CancellationToken, ValueTask> WorkItem, Guid Id)> _queue;
    private readonly System.Collections.Concurrent.ConcurrentDictionary<Guid, QueueItem> _items = new();

    public BackgroundTaskQueue(int capacity)
    {
        var options = new BoundedChannelOptions(capacity)
        {
            FullMode = BoundedChannelFullMode.Wait
        };
        _queue = Channel.CreateBounded<(Func<CancellationToken, ValueTask>, Guid)>(options);
    }

    public async ValueTask QueueBackgroundWorkItemAsync(string mangaTitle, double chapterNumber, Func<CancellationToken, ValueTask> workItem)
    {
        ArgumentNullException.ThrowIfNull(workItem);

        var id = Guid.NewGuid();
        var item = new QueueItem(id, mangaTitle, chapterNumber, "Queued", DateTime.UtcNow);
        _items.TryAdd(id, item);

        await _queue.Writer.WriteAsync((workItem, id));
    }

    public async ValueTask<(Func<CancellationToken, ValueTask> WorkItem, Guid Id)> DequeueAsync(CancellationToken cancellationToken)
    {
        return await _queue.Reader.ReadAsync(cancellationToken);
    }

    public List<QueueItem> GetQueueItems()
    {
        return _items.Values.OrderBy(x => x.QueuedAt).ToList();
    }

    public void UpdateStatus(Guid id, string status)
    {
        if (_items.TryGetValue(id, out var item))
        {
            _items[id] = item with { Status = status };
        }
    }

    public void Remove(Guid id)
    {
        _items.TryRemove(id, out _);
    }
}
