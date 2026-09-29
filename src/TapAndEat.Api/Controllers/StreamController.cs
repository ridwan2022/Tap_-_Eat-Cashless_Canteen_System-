using System.Text.Json;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using TapAndEat.Api.Infrastructure;

namespace TapAndEat.Api.Controllers;

/// <summary>
/// Task 6.4 — Server-Sent Events for the counter and kitchen screens.
/// Browsers' EventSource can't send an Authorization header, so this endpoint
/// (only) also accepts <c>?access_token=</c>; see BearerTokenAuthenticationHandler.
/// </summary>
[ApiController]
[Route("api/stream")]
[Authorize(Roles = Roles.Staff)]
public class StreamController : ControllerBase
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);
    private static readonly TimeSpan Heartbeat = TimeSpan.FromSeconds(15);

    private readonly IEventBroadcaster _events;

    public StreamController(IEventBroadcaster events)
    {
        _events = events;
    }

    [HttpGet("kitchen")]
    public async Task Kitchen(CancellationToken ct)
    {
        Response.Headers.ContentType = "text/event-stream";
        Response.Headers.CacheControl = "no-cache";
        Response.Headers["X-Accel-Buffering"] = "no"; // don't let nginx buffer the stream

        using var subscription = _events.Subscribe();
        await WriteAsync("ready", new { connectedAtUtc = DateTimeOffset.UtcNow }, ct);

        try
        {
            while (!ct.IsCancellationRequested)
            {
                using var idle = CancellationTokenSource.CreateLinkedTokenSource(ct);
                idle.CancelAfter(Heartbeat);
                try
                {
                    if (!await subscription.Reader.WaitToReadAsync(idle.Token)) break;
                    while (subscription.Reader.TryRead(out var evt))
                    {
                        await WriteAsync(evt.Type, evt.Data, ct);
                    }
                }
                catch (OperationCanceledException) when (!ct.IsCancellationRequested)
                {
                    // Quiet period: send a comment so proxies and the browser keep the connection open.
                    await Response.WriteAsync(": keep-alive\n\n", ct);
                    await Response.Body.FlushAsync(ct);
                }
            }
        }
        catch (OperationCanceledException)
        {
            // client disconnected — normal
        }
    }

    private async Task WriteAsync(string type, object data, CancellationToken ct)
    {
        await Response.WriteAsync($"event: {type}\ndata: {JsonSerializer.Serialize(data, Json)}\n\n", ct);
        await Response.Body.FlushAsync(ct);
    }
}
