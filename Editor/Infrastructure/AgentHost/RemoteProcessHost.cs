// SPDX-License-Identifier: GPL-3.0-only
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using UnityEditor;
using Debug = UnityEngine.Debug;

namespace Ryx.Sidekick.Editor
{
    /// <summary>
    /// IPC client implementation of <see cref="IProcessHost"/> that proxies the seam to an
    /// out-of-process Sidekick Agent Host daemon over a loopback TCP socket (JSON-lines, UTF-8,
    /// one object per line, <c>t</c> discriminator — Phase 1 protocol).
    ///
    /// <para>
    /// The whole point: incoming <c>OUTPUT</c> frames are re-emitted as
    /// <see cref="OnOutputLine"/>/<see cref="OnErrorLine"/>, <c>STARTED</c> as
    /// <see cref="OnProcessStarted"/>, and <c>EXITED</c> as <see cref="OnProcessExited"/>, so every
    /// consumer ABOVE the seam (<c>ProcessManager</c>, the stream parsers,
    /// <c>ControlRequestHandler</c>) stays byte-for-byte unchanged. Events are marshaled onto the
    /// Editor main thread via an <see cref="EditorApplication.update"/>-pumped queue — exactly like
    /// <see cref="CliProcessHost"/> — never raised on the socket-reader thread.
    /// </para>
    ///
    /// <para>
    /// Phase 2 derives the daemon spawn-spec from the same launch info <see cref="CliProcessHost"/>
    /// uses (<see cref="SidekickSettings.CreateProcessStartInfo"/>); Phase 4 will supply the full
    /// platform-resolved spawn-spec built by <c>ICliPlatform</c>.
    /// </para>
    /// </summary>
    internal sealed class RemoteProcessHost : IReconnectableProcessHost
    {
        private const int OutputEventsPerPump = 16;

        // Safety valve for the in-order reassembler in HandleOutput. The daemon assigns strictly
        // contiguous seqs, so a gap always fills within the same burst; this only bounds memory and
        // prevents a permanently-stalled delivery if a gap were somehow never filled.
        private const int MaxPendingReorder = 4096;

        public event Action<string> OnOutputLine;
        public event Action<string> OnErrorLine;
        public event Action OnProcessStarted;
        public event Action<int> OnProcessExited;

        private readonly IAgentHostConnector _connector;

        // Pending main-thread events, pumped FIFO on EditorApplication.update to preserve ordering
        // across OUTPUT/STARTED/EXITED (CliProcessHost splits these across update + delayCall, but a
        // single ordered queue is the faithful analogue for a single multiplexed socket stream).
        private readonly ConcurrentQueue<Action> _mainThreadQueue = new();
        private readonly object _pumpLock = new();
        private readonly object _connectionLock = new();
        private readonly object _seqLock = new();

        private TcpClient _client;
        private NetworkStream _stream;
        private StreamReader _reader;
        private Thread _readerThread;
        private CancellationTokenSource _cts;

        private volatile bool _connected;
        private volatile bool _running;
        private volatile bool _stdinOpen;
        private volatile bool _detached;
        private bool _pumpScheduled;

        private string _sessionHandle = string.Empty;
        private long _lastObservedSeq;
        // Out-of-order OUTPUT frames held until their lower-seq predecessors arrive (guarded by _seqLock).
        private readonly SortedDictionary<long, OutputFrame> _pendingBySeq = new();
        private TaskCompletionSource<int> _exitTcs;

        public RemoteProcessHost(IAgentHostConnector connector)
        {
            _connector = connector ?? new UnavailableAgentHostConnector();
        }

        public bool IsRunning => _running;
        public bool IsStdinOpen => _stdinOpen;
        public string SessionHandle => _sessionHandle;

        public long LastObservedSequence
        {
            get { lock (_seqLock) return _lastObservedSeq; }
        }

