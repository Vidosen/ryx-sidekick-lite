// SPDX-License-Identifier: GPL-3.0-only
using System;
using System.Collections.Generic;
using System.IO;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace Ryx.Sidekick.AgentHost.Tests;

/// <summary>
/// Minimal JSON-lines test client that talks to the daemon over a real
/// loopback socket. Reads run on a background pump into a queue so tests can
/// await specific message types with bounded timeouts (no Thread.Sleep races).
/// </summary>
internal sealed class TestClient : IDisposable
{
    private readonly TcpClient _tcp;
    private readonly NetworkStream _stream;
    private readonly StreamReader _reader;
    private readonly object _writeLock = new();
    private readonly System.Collections.Concurrent.BlockingCollection<JsonElement> _inbox = new();
    private readonly CancellationTokenSource _cts = new();
    private readonly List<JsonDocument> _docs = new();

    public TestClient(int port)
    {
        _tcp = new TcpClient();
        _tcp.Connect("127.0.0.1", port);
        _tcp.NoDelay = true;
        _stream = _tcp.GetStream();
        _reader = new StreamReader(_stream, new UTF8Encoding(false), false, 4096, leaveOpen: true);
        _ = Task.Run(PumpAsync);
    }

    private async Task PumpAsync()
    {
        try
        {
            while (!_cts.IsCancellationRequested)
            {
                var line = await _reader.ReadLineAsync().ConfigureAwait(false);
                if (line == null)
                    break;
                if (line.Length == 0)
                    continue;
                var doc = JsonDocument.Parse(line);
                lock (_docs) _docs.Add(doc);
                _inbox.Add(doc.RootElement);
            }
        }
        catch { /* socket closed */ }
        finally
        {
            try { _inbox.CompleteAdding(); } catch { /* ignore */ }
        }
    }

    private void SendRaw(string json)
    {
        var bytes = Encoding.UTF8.GetBytes(json + "\n");
        lock (_writeLock)
        {
            _stream.Write(bytes, 0, bytes.Length);
            _stream.Flush();
        }
    }

    public void Send(object message) => SendRaw(JsonSerializer.Serialize(message));

    /// <summary>Wait for the next message whose "t" == type. Throws on timeout.</summary>
    public JsonElement WaitFor(string type, int timeoutMs = 5000)
    {
        var deadline = DateTime.UtcNow.AddMilliseconds(timeoutMs);
        while (DateTime.UtcNow < deadline)
        {
            var remaining = (int)Math.Max(1, (deadline - DateTime.UtcNow).TotalMilliseconds);
            if (!_inbox.TryTake(out var el, remaining))
                break;
            if (GetT(el) == type)
                return el;
            // Not the type we want — keep draining.
        }
        throw new TimeoutException($"Timed out waiting for message type '{type}' after {timeoutMs}ms.");
    }

    /// <summary>Collect messages of a type until <paramref name="predicate"/> says stop or timeout.</summary>
    public List<JsonElement> CollectWhile(string type, Func<List<JsonElement>, bool> stop, int timeoutMs = 5000)
    {
        var result = new List<JsonElement>();
        var deadline = DateTime.UtcNow.AddMilliseconds(timeoutMs);
        while (DateTime.UtcNow < deadline)
        {
            var remaining = (int)Math.Max(1, (deadline - DateTime.UtcNow).TotalMilliseconds);
            if (!_inbox.TryTake(out var el, remaining))
                break;
            if (GetT(el) == type)
            {
                result.Add(el);
                if (stop(result))
                    return result;
            }
        }
        return result;
    }

    /// <summary>True if the read pump observed EOF (server closed the socket).</summary>
    public bool WaitForClose(int timeoutMs = 5000)
    {
        var deadline = DateTime.UtcNow.AddMilliseconds(timeoutMs);
        while (DateTime.UtcNow < deadline)
        {
            if (_inbox is { IsAddingCompleted: true, Count: 0 })
                return true;
            // Try to take with a short slice; CompleteAdding will surface as no more items.
            if (!_inbox.TryTake(out _, 50))
            {
                if (_inbox.IsAddingCompleted)
                    return true;
            }
        }
        return _inbox.IsAddingCompleted;
    }

    private static string GetT(JsonElement el)
        => el.TryGetProperty("t", out var t) ? t.GetString() ?? "" : "";

    public void Dispose()
    {
        _cts.Cancel();
        try { _reader.Dispose(); } catch { /* ignore */ }
        try { _stream.Dispose(); } catch { /* ignore */ }
        try { _tcp.Close(); } catch { /* ignore */ }
        lock (_docs)
        {
            foreach (var d in _docs)
            {
                try { d.Dispose(); } catch { /* ignore */ }
            }
            _docs.Clear();
        }
    }
}