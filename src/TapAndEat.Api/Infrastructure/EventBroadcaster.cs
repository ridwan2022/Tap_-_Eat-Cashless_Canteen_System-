using System.Collections.Concurrent;
using System.Threading.Channels;

namespace TapAndEat.Api.Infrastructure;

public record ServerEvent(string Type, object Data);

public sealed class EventSubscription : IDisposable
{
    private readonly Action _onDispose;
    public ChannelReader<ServerEvent> Reader { get; }

    public EventSubscription(ChannelReader<ServerEvent> reader, Action onDispose)
    {
        Reader = reader;
        _onDispose = onDispose;
    }

    public void Dispose() => _onDispose();
}

/// <summary>
/// Task 6.4 — fan-out for the Server-Sent Events stream. Services publish
/// (RFID tap results, token status changes); every connected counter/kitchen
/// screen holds a subscription and receives them within moments. Slow or
/// stalled clients drop the oldest events rather than blocking publishers.
/// </summary>
public interface IEventBroadcaster
{
    EventSubscription Subscribe();
    void Publish(ServerEvent evt);
}

public class EventBroadcaster : IEventBroadcaster
{
    private readonly ConcurrentDictionary<Guid, Channel<ServerEvent>> _subscribers = new();

    public EventSubscription Subscribe()
    {
        var id = Guid.NewGuid();
        var channel = Channel.CreateBounded<ServerEvent>(new BoundedChannelOptions(64)
        {
            FullMode = BoundedChannelFullMode.DropOldest,
            SingleReader = true
        });
        _subscribers[id] = channel;
        return new EventSubscription(channel.Reader, () =>
        {
            if (_subscribers.TryRemove(id, out var removed))
            {
                removed.Writer.TryComplete();
            }
        });
    }

    public void Publish(ServerEvent evt)
    {
        foreach (var channel in _subscribers.Values)
        {
            channel.Writer.TryWrite(evt);
        }
    }
}