        /// <summary>
        /// Start a streaming CLI session on the daemon. Connects + authenticates if needed, then
        /// sends START with a spawn-spec derived from the same settings <see cref="CliProcessHost"/>
        /// uses, and waits (bounded) for the STARTED handshake.
        /// </summary>
        public bool StartStreaming(string arguments)
        {
            _detached = false;

            if (_running)
            {
                Stop();
                Cleanup();
            }

            // A fresh streaming session must NOT inherit the previous session's handle/seq state — this
            // instance is reused across turns (the persistent Claude path keeps one host alive), and a
            // stale handle would collide with the daemon's retained session, while a stale seq would
            // make the new session's low-seq output look already-observed and get dropped.
            ResetSessionState();

            if (!EnsureConnected())
            {
                Debug.LogError("[RemoteProcessHost] Cannot start: no Agent Host daemon connection.");
                return false;
            }

            JObject spec;
            try
            {
                spec = BuildSpawnSpec(arguments);
            }
            catch (Exception ex)
            {
                Debug.LogException(ex);
                return false;
            }

            _exitTcs = new TaskCompletionSource<int>(TaskCreationOptions.RunContinuationsAsynchronously);
            _exitReported = false;

            // STARTED arrives asynchronously over the socket; gate the synchronous bool return on it
            // so callers see the same contract as CliProcessHost (true == process is up). A daemon
            // ERROR (bad spec, handle collision, spawn failure) also releases the wait so a failed
            // start fails FAST instead of blocking the full timeout.
            var startedSignal = new ManualResetEventSlim(false);
            var startedHandle = string.Empty;
            string startError = null;
            void OnStarted(string handle)
            {
                startedHandle = handle;
                startedSignal.Set();
            }
            void OnStartError(string message)
            {
                startError = message;
                startedSignal.Set();
            }

            _startedCallback = OnStarted;
            _startErrorCallback = OnStartError;

            // StartStreaming always begins a FRESH session: the daemon assigns the handle (returned in
            // STARTED). Reconnect to an existing session is TryAttach's job — sending no handle here
            // (combined with ResetSessionState above) guarantees no stale handle is re-sent.
            var start = new JObject
            {
                ["t"] = WireProtocol.Start,
                ["spec"] = spec
            };

            if (!SendFrame(start))
            {
                _startedCallback = null;
                _startErrorCallback = null;
                return false;
            }

            var signaled = startedSignal.Wait(TimeSpan.FromSeconds(10));
            _startedCallback = null;
            _startErrorCallback = null;

            if (startError != null)
            {
                Debug.LogError($"[RemoteProcessHost] Daemon rejected START: {startError}");
                return false;
            }

            if (!signaled || string.IsNullOrEmpty(startedHandle))
            {
                Debug.LogError("[RemoteProcessHost] Timed out waiting for STARTED from daemon.");
                return false;
            }

            _sessionHandle = startedHandle;
            _running = true;
            _stdinOpen = true;

            // OnProcessStarted is fired by CliProcessHost synchronously inside StartStreaming; mirror
            // that here (we are already on the main thread when StartStreaming is called).
            OnProcessStarted?.Invoke();
            return true;
        }

        public bool WriteLineToStdin(string line)
        {
            return WriteToStdin(line, appendNewLine: true);
        }

        public bool WriteToStdin(string text, bool appendNewLine = false)
        {
            if (!_connected || !_stdinOpen || string.IsNullOrEmpty(_sessionHandle))
            {
                return false;
            }

            var write = new JObject
            {
                ["t"] = WireProtocol.Write,
                ["handle"] = _sessionHandle,
                ["data"] = text ?? string.Empty,
                ["appendNewline"] = appendNewLine
            };
            return SendFrame(write);
        }

        public void CloseStdin()
        {
            TryCloseStdin();
        }

