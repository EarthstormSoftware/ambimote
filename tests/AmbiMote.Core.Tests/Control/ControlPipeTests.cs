using AmbiMote.Core.Control;

namespace AmbiMote.Core.Tests.Control;

public sealed class ControlPipeTests
{
    private static readonly TimeSpan s_timeout = TimeSpan.FromSeconds(5);

    private static string NewPipeName() => $"AmbiMote.Tests.{Guid.NewGuid():N}";

    [Fact]
    public async Task SendAsync_GetsTheHandlersReply()
    {
        string name = NewPipeName();
        var received = new List<string>();
        await using var server = new ControlServer(name, request =>
        {
            lock (received)
            {
                received.Add(request);
            }

            return Task.FromResult($"echo {request}");
        });
        server.Start();

        string first = await ControlPipe.SendAsync(name, "brightness +10", s_timeout);
        string second = await ControlPipe.SendAsync(name, "toggle", s_timeout);

        Assert.Equal("echo brightness +10", first);
        Assert.Equal("echo toggle", second);
        Assert.Equal(["brightness +10", "toggle"], received);
    }

    [Fact]
    public async Task SendAsync_WithNobodyListening_TimesOut()
    {
        await Assert.ThrowsAsync<TimeoutException>(() => ControlPipe.SendAsync(NewPipeName(), "status", TimeSpan.FromMilliseconds(200)));
    }

    [Fact]
    public async Task Server_WhenTheHandlerThrows_RepliesWithTheErrorAndKeepsGoing()
    {
        string name = NewPipeName();
        await using var server = new ControlServer(name, request =>
            request == "fail" ? throw new InvalidOperationException("broken") : Task.FromResult("fine"));
        server.Start();

        var failed = ControlReply.Parse(await ControlPipe.SendAsync(name, "fail", s_timeout));
        string next = await ControlPipe.SendAsync(name, "status", s_timeout);

        Assert.Contains("broken", failed.Error, StringComparison.Ordinal);
        Assert.Equal("fine", next);
    }

    [Fact]
    public async Task Server_DropsAClientThatSendsNothingAndServesTheNext()
    {
        string name = NewPipeName();
        await using var server = new ControlServer(name, _ => Task.FromResult("ok"));
        server.Start();

        // A client that connects and goes away without a request must not stop the server.
        await using (var silent = new System.IO.Pipes.NamedPipeClientStream(".", name, System.IO.Pipes.PipeDirection.InOut))
        {
            await silent.ConnectAsync(5000);
        }

        Assert.Equal("ok", await ControlPipe.SendAsync(name, "status", s_timeout));
    }

    [Fact]
    public async Task ReadLineAsync_RefusesAnEndlessLine()
    {
        using var stream = new MemoryStream(new byte[ControlPipe.MaxLineBytes + 10]);

        await Assert.ThrowsAsync<IOException>(() => ControlPipe.ReadLineAsync(stream, CancellationToken.None));
    }
}
