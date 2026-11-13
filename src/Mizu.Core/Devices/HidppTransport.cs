using System.Threading.Channels;
using Mizu.Core.Native;
using Mizu.Core.Services;

namespace Mizu.Core.Devices;

// Windows routes short and long report IDs to different top-level collections.
// Listen on both: a getFeature request may be short while a name reply is long.
public sealed class HidppTransport : IAsyncDisposable
{
    private readonly List<(HidEndpoint Endpoint, HidChannel Io)> channels = [];
    private readonly CancellationTokenSource lifetime;
    private readonly Channel<byte[]> incoming = Channel.CreateBounded<byte[]>(new BoundedChannelOptions(128) { FullMode = BoundedChannelFullMode.DropOldest });
    private readonly Task[] readers;
    public HidppTransport(IEnumerable<HidEndpoint> endpoints, CancellationToken token)
    {
        lifetime = CancellationTokenSource.CreateLinkedTokenSource(token);
        try
        {
            foreach (var endpoint in endpoints.Where(x => x.Usage is 1 or 2 && x.InputLength > 0).DistinctBy(x => x.Usage))
                channels.Add((endpoint, new HidChannel(endpoint)));
            if (channels.Count == 0) throw new IOException("No HID++ channel.");
            readers = channels.Select(channel => Task.Run(async () =>
            {
                try
                {
                    while (!lifetime.IsCancellationRequested)
                        await incoming.Writer.WriteAsync(await channel.Io.ReceiveAsync(lifetime.Token), lifetime.Token);
                }
                catch (OperationCanceledException) { }
                catch (Exception ex) when (ex is IOException or ObjectDisposedException) { AppLog.Write("hidpp.reader", ex.GetType().Name); }
            })).ToArray();
        }
        catch { foreach (var c in channels) c.Io.Dispose(); lifetime.Dispose(); throw; }
    }
    public async Task<byte[]?> QueryAsync(byte[] longRequest, CancellationToken token)
    {
        while (incoming.Reader.TryRead(out _)) { }
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(token); deadline.CancelAfter(550);
        // Bolt / HID++ 4.x may ignore a short root ping even when it exposes
        // a short collection. Prefer the long request; accept replies on both.
        var output = channels.First(x => x.Endpoint.OutputLength >= 20);
        var request = longRequest;
        try
        {
            await output.Io.SendAsync(request, deadline.Token);
            while (!deadline.IsCancellationRequested)
            {
                var reply = await incoming.Reader.ReadAsync(deadline.Token);
                if (HidppProtocol.Matches(reply, request)) return reply;
            }
        }
        catch (OperationCanceledException) when (!token.IsCancellationRequested) { }
        return null;
    }
    public async ValueTask DisposeAsync()
    {
        await lifetime.CancelAsync();
        try { await Task.WhenAll(readers).WaitAsync(TimeSpan.FromSeconds(1)); } catch (TimeoutException) { }
        foreach (var c in channels) c.Io.Dispose(); lifetime.Dispose();
    }
}