        public bool TryCloseStdin()
        {
            if (!_connected || !_stdinOpen || string.IsNullOrEmpty(_sessionHandle))
            {
                return false;
            }

            var ok = SendFrame(new JObject
            {
                ["t"] = WireProtocol.CloseStdin,
                ["handle"] = _sessionHandle
            });
            _stdinOpen = false;
            return ok;
        }

        public void Stop()
        {
            // Phase 3 crux: a domain-reload teardown reaches Stop() through the same dispose chain as a
            // user stop (ProcessManager.Dispose → Stop → host.Dispose → Stop). For a RemoteProcessHost
            // those two must diverge: a reload must DETACH (keep the daemon child alive so the next
            // domain re-attaches), a user stop must STOP (kill). DomainReloadAutoResume.OnBeforeAssemblyReload
            // sets the reload flag BEFORE OnDisable, so it is observable here for the whole teardown.
            if (AgentHostReloadCoordinator.IsReloadTeardownInProgress)
            {
                Detach();
                return;
            }

            // Explicit user stop / real window close: terminate the daemon-owned child.
            if (_connected && !string.IsNullOrEmpty(_sessionHandle))
            {
                SendFrame(new JObject
                {
                    ["t"] = WireProtocol.Stop,
                    ["handle"] = _sessionHandle
                });
            }

            DrainMainThreadQueue();
            UnschedulePump();
            _running = false;
            _stdinOpen = false;
        }

        /// <summary>
        /// Detach from the daemon without stopping the session: close the socket only. Sends NO
        /// STOP/INTERRUPT/SHUTDOWN frame, so the daemon keeps the child process alive for re-attach by
        /// the next domain. Idempotent — a second call (e.g. the later Dispose in the reload chain) is a
        /// no-op once the connection is already closed.
        /// </summary>
        public void Detach()
        {
            _detached = true;
            DrainMainThreadQueue();
            UnschedulePump();
            _running = false;
            _stdinOpen = false;
            VerboseLog($"[AgentHost] Detaching from daemon (session={_sessionHandle}); child kept alive for re-attach.");
            // Closing the socket leaves the daemon session untouched; only the transport goes away.
            CloseConnection();
            // The session handle is intentionally preserved on this instance, but the durable reconnect
            // keys are persisted separately by Phase 3's resume store — this instance is discarded with
            // the AppDomain, and the next domain re-attaches via a fresh RemoteProcessHost.
        }

        public void SendTrim(long safeSeq)
        {
            if (!_connected || string.IsNullOrEmpty(_sessionHandle) || safeSeq <= 0)
            {
                return;
            }

            SendFrame(new JObject
            {
                ["t"] = WireProtocol.Trim,
                ["handle"] = _sessionHandle,
                ["safeSeq"] = safeSeq
            });
        }

        public async Task InterruptAsync()
        {
            if (!_running || !_connected || string.IsNullOrEmpty(_sessionHandle))
            {
                return;
            }

            SendFrame(new JObject
            {
                ["t"] = WireProtocol.Interrupt,
                ["handle"] = _sessionHandle
            });

            try
            {
                var exitTask = _exitTcs?.Task ?? Task.CompletedTask;
                await Task.WhenAny(exitTask, Task.Delay(3000));
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"[RemoteProcessHost] Error during interrupt: {ex.Message}");
            }
            finally
            {
                Cleanup();
            }
        }

        public void Cleanup()
        {
            _stdinOpen = false;
            _running = false;
            UnschedulePump();
            DrainMainThreadQueue();
            CloseConnection();
            _exitTcs = null;
            ResetSessionState();
        }

        // Clears per-session state so a reused host instance (the persistent Claude path keeps one host
        // alive across turns) starts the next session clean: a stale handle would collide with the
        // daemon's retained session and a stale seq high-water mark would drop the new session's output.
        private void ResetSessionState()
        {
            _sessionHandle = string.Empty;
            _exitReported = false;
            lock (_seqLock)
            {
                _lastObservedSeq = 0;
                _pendingBySeq.Clear();
            }
        }

