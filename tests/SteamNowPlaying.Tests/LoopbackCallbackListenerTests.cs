using System.Net;
using System.Net.Sockets;
using System.Text;
using SteamNowPlaying.Spotify;
using Xunit;

namespace SteamNowPlaying.Tests;

public class LoopbackCallbackListenerTests
{
    [Fact]
    public void Parses_callback_request_line_and_query()
    {
        var (matched, query) = LoopbackCallbackListener.ParseRequest(
            "GET /callback?code=abc%2F123&state=xyz HTTP/1.1\r\nHost: 127.0.0.1:8888\r\n\r\n", "/callback");
        Assert.True(matched);
        Assert.Equal("abc/123", query["code"]);
        Assert.Equal("xyz", query["state"]);
    }

    [Fact]
    public void Other_paths_and_methods_do_not_match()
    {
        Assert.False(LoopbackCallbackListener.ParseRequest("GET /favicon.ico HTTP/1.1\r\n\r\n", "/callback").Matched);
        Assert.False(LoopbackCallbackListener.ParseRequest("POST /callback HTTP/1.1\r\n\r\n", "/callback").Matched);
        Assert.False(LoopbackCallbackListener.ParseRequest("garbage", "/callback").Matched);
    }

    [Fact]
    public void Query_parsing_handles_plus_errors_and_empty_values()
    {
        var q = LoopbackCallbackListener.ParseQuery("error=access_denied&note=a+b&empty=&flag");
        Assert.Equal("access_denied", q["error"]);
        Assert.Equal("a b", q["note"]);
        Assert.Equal("", q["empty"]);
        Assert.Equal("", q["flag"]);
    }

    [Fact]
    public async Task Returns_the_callback_even_when_an_idle_connection_arrives_first()
    {
        using var listener = new LoopbackCallbackListener(0);
        listener.Start();
        var port = listener.Port;
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(20));

        var wait = listener.WaitForCallbackAsync("/callback", "<p>done</p>", cts.Token);

        // A browser's spare connection that never sends a request.
        using var idle = new TcpClient();
        await idle.ConnectAsync(IPAddress.Loopback, port);

        // A stray favicon request gets a 404.
        var favicon = await SendAsync(port, "GET /favicon.ico HTTP/1.1\r\nHost: x\r\n\r\n");
        Assert.StartsWith("HTTP/1.1 404", favicon);

        var response = await SendAsync(port, "GET /callback?code=C&state=S HTTP/1.1\r\nHost: x\r\n\r\n");
        Assert.StartsWith("HTTP/1.1 200", response);
        Assert.Contains("<p>done</p>", response);

        var query = await wait;
        Assert.Equal("C", query["code"]);
        Assert.Equal("S", query["state"]);
    }

    [Fact]
    public async Task Cancelling_stops_the_wait()
    {
        using var listener = new LoopbackCallbackListener(0);
        listener.Start();
        using var cts = new CancellationTokenSource(TimeSpan.FromMilliseconds(200));
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => listener.WaitForCallbackAsync("/callback", "", cts.Token));
    }

    [Fact]
    public void Busy_port_gives_a_helpful_error()
    {
        using var first = new LoopbackCallbackListener(0);
        first.Start();
        using var second = new LoopbackCallbackListener(first.Port);
        var ex = Assert.Throws<InvalidOperationException>(() => second.Start());
        Assert.Contains("already in use", ex.Message);
    }

    static async Task<string> SendAsync(int port, string request)
    {
        using var client = new TcpClient();
        await client.ConnectAsync(IPAddress.Loopback, port);
        var stream = client.GetStream();
        await stream.WriteAsync(Encoding.ASCII.GetBytes(request));
        using var reader = new StreamReader(stream, Encoding.UTF8);
        return await reader.ReadToEndAsync();
    }
}
