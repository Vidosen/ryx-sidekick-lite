// SPDX-License-Identifier: GPL-3.0-only
using System;
using System.Diagnostics;
using System.Text;
using System.Threading.Tasks;
using Ryx.Sidekick.AgentHost.Protocol;

namespace Ryx.Sidekick.AgentHost;

/// <summary>
/// Owns one child CLI process and multiplexes its stdio. Ports the pipe-pump
/// model of the Unity-side CliProcessHost (OutputDataReceived -&gt; buffer; close
/// stdin then wait then kill on graceful stop) but is protocol-blind: it just
/// relays lines/bytes and buffers output with monotonic seqs.
///
/// Output handling: <see cref="Process.OutputDataReceived"/> /
/// <see cref="Process.ErrorDataReceived"/> fire on the runtime's thread pool.
/// We append under <see cref="_bufferLock"/> (so seqs stay monotonic across
/// both streams) and raise <see cref="LineBuffered"/> for the server to stream
/// live to the attached client.
/// </summary>
internal sealed class ChildSession : IDisposable
{
    private const int GracefulKillTimeoutMs = 3000;

    private readonly object _processLock = new();
    private readonly object _bufferLock = new();
    private readonly OutputBuffer _buffer;
    private readonly TaskCompletionSource<int> _exitTcs =
        new(TaskCreationOptions.RunContinuationsAsynchronously);

    private Process? _process;
    private volatile bool _started;
    private volatile bool _exited;
    private bool _stdinOpen;

    public ChildSession(string handle, int maxBufferLines, long maxBufferBytes)
    {
        Handle = handle;
        _buffer = new OutputBuffer(maxBufferLines, maxBufferBytes);
    }

    /// <summary>Opaque session id assigned by the server (GUID string).</summary>
    public string Handle { get; }

    public bool IsAlive => _started && !_exited;
    public int ExitCode { get; private set; } = -1;

    /// <summary>UTC time the child exited (null while alive). Orders retention eviction of exited sessions.</summary>
    public DateTime? ExitedAtUtc { get; private set; }

    public Task<int> ExitTask => _exitTcs.Task;

    /// <summary>Raised on the thread-pool when a child line is buffered.</summary>
    public event Action<ChildSession, BufferedLine>? LineBuffered;

    /// <summary>Raised when the child exits, carrying its exit code.</summary>
    public event Action<ChildSession, int>? Exited;

    public long LastSeq
    {
        get { lock (_bufferLock) return _buffer.LastSeq; }
    }

    public long FloorSeq
    {
        get { lock (_bufferLock) return _buffer.FloorSeq; }
    }

    /// <summary>
    /// Spawn the child from a spec. env is merged over a minimal inherited env,
    /// with TERM=dumb / NO_COLOR=1 forced (matching CliProcessHost). UTF-8 in/out,
    /// UseShellExecute=false, all three streams redirected.
    /// </summary>
    public bool Start(SpawnSpec spec)
    {
        if (_started)
            return false;

        var startInfo = new ProcessStartInfo
        {
            FileName = spec.filename,
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            StandardOutputEncoding = Encoding.UTF8,
            StandardErrorEncoding = Encoding.UTF8,
            StandardInputEncoding = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false),
        };

        // Prefer the pre-built command-line string (what the in-process CliProcessHost passes to
        // Process.Start verbatim) so the daemon never re-tokenizes — single source of truth for the
        // child's args is the Unity-side ICliPlatform. Fall back to the token list only when absent.
        if (!string.IsNullOrEmpty(spec.commandLine))
        {
            startInfo.Arguments = spec.commandLine;
        }
        else if (spec.args != null)
        {
            foreach (var arg in spec.args)
                startInfo.ArgumentList.Add(arg ?? "");
        }

        if (!string.IsNullOrEmpty(spec.workingDir))
            startInfo.WorkingDirectory = spec.workingDir;

        // env merged OVER the daemon's own (minimal) inherited env.
        if (spec.env != null)
        {
            foreach (var kvp in spec.env)
                startInfo.EnvironmentVariables[kvp.Key] = kvp.Value;
        }

        startInfo.EnvironmentVariables["TERM"] = "dumb";
        startInfo.EnvironmentVariables["NO_COLOR"] = "1";