        public void Dispose()
        {
            // If we already detached (the reload-teardown path ran via Stop()), Dispose must stay a
            // no-op: the socket is closed and the daemon session is deliberately left alive. Calling
            // Stop()/Cleanup() again would not send a STOP (Detach already closed the connection, so
            // SendFrame would fail), but short-circuiting makes the intent explicit and avoids any
            // chance of resurrecting and then tearing the connection down.
            if (_detached)
            {
                return;
            }

            Stop();

            // Stop() may have detached (reload in progress) — respect that and do not run Cleanup,
            // which is harmless but pointless once detached.
            if (_detached)
            {
                return;
            }

            Cleanup();
        }

        /// <summary>
        /// Re-attach to an existing daemon session and replay buffered output from
        /// <paramref name="afterSequence"/>. Connects + authenticates if needed. De-dups replayed
        /// seqs against live. Returns false on REPLAY_TRUNCATED or a missing session so the caller
        /// can fall back to <c>-r</c> resume.
        /// </summary>
        public bool TryAttach(string sessionHandle, long afterSequence)
        {
            if (string.IsNullOrEmpty(sessionHandle))
            {
                return false;
            }

            _detached = false;

            if (!EnsureConnected())
            {
                return false;
            }

            lock (_seqLock)
            {
                _sessionHandle = sessionHandle;
                _lastObservedSeq = afterSequence;
                _pendingBySeq.Clear();
            }

            // Arm exit tracking BEFORE sending ATTACH. The daemon re-sends EXITED on attach to a session
            // that already exited (its turn completed during the reload gap), and that EXITED can arrive
            // on the reader thread DURING the bounded wait below. Arming first means HandleExited resolves
            // THIS _exitTcs and flips _running/_exitReported; the post-wait state set must then not
            // resurrect a dead session.
            _exitTcs = new TaskCompletionSource<int>(TaskCreationOptions.RunContinuationsAsynchronously);
            _exitReported = false;

            var truncatedSignal = new ManualResetEventSlim(false);
            var truncated = false;
            string attachError = null;
            void OnTruncated(string handle, long floorSeq)
            {
                if (string.Equals(handle, sessionHandle, StringComparison.Ordinal))
                {
                    truncated = true;
                    truncatedSignal.Set();
                }
            }
            // A daemon ERROR during attach — most importantly "no such session" (the session was evicted,
            // the daemon restarted, or a version-skew drain replaced it) — must be treated as attach
            // FAILURE so the caller falls back to -r resume, not reported as a phantom running turn. It
            // reuses the start/attach error-callback slot and wakes the same bounded wait.
            void OnAttachError(string message)
            {
                attachError = message;
                truncatedSignal.Set();
            }

            _replayTruncatedCallback = OnTruncated;
            _startErrorCallback = OnAttachError;

            var attach = new JObject
            {
                ["t"] = WireProtocol.Attach,
                ["handle"] = sessionHandle,
                ["afterSeq"] = afterSequence
            };

            if (!SendFrame(attach))
            {
                _replayTruncatedCallback = null;
                _startErrorCallback = null;
                return false;
            }

            // The daemon's ATTACH response is EITHER an immediate REPLAY_TRUNCATED (sent before any
            // replay, when afterSeq is below the floor) OR a stream of replay OUTPUTs — never both. So
            // a short bounded wait for the NACK is sufficient: its absence means the attach was
            // accepted and replay/live is flowing. (Phase 3 reworks attach into a fully async flow so
            // the main thread never blocks on reconnect.)
            var gotTruncated = truncatedSignal.Wait(TimeSpan.FromMilliseconds(750));
            _replayTruncatedCallback = null;
            _startErrorCallback = null;

            if (gotTruncated && truncated)
            {
                _running = false;
                _stdinOpen = false;
                VerboseLog($"[AgentHost] Re-attach to session {sessionHandle} got REPLAY_TRUNCATED; caller falls back to -r resume.");
                return false;
            }

            if (attachError != null)
            {
                _running = false;
                _stdinOpen = false;
                VerboseLog($"[AgentHost] Re-attach to session {sessionHandle} failed: {attachError}; caller falls back to -r resume.");
                return false;
            }

            // Do NOT resurrect a session that already replayed EXITED during the wait above (its turn
            // finished during the reload gap → the daemon re-sends EXITED on attach). In that case the
            // reader thread has already flipped _running=false / _exitReported=true and completed
            // _exitTcs; clobbering _running=true here would make the host report a phantom running
            // process for a dead child, breaking the next turn.
            if (!_exitReported)
            {
                _running = true;
                _stdinOpen = true;
            }
            VerboseLog($"[AgentHost] Re-attached to session {sessionHandle}; replaying from seq {afterSequence}.");
            return true;
        }

