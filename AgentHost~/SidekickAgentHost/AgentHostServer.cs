// SPDX-License-Identifier: GPL-3.0-only
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;
using Ryx.Sidekick.AgentHost.Protocol;

namespace Ryx.Sidekick.AgentHost;

/// <summary>
/// The daemon: a protocol-blind subprocess multiplexer. Binds a loopback TCP
/// listener, authenticates one reconnectable client at a time, owns N child
/// sessions, relays stdio, buffers output with monotonic seqs, and supports
/// ATTACH replay + reconnect. Children survive client disconnect; the daemon
/// self-terminates on an explicit SHUTDOWN, when the owner Editor process dies
/// (authoritative — independent of grace), or on grace expiry (no client
/// returned within the grace window).
/// </summary>
internal sealed class AgentHostServer : IDisposable
{
    // Generous per-session safety bounds (durable-trim keeps the real buffer;
    // these only bound a session that overruns before any TRIM — see OutputBuffer).
    private const int DefaultMaxBufferLines = 200_000;
    private const long DefaultMaxBufferBytes = 8L * 1024 * 1024;

    // Exited sessions are retained so a domain-reload reattach can still replay their buffer (and learn
    // the exit). Bound that retention: keep at most this many exited sessions, evicting the OLDEST when a
    // new exit pushes past the cap, so a long-lived daemon cannot accumulate them without limit. Retained
    // sessions are cheap (their buffers are trimmed at turn completion), and the newest exited session — a
    // reload's reattach target — is never the one evicted.
    private const int DefaultMaxRetainedExitedSessions = 32;

    private readonly DaemonOptions _options;
    private readonly string _token;
    private readonly int _maxBufferLines;
    private readonly long _maxBufferBytes;
    private readonly int _maxRetainedExitedSessions;

    private readonly ConcurrentDictionary<string, ChildSession> _sessions = new();
    private readonly object _activeLock = new();
    private readonly CancellationTokenSource _cts = new();
    private readonly TaskCompletionSource<bool> _stopped =
        new(TaskCreationOptions.RunContinuationsAsynchronously);

    private TcpListener? _listener;
    private ClientConnection? _active;

    // Grace: when no client is connected, _graceDeadlineUtc is set; the
    // watchdog stops the daemon once it passes. The watchdog also stops the
    // daemon immediately if the owner Editor pid dies, regardless of grace.
    private DateTime? _graceDeadlineUtc;
    private int _shuttingDown;

    public AgentHostServer(DaemonOptions options, string token,
        int maxBufferLines = DefaultMaxBufferLines, long maxBufferBytes = DefaultMaxBufferBytes,
        int maxRetainedExitedSessions = DefaultMaxRetainedExitedSessions)
    {
        _options = options;
        _token = token;
        _maxBufferLines = maxBufferLines;
        _maxBufferBytes = maxBufferBytes;
        _maxRetainedExitedSessions = maxRetainedExitedSessions;
    }

    /// <summary>The OS-assigned loopback port (valid after <see cref="Start"/>).</summary>
    public int Port { get; private set; }

    /// <summary>Completes when the daemon has fully stopped.</summary>
    public Task Stopped => _stopped.Task;

    public int SessionCount => _sessions.Count;

    /// <summary>
    /// Bind the listener (127.0.0.1:0), write discovery files (token + pid first,
    /// then port AFTER bind succeeds), and launch the accept + watchdog loops.
    /// </summary>
    public void Start()
    {
        _listener = new TcpListener(IPAddress.Loopback, 0);
        _listener.Start();
        Port = ((IPEndPoint)_listener.LocalEndpoint).Port;

        // Token + pid first; port file LAST so its existence ⇒ ready.
        if (!string.IsNullOrEmpty(_options.TokenFile))
            DiscoveryPaths.WriteTokenFile(_options.TokenFile!, _token);
        if (!string.IsNullOrEmpty(_options.PidFile))
            DiscoveryPaths.WritePidFile(_options.PidFile!, Environment.ProcessId);
        if (!string.IsNullOrEmpty(_options.PortFile))
            DiscoveryPaths.WritePortFile(_options.PortFile!, Port);

        // No client yet → arm the grace timer immediately so a daemon that is
        // never connected to does not linger forever.
        ArmGrace();

        _ = Task.Run(AcceptLoopAsync);
        _ = Task.Run(WatchdogLoopAsync);
    }

