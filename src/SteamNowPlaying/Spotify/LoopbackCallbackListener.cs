using System.Net;
using System.Net.Sockets;
using System.Text;

namespace SteamNowPlaying.Spotify;

/// <summary>
/// Minimal HTTP listener on 127.0.0.1 that catches Spotify's redirect after sign-in.
/// Uses a raw socket rather than HttpListener, which needs admin rights to bind 127.0.0.1.
/// </summary>
public sealed class LoopbackCallbackListener : IDisposable
{
    static readonly IReadOnlyDictionary<string, string> Empty = new Dictionary<string, string>();

    readonly TcpListener listener;
    readonly int port;

    public LoopbackCallbackListener(int port)
    {
        this.port = port;
        listener = new TcpListener(IPAddress.Loopback, port);
    }

    public int Port => ((IPEndPoint)listener.LocalEndpoint).Port;

    public void Start()
    {
        try
        {
            listener.Start();
        }
        catch (SocketException ex) when (ex.SocketErrorCode is SocketError.AddressAlreadyInUse or SocketError.AccessDenied)
        {
            throw new InvalidOperationException(
                $"Port {port} is already in use, so Spotify can't hand you back to this app. " +
                "Close whatever is using it (for example the old command-line version) and try again.", ex);
        }
    }

    /// <summary>
    /// Waits for a GET to <paramref name="path"/> and returns its query string.
    /// Connections are handled in parallel because browsers open spare connections that never send anything.
    /// </summary>
    public async Task<IReadOnlyDictionary<string, string>> WaitForCallbackAsync(string path, string responseHtml, CancellationToken ct)
    {
        var result = new TaskCompletionSource<IReadOnlyDictionary<string, string>>(TaskCreationOptions.RunContinuationsAsynchronously);
        using var registration = ct.Register(() => result.TrySetCanceled(ct));
        _ = AcceptLoopAsync(path, responseHtml, result, ct);
        return await result.Task.ConfigureAwait(false);
    }

    async Task AcceptLoopAsync(string path, string responseHtml, TaskCompletionSource<IReadOnlyDictionary<string, string>> result, CancellationToken ct)
    {
        while (!result.Task.IsCompleted)
        {
            TcpClient client;
            try
            {
                client = await listener.AcceptTcpClientAsync(ct).ConfigureAwait(false);
            }
            catch
            {
                return; // cancelled or listener stopped
            }
            _ = HandleClientAsync(client, path, responseHtml, result);
        }
    }

    static async Task HandleClientAsync(TcpClient client, string path, string responseHtml, TaskCompletionSource<IReadOnlyDictionary<string, string>> result)
    {
        using (client)
        {
            try
            {
                using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
                var stream = client.GetStream();
                var head = await ReadHeadAsync(stream, timeout.Token).ConfigureAwait(false);
                if (head is null) return;

                var (matched, query) = ParseRequest(head, path);
                if (!matched)
                {
                    await WriteResponseAsync(stream, "404 Not Found", "<!doctype html><title>Not found</title>", timeout.Token).ConfigureAwait(false);
                    return;
                }

                await WriteResponseAsync(stream, "200 OK", responseHtml, timeout.Token).ConfigureAwait(false);
                result.TrySetResult(query);
            }
            catch
            {
                // A broken or idle connection just gets dropped.
            }
        }
    }

    internal static async Task<string?> ReadHeadAsync(Stream stream, CancellationToken ct)
    {
        var buffer = new byte[8192];
        var total = 0;
        while (total < buffer.Length)
        {
            var read = await stream.ReadAsync(buffer.AsMemory(total), ct).ConfigureAwait(false);
            if (read == 0) break;
            total += read;
            if (buffer.AsSpan(0, total).IndexOf("\r\n\r\n"u8) >= 0) break;
        }
        return total == 0 ? null : Encoding.ASCII.GetString(buffer, 0, total);
    }

    /// <summary>Parses "GET /path?query HTTP/1.1". Returns whether the path matched, and the query.</summary>
    public static (bool Matched, IReadOnlyDictionary<string, string> Query) ParseRequest(string head, string path)
    {
        var requestLine = head.Split("\r\n", 2)[0];
        var parts = requestLine.Split(' ');
        if (parts.Length < 2 || parts[0] != "GET") return (false, Empty);

        var target = parts[1];
        var q = target.IndexOf('?');
        var targetPath = q >= 0 ? target[..q] : target;
        if (!string.Equals(targetPath, path, StringComparison.Ordinal)) return (false, Empty);

        return (true, ParseQuery(q >= 0 ? target[(q + 1)..] : ""));
    }

    public static IReadOnlyDictionary<string, string> ParseQuery(string query)
    {
        var values = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var pair in query.Split('&', StringSplitOptions.RemoveEmptyEntries))
        {
            var eq = pair.IndexOf('=');
            var key = Decode(eq >= 0 ? pair[..eq] : pair);
            var value = eq >= 0 ? Decode(pair[(eq + 1)..]) : "";
            values[key] = value;
        }
        return values;
    }

    static string Decode(string s) => Uri.UnescapeDataString(s.Replace('+', ' '));

    static async Task WriteResponseAsync(Stream stream, string status, string html, CancellationToken ct)
    {
        var body = Encoding.UTF8.GetBytes(html);
        var header =
            $"HTTP/1.1 {status}\r\n" +
            "Content-Type: text/html; charset=utf-8\r\n" +
            $"Content-Length: {body.Length}\r\n" +
            "Cache-Control: no-store\r\n" +
            "Connection: close\r\n\r\n";
        await stream.WriteAsync(Encoding.ASCII.GetBytes(header), ct).ConfigureAwait(false);
        await stream.WriteAsync(body, ct).ConfigureAwait(false);
        await stream.FlushAsync(ct).ConfigureAwait(false);
    }

    public void Dispose()
    {
        try { listener.Stop(); }
        catch { /* already stopped */ }
    }
}