        // === connection / handshake ===

        private bool EnsureConnected()
        {
            lock (_connectionLock)
            {
                if (_connected)
                {
                    return true;
                }

                if (!_connector.TryConnect(out var endpoint) || !endpoint.IsValid)
                {
                    return false;
                }

                try
                {
                    _client = new TcpClient { NoDelay = true };
                    _client.Connect(endpoint.Host, endpoint.Port);
                    _stream = _client.GetStream();
                    _reader = new StreamReader(_stream, new UTF8Encoding(false), false, 4096, leaveOpen: true);
                }
                catch (Exception ex)
                {
                    Debug.LogError($"[RemoteProcessHost] Failed to connect to daemon {endpoint.Host}:{endpoint.Port}: {ex.Message}");
                    CloseConnectionLocked();
                    return false;
                }

                if (!PerformHandshake(endpoint))
                {
                    CloseConnectionLocked();
                    return false;
                }

                _connected = true;
                _cts = new CancellationTokenSource();
                _readerThread = new Thread(ReaderLoop) { IsBackground = true, Name = "SidekickRemoteProcessHostReader" };
                _readerThread.Start();
                VerboseLog($"[AgentHost] Connected to daemon {endpoint.Host}:{endpoint.Port}.");
                return true;
            }
        }

        // Verbose-gated info log. Errors/warnings elsewhere always log; routine connect/detach/attach
        // transitions are gated on SidekickSettings.VerboseLogging so the feature is quiet by default.
        private static void VerboseLog(string message)
        {
            try
            {
                if (SidekickSettings.instance.VerboseLogging)
                    Debug.Log(message);
            }
            catch { /* settings unavailable in some test contexts — stay quiet */ }
        }

        private bool PerformHandshake(AgentHostEndpoint endpoint)
        {
            var hello = new JObject
            {
                ["t"] = WireProtocol.Hello,
                ["token"] = endpoint.Token ?? string.Empty,
                ["proto"] = WireProtocol.ProtocolVersion,
                ["ownerPid"] = endpoint.OwnerPid
            };

            if (!WriteLineRaw(hello.ToString(Formatting.None)))
            {
                return false;
            }

            // The daemon replies HELLO_OK (or an ERROR / silent close on bad token / proto mismatch).
            string response;
            try
            {
                response = _reader.ReadLine();
            }
            catch (Exception ex)
            {
                Debug.LogError($"[RemoteProcessHost] Handshake read failed: {ex.Message}");
                return false;
            }

            if (string.IsNullOrEmpty(response))
            {
                return false;
            }

            JObject parsed;
            try
            {
                parsed = JObject.Parse(response);
            }
            catch (JsonException)
            {
                return false;
            }

            var type = (string)parsed["t"];
            if (!string.Equals(type, WireProtocol.HelloOk, StringComparison.Ordinal))
            {
                if (string.Equals(type, WireProtocol.Error, StringComparison.Ordinal))
                {
                    Debug.LogError($"[RemoteProcessHost] Daemon rejected HELLO: {(string)parsed["message"]}");
                }
                return false;
            }

            return true;
        }

