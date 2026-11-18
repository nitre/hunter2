using System.Collections.Concurrent;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Threading.Tasks;

namespace Hunter;

/// <summary>NEW, NOT THE ORIGINAL (development only): HUNTER_LIVE=port opens 127.0.0.1:port; each line sent is one command,
/// run on the game's own thread at the start of the next frame (GameView.LiveCommand), the reply sent back as lines
/// ending with a lone ".". Never in a normal game: nothing listens without the variable. Client: tools/live.py.</summary>
public sealed class Live
{
    readonly ConcurrentQueue<(string Line, TaskCompletionSource<string> Reply)> queue = new();

    public Live(int port)
    {
        var server = new TcpListener(IPAddress.Loopback, port);
        server.Start();
        Task.Run(async () =>
        {
            while (true)
            {
                var client = await server.AcceptTcpClientAsync();
                _ = Task.Run(() => Serve(client));
            }
        });
        Godot.GD.Print($"HUNTER_LIVE: listening on 127.0.0.1:{port}");
    }

    async Task Serve(TcpClient client)
    {
        using var _ = client;
        using var stream = client.GetStream();
        using var reader = new StreamReader(stream);
        using var writer = new StreamWriter(stream) { AutoFlush = true, NewLine = "\n" };
        while (await reader.ReadLineAsync() is { } line)
        {
            var reply = new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously);
            queue.Enqueue((line.Trim(), reply));
            await writer.WriteLineAsync(await reply.Task);
            await writer.WriteLineAsync(".");
        }
    }

    /// <summary>The game thread: every command waiting, through run.</summary>
    public void Pump(System.Func<string, Task<string>> run)
    {
        while (queue.TryDequeue(out var c))
        {
            var reply = c.Reply;
            Task<string> t;
            try { t = run(c.Line); }
            catch (System.Exception e) { reply.SetResult("ERROR " + e.Message); continue; }
            t.ContinueWith(d => reply.SetResult(d.IsFaulted ? "ERROR " + d.Exception!.InnerException!.Message : d.Result));   // a card readback answers frames later
        }
    }
}