    private async Task AcceptLoopAsync()
    {
        var listener = _listener!;
        while (!_cts.IsCancellationRequested)
        {
            TcpClient tcp;
            try
            {
                tcp = await listener.AcceptTcpClientAsync().ConfigureAwait(false);
            }
            catch (ObjectDisposedException) { break; }
            catch (SocketException) { break; }
            catch (Exception) { continue; }

            var conn = new ClientConnection(tcp);
            _ = Task.Run(() => HandleConnectionAsync(conn));
        }
    }

    private async Task HandleConnectionAsync(ClientConnection conn)
    {
        try
        {
            // First line MUST be a valid, authenticated HELLO.
            var helloLine = conn.ReadLine();
            if (helloLine == null)
            {
                conn.Close();
                return;
            }

            var hello = WireCodec.ReadIncoming(helloLine);
            if (hello.Type != MessageTypes.Hello || !ConstantTime.Equals(hello.Token, _token))
            {
                // Bad token / not a HELLO: close silently (no HELLO_OK).
                conn.Close();
                return;
            }

            if (hello.Proto != DaemonInfo.Protocol)
            {
                conn.Send(new ErrorMessage { message = $"proto mismatch: daemon={DaemonInfo.Protocol} client={hello.Proto}" });
                conn.Close();
                return;
            }

            conn.Authenticated = true;
            PromoteActiveConnection(conn);

            conn.Send(new HelloOkMessage
            {
                daemonVersion = DaemonInfo.Version,
                proto = DaemonInfo.Protocol,
                sessions = SnapshotSessions(),
            });

            // Dispatch loop.
            while (!_cts.IsCancellationRequested)
            {
                var line = conn.ReadLine();
                if (line == null)
                    break; // client disconnected

                var msg = WireCodec.ReadIncoming(line);
                if (!msg.IsValid)
                    continue;

                if (await DispatchAsync(conn, msg).ConfigureAwait(false))
                    break; // SHUTDOWN requested
            }
        }
        catch (Exception)
        {
            /* connection-level errors are non-fatal to the daemon */
        }
        finally
        {
            OnConnectionClosed(conn);
        }
    }

    /// <summary>Returns true if the daemon should stop (SHUTDOWN).</summary>
    private async Task<bool> DispatchAsync(ClientConnection conn, IncomingMessage msg)
    {
        switch (msg.Type)
        {
            case MessageTypes.Ping:
                conn.Send(new PongMessage());
                return false;

            case MessageTypes.Start:
                HandleStart(conn, msg);
                return false;

            case MessageTypes.Write:
                HandleWrite(conn, msg);
                return false;

            case MessageTypes.CloseStdin:
                if (TryGetSession(msg.Handle, out var closeSession))
                    closeSession!.CloseStdin();
                return false;

            case MessageTypes.Stop:
                if (TryGetSession(msg.Handle, out var stopSession))
                    stopSession!.Stop();
                return false;

            case MessageTypes.Interrupt:
                if (TryGetSession(msg.Handle, out var intSession))
                    await intSession!.InterruptAsync().ConfigureAwait(false);
                return false;

            case MessageTypes.Attach:
                HandleAttach(conn, msg);
                return false;

            case MessageTypes.Trim:
                if (TryGetSession(msg.Handle, out var trimSession))
                    trimSession!.Trim(msg.SafeSeq);
                return false;

            case MessageTypes.Shutdown:
                Stop();
                return true;

            case MessageTypes.Hello:
                // Duplicate HELLO on an already-authenticated connection: ignore.
                return false;

            default:
                conn.Send(new ErrorMessage { handle = msg.Handle, message = $"unknown message type: {msg.Type}" });
                return false;
        }
    }

    private void HandleStart(ClientConnection conn, IncomingMessage msg)
    {
        if (msg.Spec == null || string.IsNullOrEmpty(msg.Spec.filename))
        {
            conn.Send(new ErrorMessage { message = "START missing spec.filename" });
            return;
        }

        var handle = string.IsNullOrEmpty(msg.Handle) ? Guid.NewGuid().ToString("N") : msg.Handle!;
        var session = new ChildSession(handle, _maxBufferLines, _maxBufferBytes);
        WireSession(session);

        if (!_sessions.TryAdd(handle, session))
        {
            conn.Send(new ErrorMessage { handle = handle, message = "handle already exists" });
            return;
        }

        conn.Subscribe(handle);

        if (!session.Start(msg.Spec))
        {
            _sessions.TryRemove(handle, out _);
            conn.Send(new ErrorMessage { handle = handle, message = "failed to start process" });
            return;
        }

        conn.Send(new StartedMessage { handle = handle });
    }