        // === socket reader (background thread) ===

        private void ReaderLoop()
        {
            var token = _cts?.Token ?? CancellationToken.None;
            try
            {
                while (!token.IsCancellationRequested)
                {
                    string line;
                    try
                    {
                        line = _reader.ReadLine();
                    }
                    catch
                    {
                        break;
                    }

                    if (line == null)
                    {
                        break; // daemon closed the connection
                    }

                    HandleIncomingLine(line);
                }
            }
            finally
            {
                // A dropped connection while running is reported as an exit so the stack above the
                // seam tears the turn down (mirrors CliProcessHost's process-exit path). Guard against
                // double-firing when the daemon sent EXITED right before closing the socket: the flag
                // is flipped eagerly on this thread in HandleExited, so we won't re-report here.
                if (_running && !_exitReported)
                {
                    _exitReported = true;
                    _running = false;
                    _stdinOpen = false;
                    EnqueueMainThread(() =>
                    {
                        _exitTcs?.TrySetResult(-1);
                        OnProcessExited?.Invoke(-1);
                    });
                }
            }
        }

        private void HandleIncomingLine(string line)
        {
            if (string.IsNullOrWhiteSpace(line))
            {
                return;
            }

            JObject msg;
            try
            {
                msg = JObject.Parse(line);
            }
            catch (JsonException)
            {
                return;
            }

            var type = (string)msg["t"];
            switch (type)
            {
                case WireProtocol.Output:
                    HandleOutput(msg);
                    break;

                case WireProtocol.Started:
                    _startedCallback?.Invoke((string)msg["handle"] ?? string.Empty);
                    break;

                case WireProtocol.Exited:
                    HandleExited(msg);
                    break;

                case WireProtocol.ReplayTruncated:
                    _replayTruncatedCallback?.Invoke((string)msg["handle"] ?? string.Empty,
                        (long?)msg["floorSeq"] ?? 0L);
                    break;

                case WireProtocol.Pong:
                    break;

                case WireProtocol.Error:
                    var errorText = (string)msg["message"] ?? "unknown daemon error";
                    // If a START is in flight, release its wait so it fails fast (see StartStreaming).
                    _startErrorCallback?.Invoke(errorText);
                    EnqueueMainThread(() => OnErrorLine?.Invoke(errorText));
                    break;
            }
        }

        private void HandleOutput(JObject msg)
        {
            var seq = (long?)msg["seq"] ?? 0L;
            var stream = (string)msg["stream"] ?? WireProtocol.StreamStdout;
            var text = (string)msg["line"] ?? string.Empty;
            var isStderr = string.Equals(stream, WireProtocol.StreamStderr, StringComparison.Ordinal);

            // Unsequenced frame (seq 0): nothing to order against — deliver as-is.
            if (seq == 0)
            {
                DeliverOutput(isStderr, text);
                return;
            }

            // In-order reassembly. The daemon assigns strictly contiguous seqs (shared across
            // stdout+stderr), but frames can reach us out of order: the daemon's ATTACH replay loop and
            // its live LineBuffered handler race for the connection write lock, and its stdout/stderr
            // pumps race each other. A high-water-mark dedup would silently DROP the lower-seq straggler
            // (losing replayed/streamed lines), so we deliver strictly in seq order and hold gaps until
            // their predecessors arrive. A seq we've already delivered is a genuine duplicate (the
            // replay set overlaps the live stream) and is dropped.
            List<OutputFrame> ready = null;
            lock (_seqLock)
            {
                if (seq <= _lastObservedSeq)
                {
                    return; // duplicate (replay ∩ live overlap)
                }

                if (seq == _lastObservedSeq + 1)
                {
                    ready = new List<OutputFrame> { new OutputFrame(isStderr, text) };
                    _lastObservedSeq = seq;

                    // Drain any contiguous successors that arrived early.
                    while (_pendingBySeq.TryGetValue(_lastObservedSeq + 1, out var next))
                    {
                        _pendingBySeq.Remove(_lastObservedSeq + 1);
                        ready.Add(next);
                        _lastObservedSeq++;
                    }
                }
                else
                {
                    // Gap: hold until the missing predecessor(s) arrive (SortedDictionary keeps order).
                    _pendingBySeq[seq] = new OutputFrame(isStderr, text);

                    if (_pendingBySeq.Count > MaxPendingReorder)
                    {
                        // Pathological never-filled gap: flush everything held, in seq order, so the
                        // buffer cannot grow without bound or stall delivery forever.
                        ready = new List<OutputFrame>(_pendingBySeq.Count);
                        foreach (var kvp in _pendingBySeq)
                        {
                            ready.Add(kvp.Value);
                            _lastObservedSeq = kvp.Key;
                        }
                        _pendingBySeq.Clear();
                    }
                }
            }

            if (ready == null)
            {
                return;
            }

            // Deliver outside the lock; the reader thread is single, so emission order is preserved.
            foreach (var frame in ready)
            {
                DeliverOutput(frame.IsStderr, frame.Text);
            }
        }

