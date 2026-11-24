using System.Threading.Channels;

namespace Handyman.Api.Cqrs;

/// <summary>One push to send later. Type is the notification type string stored on the notification row.</summary>
public sealed record PushMessage(Guid UserId, string Type, Guid? JobId);

/// <summary>Pushes queued by a command handler. Flushed by the transaction decorator only after commit.</summary>
public sealed class PendingPushes
{
    private readonly List<PushMessage> _items = [];

    public IReadOnlyList<PushMessage> Items => _items;

    public void Add(Guid userId, string type, Guid? jobId = null) => _items.Add(new PushMessage(userId, type, jobId));

    internal void Clear() => _items.Clear();
}

/// <summary>Receives pushes after a successful commit. Sending them is E08 (PushWorker).</summary>
public interface IPushQueue
{
    void Enqueue(PushMessage message);
}

/// <summary>In-memory queue; the E08 PushWorker will read from <see cref="Reader"/>.</summary>
public sealed class PushQueue : IPushQueue
{
    private readonly Channel<PushMessage> _channel = Channel.CreateUnbounded<PushMessage>();

    public ChannelReader<PushMessage> Reader => _channel.Reader;

    public void Enqueue(PushMessage message) => _channel.Writer.TryWrite(message);
}