    private void HandleWrite(ClientConnection conn, IncomingMessage msg)
    {
        if (!TryGetSession(msg.Handle, out var session))
            return;
        session!.Write(msg.Data ?? string.Empty, msg.AppendNewline);
    }

    private void HandleAttach(ClientConnection conn, IncomingMessage msg)
    {
        if (!TryGetSession(msg.Handle, out var session))
        {
            conn.Send(new ErrorMessage { handle = msg.Handle, message = "no such session" });
            return;
        }

        // Snapshot replay under the session buffer lock, THEN subscribe for live.
        // Ordering note: a line buffered between the snapshot and Subscribe could
        // be missed; we subscribe FIRST, then replay, and de-dup by seq so the
        // client never sees a gap (live lines with seq <= last replayed are
        // already covered by the replay set; the client dedups on seq anyway).
        conn.Subscribe(msg.Handle!);

        if (!session!.TryReplay(msg.AfterSeq, out var lines, out var floorSeq))
        {
            conn.Send(new ReplayTruncatedMessage { handle = msg.Handle!, floorSeq = floorSeq });
            return;
        }

        foreach (var entry in lines)
        {
            conn.Send(new OutputMessage
            {
                handle = msg.Handle!,
                seq = entry.Seq,
                stream = entry.IsStderr ? StreamNames.Stderr : StreamNames.Stdout,
                line = entry.Line,
            });
        }

        // If the session already exited (e.g. its turn completed during a domain-reload gap while no
        // client was connected), the live Exited event fired to nobody — so re-send EXITED now, after the
        // replay, so this late re-subscriber learns the child is gone instead of waiting forever for a
        // result the (dead) live stream will never produce. Idempotent on the client (it de-dups EXITED).
        if (!session.IsAlive)
        {
            conn.Send(new ExitedMessage { handle = msg.Handle!, code = session.ExitCode });
        }
        // Otherwise live streaming resumes via the session's LineBuffered handler.
    }

    private void WireSession(ChildSession session)
    {
        session.LineBuffered += (s, entry) =>
        {
            var active = Volatile.Read(ref _active);
            if (active != null && active.IsSubscribed(s.Handle))
            {
                active.Send(new OutputMessage
                {
                    handle = s.Handle,
                    seq = entry.Seq,
                    stream = entry.IsStderr ? StreamNames.Stderr : StreamNames.Stdout,
                    line = entry.Line,
                });
            }
        };

        session.Exited += (s, code) =>
        {
            var active = Volatile.Read(ref _active);
            if (active != null && active.IsSubscribed(s.Handle))
                active.Send(new ExitedMessage { handle = s.Handle, code = code });
            // The session is kept in the dict so its buffer can still be replayed on reconnect and
            // reported (alive=false) in HELLO_OK — but bound how many exited sessions we retain.
            EvictExcessExitedSessions();
        };
    }

    /// <summary>
    /// Bound the number of retained EXITED sessions: evict the oldest-exited ones beyond
    /// <see cref="_maxRetainedExitedSessions"/> so a long-lived daemon cannot accumulate exited
    /// sessions (and their buffers) without limit. Live sessions are never evicted, and the session
    /// the current client is actively subscribed to is skipped (it may be mid-replay), so a reload's
    /// reattach target — always the newest — is never dropped out from under it.
    /// </summary>
    private void EvictExcessExitedSessions()
    {
        List<KeyValuePair<string, ChildSession>>? exited = null;
        foreach (var kvp in _sessions)
        {
            if (!kvp.Value.IsAlive)
                (exited ??= new List<KeyValuePair<string, ChildSession>>()).Add(kvp);
        }

        if (exited == null || exited.Count <= _maxRetainedExitedSessions)
            return;

        exited.Sort((x, y) => Nullable.Compare(x.Value.ExitedAtUtc, y.Value.ExitedAtUtc));

        var activeConn = Volatile.Read(ref _active);
        var toEvict = exited.Count - _maxRetainedExitedSessions;
        for (var i = 0; i < exited.Count && toEvict > 0; i++)
        {
            var handle = exited[i].Key;
            if (activeConn != null && activeConn.IsSubscribed(handle))
                continue; // don't evict a session the live client is attached to / replaying

            if (_sessions.TryRemove(handle, out var evicted))
            {
                evicted.Dispose();
                toEvict--;
            }
        }
    }