        private void DeliverOutput(bool isStderr, string text)
        {
            if (isStderr)
            {
                EnqueueMainThread(() => OnErrorLine?.Invoke(text));
            }
            else
            {
                EnqueueMainThread(() => OnOutputLine?.Invoke(text));
            }
        }

        private void HandleExited(JObject msg)
        {
            // Eagerly mark exit-reported on the reader thread so the reader-loop finally does not also
            // synthesize a connection-drop exit (the daemon typically closes the socket right after).
            if (_exitReported)
            {
                return;
            }
            _exitReported = true;
            _running = false;
            _stdinOpen = false;

            var code = (int?)msg["code"] ?? -1;
            EnqueueMainThread(() =>
            {
                _exitTcs?.TrySetResult(code);
                OnProcessExited?.Invoke(code);
            });
        }

        // STARTED / ERROR / REPLAY_TRUNCATED are correlated synchronously inside StartStreaming/TryAttach
        // via these volatile callbacks (set on the calling thread, invoked on the reader thread).
        private volatile Action<string> _startedCallback;
        private volatile Action<string> _startErrorCallback;
        private volatile Action<string, long> _replayTruncatedCallback;
        private volatile bool _exitReported;

        // A buffered OUTPUT frame awaiting in-order delivery (see HandleOutput's reassembler).
        private readonly struct OutputFrame
        {
            public readonly bool IsStderr;
            public readonly string Text;

            public OutputFrame(bool isStderr, string text)
            {
                IsStderr = isStderr;
                Text = text;
            }
        }

        // === main-thread pump (mirrors CliProcessHost) ===

        private void EnqueueMainThread(Action action)
        {
            _mainThreadQueue.Enqueue(action);
            EnsurePumpScheduled();
        }

        private void PumpMainThreadQueue()
        {
            var processed = 0;
            while (processed < OutputEventsPerPump && _mainThreadQueue.TryDequeue(out var action))
            {
                try { action(); }
                catch (Exception ex) { Debug.LogException(ex); }
                processed++;
            }

            if (!_mainThreadQueue.IsEmpty)
            {
                return;
            }
            UnschedulePump();
        }

        private void DrainMainThreadQueue()
        {
            while (_mainThreadQueue.TryDequeue(out _)) { }
        }

        private void EnsurePumpScheduled()
        {
            lock (_pumpLock)
            {
                if (_pumpScheduled)
                {
                    return;
                }
                _pumpScheduled = true;
                EditorApplication.update += PumpMainThreadQueue;
            }
        }

        private void UnschedulePump()
        {
            lock (_pumpLock)
            {
                if (!_pumpScheduled)
                {
                    return;
                }
                EditorApplication.update -= PumpMainThreadQueue;
                _pumpScheduled = false;
            }
        }

