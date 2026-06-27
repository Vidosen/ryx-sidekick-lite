// SPDX-License-Identifier: GPL-3.0-only
using System;
using System.Collections.Generic;
using System.IO;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using Ryx.Sidekick.AgentHost.Protocol;

namespace Ryx.Sidekick.AgentHost;

/// <summary>
/// One TCP client connection. Owns a UTF-8 line reader/writer over the socket.
/// Writes are serialized under a lock so the streaming thread (child output)
/// and the request-handling thread cannot interleave bytes on the wire.
/// Tracks which session handles this connection is subscribed to for live
/// streaming (a handle becomes subscribed on START or ATTACH).
/// </summary>
internal sealed class ClientConnection : IDisposable
{
    private readonly TcpClient _client;
    private readonly NetworkStream _stream;
    private readonly StreamReader _reader;
    private readonly object _writeLock = new();
    private readonly HashSet<string> _subscribed = new();
    private volatile bool _closed;

    public ClientConnection(TcpClient client)
    {
        _client = client;
        _client.NoDelay = true;
        _stream = client.GetStream();
        // UTF-8 without BOM; leave the socket open across reader lifetime.
        _reader = new StreamReader(_stream, new UTF8Encoding(false), false, 4096, leaveOpen: true);
    }

    public bool Authenticated { get; set; }
    public int Id { get; } = NextId();

    public bool IsClosed => _closed;

    /// <summary>Mark a session handle as live-subscribed for this connection.</summary>
    public void Subscribe(string handle)
    {
        lock (_subscribed)
            _subscribed.Add(handle);
    }

    public bool IsSubscribed(string handle)
    {
        lock (_subscribed)
            return _subscribed.Contains(handle);
    }

    /// <summary>Blocking read of the next JSON line; null on EOF/disconnect.</summary>
    public string? ReadLine()
    {
        try { return _reader.ReadLine(); }
        catch { return null; }
    }

    /// <summary>Serialize and send a message as one UTF-8 line. Swallows write errors.</summary>
    public bool Send<T>(T message)
    {
        var json = WireCodec.Serialize(message);
        return SendRaw(json);
    }

    public bool SendRaw(string jsonLine)
    {
        if (_closed)
            return false;

        var bytes = Encoding.UTF8.GetBytes(jsonLine + "\n");
        lock (_writeLock)
        {
            if (_closed)
                return false;
            try
            {
                _stream.Write(bytes, 0, bytes.Length);
                _stream.Flush();
                return true;
            }
            catch
            {
                _closed = true;
                return false;
            }
        }
    }

    public void Close()
    {
        if (_closed)
            return;
        _closed = true;
        try { _reader.Dispose(); } catch { /* ignore */ }
        try { _stream.Dispose(); } catch { /* ignore */ }
        try { _client.Close(); } catch { /* ignore */ }
    }

    public void Dispose() => Close();

    private static int _idCounter;
    private static int NextId() => Interlocked.Increment(ref _idCounter);
}