    private List<SessionSummary> SnapshotSessions()
    {
        var list = new List<SessionSummary>();
        foreach (var kvp in _sessions)
        {
            list.Add(new SessionSummary
            {
                handle = kvp.Key,
                alive = kvp.Value.IsAlive,
                lastSeq = kvp.Value.LastSeq,
            });
        }
        return list;
    }

    private bool TryGetSession(string? handle, out ChildSession? session)
    {
        session = null;
        return !string.IsNullOrEmpty(handle) && _sessions.TryGetValue(handle!, out session);
    }

    private void PromoteActiveConnection(ClientConnection conn)
    {
        ClientConnection? previous;
        lock (_activeLock)
        {
            previous = _active;
            _active = conn;
            _graceDeadlineUtc = null; // a client is connected → cancel grace
        }

        // Latest valid HELLO wins: tear down the prior connection (its children
        // are untouched — they belong to the daemon, not the socket).
        if (previous != null && !ReferenceEquals(previous, conn))
            previous.Close();
    }

    private void OnConnectionClosed(ClientConnection conn)
    {
        conn.Close();

        lock (_activeLock)
        {
            if (ReferenceEquals(_active, conn))
            {
                _active = null;
                ArmGraceLocked(); // last client gone → start the grace countdown
            }
        }
    }

    private void ArmGrace()
    {
        lock (_activeLock)
            ArmGraceLocked();
    }

    private void ArmGraceLocked()
    {
        if (_active != null)
        {
            _graceDeadlineUtc = null;
            return;
        }
        _graceDeadlineUtc = DateTime.UtcNow.AddSeconds(_options.GraceSeconds);
    }

    private async Task WatchdogLoopAsync()
    {
        while (!_cts.IsCancellationRequested)
        {
            try { await Task.Delay(250, _cts.Token).ConfigureAwait(false); }
            catch (OperationCanceledException) { break; }

            // Owner-pid watch (authoritative): if the Editor process that launched us is gone, stop NOW —
            // independent of grace or any "active" client. A domain reload keeps the Editor OS process
            // alive (only its AppDomain reloads), so this never false-positives on a reload; it fires only
            // on a real Editor exit/crash. Without it, an Editor killed while a client socket is still
            // half-open (the OS has not yet reaped the dead peer) would leave the daemon believing a client
            // is connected (grace never arms) and it would orphan forever.
            if (_options.OwnerPid > 0 && !IsProcessAlive(_options.OwnerPid))
            {
                Stop();
                break;
            }

            DateTime? deadline;
            bool hasClient;
            lock (_activeLock)
            {
                deadline = _graceDeadlineUtc;
                hasClient = _active != null;
            }

            if (hasClient)
                continue;

            if (deadline.HasValue && DateTime.UtcNow >= deadline.Value)
            {
                // Grace expired with no client → stop.
                Stop();
                break;
            }
        }
    }

    private static bool IsProcessAlive(int pid)
    {
        try
        {
            var p = Process.GetProcessById(pid);
            return !p.HasExited;
        }
        catch (ArgumentException) { return false; } // not running
        catch (InvalidOperationException) { return false; }
        catch (Exception) { return true; } // unknown → assume alive (don't kill prematurely)
    }

    /// <summary>
    /// Stop the daemon: gracefully stop every child (close stdin → wait → kill),
    /// delete discovery files, tear down the listener, and complete <see cref="Stopped"/>.
    /// Idempotent.
    /// </summary>
    public void Stop()
    {
        if (Interlocked.Exchange(ref _shuttingDown, 1) != 0)
            return;

        _cts.Cancel();

        try { _listener?.Stop(); } catch { /* ignore */ }

        lock (_activeLock)
        {
            _active?.Close();
            _active = null;
        }

        // Graceful child stop ladder, in parallel, bounded.
        var tasks = new List<Task>();
        foreach (var kvp in _sessions)
            tasks.Add(kvp.Value.InterruptAsync());
        try { Task.WaitAll(tasks.ToArray(), TimeSpan.FromSeconds(5)); }
        catch { /* ignore */ }

        foreach (var kvp in _sessions)
        {
            kvp.Value.Dispose();
        }
        _sessions.Clear();

        DiscoveryPaths.TryDelete(_options.PortFile);
        DiscoveryPaths.TryDelete(_options.TokenFile);
        DiscoveryPaths.TryDelete(_options.PidFile);

        _stopped.TrySetResult(true);
    }

    public void Dispose() => Stop();
}