        try
        {
            lock (_processLock)
            {
                _process = new Process { StartInfo = startInfo, EnableRaisingEvents = true };
                _process.OutputDataReceived += (_, e) => OnData(e, isStderr: false);
                _process.ErrorDataReceived += (_, e) => OnData(e, isStderr: true);
                _process.Exited += OnProcessExited;

                _started = true;
                _process.Start();
                _stdinOpen = true;
                _process.BeginOutputReadLine();
                _process.BeginErrorReadLine();
            }

            return true;
        }
        catch (Exception)
        {
            _started = false;
            CleanupProcess();
            return false;
        }
    }

    /// <summary>
    /// Write text to the child's stdin. Mirrors CliProcessHost.WriteToStdin:
    /// appendNewline writes a trailing newline. Returns false if stdin is closed
    /// or the child has exited.
    /// </summary>
    public bool Write(string text, bool appendNewline)
    {
        lock (_processLock)
        {
            if (_process is not { HasExited: false } || !_stdinOpen)
                return false;

            try
            {
                if (appendNewline)
                    _process.StandardInput.WriteLine(text);
                else
                    _process.StandardInput.Write(text);
                _process.StandardInput.Flush();
                return true;
            }
            catch (Exception)
            {
                return false;
            }
        }
    }

    /// <summary>Close the child's stdin without killing it.</summary>
    public bool CloseStdin()
    {
        lock (_processLock)
        {
            if (_process is not { HasExited: false } || !_stdinOpen)
                return false;

            try
            {
                _process.StandardInput.Close();
                return true;
            }
            catch (Exception)
            {
                return false;
            }
            finally
            {
                _stdinOpen = false;
            }
        }
    }

    /// <summary>Force-kill the child (entire process tree). Synchronous.</summary>
    public void Stop()
    {
        lock (_processLock)
        {
            if (_process is { HasExited: false })
            {
                try { _process.StandardInput.Close(); } catch { /* ignore */ }
                _stdinOpen = false;
                try { _process.Kill(entireProcessTree: true); } catch { /* ignore */ }
            }
        }
    }

    /// <summary>
    /// Graceful interrupt: close stdin, wait up to 3s for the child to exit on
    /// its own, then force-kill if still running (CliProcessHost.InterruptAsync).
    /// </summary>
    public async Task InterruptAsync()
    {
        if (!IsAlive)
            return;

        try
        {
            CloseStdin();

            var completed = await Task.WhenAny(_exitTcs.Task, Task.Delay(GracefulKillTimeoutMs))
                .ConfigureAwait(false);

            if (completed != _exitTcs.Task)
            {
                lock (_processLock)
                {
                    if (_process is { HasExited: false })
                    {
                        try { _process.Kill(entireProcessTree: true); } catch { /* ignore */ }
                    }
                }
            }
        }
        catch (Exception)
        {
            /* swallow — best-effort shutdown */
        }
    }

    /// <summary>Durable-trim the buffer up to safeSeq (see <see cref="OutputBuffer"/>).</summary>
    public void Trim(long safeSeq)
    {
        lock (_bufferLock)
            _buffer.Trim(safeSeq);
    }

    /// <summary>
    /// Atomically capture a replay snapshot. Returns false (and sets floorSeq)
    /// when afterSeq is below the buffer floor — the caller sends REPLAY_TRUNCATED.
    /// The snapshot is taken under the buffer lock so it is internally consistent;
    /// the server resumes live streaming from the session's LineBuffered event.
    /// </summary>
    public bool TryReplay(long afterSeq, out System.Collections.Generic.List<BufferedLine> lines, out long floorSeq)
    {
        lock (_bufferLock)
        {
            floorSeq = _buffer.FloorSeq;
            if (!_buffer.CanReplayFrom(afterSeq))
            {
                lines = new System.Collections.Generic.List<BufferedLine>();
                return false;
            }

            lines = _buffer.Replay(afterSeq);
            return true;
        }
    }

    public void Dispose()
    {
        Stop();
        CleanupProcess();
    }

    private void OnData(DataReceivedEventArgs e, bool isStderr)
    {
        if (e.Data == null)
            return; // EOF marker from BeginOutput/ErrorReadLine

        BufferedLine entry;
        lock (_bufferLock)
        {
            var seq = _buffer.Append(isStderr, e.Data);
            entry = new BufferedLine(seq, isStderr, e.Data);
        }

        LineBuffered?.Invoke(this, entry);
    }

    private void OnProcessExited(object? sender, EventArgs e)
    {
        int code;
        try { code = _process?.ExitCode ?? -1; }
        catch { code = -1; }

        ExitCode = code;
        ExitedAtUtc = DateTime.UtcNow;
        _exited = true;
        _stdinOpen = false;

        _exitTcs.TrySetResult(code);
        Exited?.Invoke(this, code);
    }

    private void CleanupProcess()
    {
        lock (_processLock)
        {
            if (_process == null)
                return;

            try
            {
                _process.Exited -= OnProcessExited;
                if (!_process.HasExited)
                    _process.Kill(entireProcessTree: true);
            }
            catch { /* ignore */ }

            try { _process.Dispose(); } catch { /* ignore */ }
            _process = null;
        }
    }
}