        // === wire write helpers ===

        private bool SendFrame(JObject frame)
        {
            return WriteLineRaw(frame.ToString(Formatting.None));
        }

        private bool WriteLineRaw(string jsonLine)
        {
            NetworkStream stream;
            lock (_connectionLock)
            {
                stream = _stream;
            }

            if (stream == null)
            {
                return false;
            }

            try
            {
                var bytes = Encoding.UTF8.GetBytes(jsonLine + "\n");
                lock (_connectionLock)
                {
                    if (_stream == null)
                    {
                        return false;
                    }
                    _stream.Write(bytes, 0, bytes.Length);
                    _stream.Flush();
                }
                return true;
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"[RemoteProcessHost] Wire write failed: {ex.Message}");
                return false;
            }
        }

        private void CloseConnection()
        {
            lock (_connectionLock)
            {
                CloseConnectionLocked();
            }
        }

        private void CloseConnectionLocked()
        {
            _connected = false;
            try { _cts?.Cancel(); } catch { /* ignore */ }
            try { _reader?.Dispose(); } catch { /* ignore */ }
            try { _stream?.Dispose(); } catch { /* ignore */ }
            try { _client?.Close(); } catch { /* ignore */ }
            _reader = null;
            _stream = null;
            _client = null;
            _cts = null;
        }

        /// <summary>
        /// Builds the daemon spawn-spec from the SAME fully platform-resolved launch info the in-process
        /// <see cref="CliProcessHost"/> consumes: <see cref="SidekickSettings.CreateProcessStartInfo"/> →
        /// <c>ICliProvider.CreateProcessStartInfo</c> → <c>ICliPlatform.CreateProcessStartInfo</c>
        /// (<c>MacOSCliPlatform</c>/<c>WindowsICliPlatform</c>), which already bakes in PATH/profile,
        /// nvm-resolved node, the <c>bash -l -c</c> wrapper, and Windows <c>.cmd</c> shims. So the
        /// daemon's child resolves byte-for-byte identically to the in-process child — one source of truth.
        ///
        /// <para>We send the resolved <c>filename</c> + the raw <c>commandLine</c> string (assigned VERBATIM
        /// to the daemon's <c>ProcessStartInfo.Arguments</c>, exactly as <c>CliProcessHost</c> does) so the
        /// daemon never re-tokenizes the argument string — avoiding a re-tokenization mismatch for either
        /// the direct-exec form (one giant literal arg) or the <c>bash -l -c "..."</c> wrapper form.</para>
        /// </summary>
        private static JObject BuildSpawnSpec(string arguments)
        {
            var settings = SidekickSettings.instance;
            var startInfo = settings.CreateProcessStartInfo(arguments);

            var env = new JObject();
            try
            {
                foreach (System.Collections.DictionaryEntry kvp in startInfo.EnvironmentVariables)
                {
                    var key = kvp.Key?.ToString();
                    if (string.IsNullOrEmpty(key))
                    {
                        continue;
                    }
                    env[key] = kvp.Value?.ToString() ?? string.Empty;
                }
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"[RemoteProcessHost] Failed to capture env for spawn-spec: {ex.Message}");
            }

            // Force the same non-color/dumb-terminal env CliProcessHost applies for streaming.
            env["TERM"] = "dumb";
            env["NO_COLOR"] = "1";

            return new JObject
            {
                ["filename"] = startInfo.FileName ?? string.Empty,
                // The whole argument string, verbatim — the daemon assigns it to ProcessStartInfo.Arguments
                // (single source of truth; no re-tokenization). args[] is left unset (back-compat only).
                ["commandLine"] = startInfo.Arguments ?? string.Empty,
                ["workingDir"] = string.IsNullOrEmpty(startInfo.WorkingDirectory) ? null : startInfo.WorkingDirectory,
                ["env"] = env
            };
        }
    }
}
