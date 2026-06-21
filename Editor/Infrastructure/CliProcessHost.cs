// SPDX-License-Identifier: GPL-3.0-only
using System;
using System.Collections.Concurrent;
using System.Diagnostics;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using UnityEditor;
using Debug = UnityEngine.Debug;

namespace Ryx.Sidekick.Editor
{
    internal interface IProcessHost : IDisposable
    {
        event Action<string> OnOutputLine;
        event Action<string> OnErrorLine;
        event Action OnProcessStarted;
        event Action<int> OnProcessExited;

        bool IsRunning { get; }
        bool IsStdinOpen { get; }

        bool StartStreaming(string arguments);
        bool WriteLineToStdin(string line);
        bool WriteToStdin(string text, bool appendNewLine = false);
        void CloseStdin();
        bool TryCloseStdin();
        void Stop();
        Task InterruptAsync();
        void Cleanup();
    }

    /// <summary>
    /// Hosts the CLI process and manages its lifecycle.
    /// Single responsibility: process spawning, stdin/stdout/stderr, and graceful shutdown.
    /// </summary>
    internal class CliProcessHost : IProcessHost
    {
        private const int OutputLinesPerPump = 16;

        public event Action<string> OnOutputLine;
        public event Action<string> OnErrorLine;
        public event Action OnProcessStarted;
        public event Action<int> OnProcessExited;

        private Process _process;
        private CancellationTokenSource _cancellationTokenSource;
        private readonly ConcurrentQueue<string> _outputQueue = new();
        private readonly ConcurrentQueue<string> _errorQueue = new();
        private readonly object _processLock = new();
        private readonly object _outputPumpLock = new();
        private volatile bool _isRunning;
        private bool _stdinOpen;
        private TaskCompletionSource<int> _processExitTcs;
        private bool _outputPumpScheduled;

        public bool IsRunning => _isRunning;
        public bool IsStdinOpen => _stdinOpen;

        /// <summary>
        /// Start a streaming CLI process with stdin open.
        /// </summary>
        public bool StartStreaming(string arguments)
        {
            if (_isRunning)
            {
                Stop();
                Cleanup();
            }

            var settings = SidekickSettings.instance;
            var startInfo = settings.CreateProcessStartInfo(arguments);
            var isDebugMode = settings.DebugMode;

            if (!isDebugMode)
            {
                startInfo.StandardOutputEncoding = Encoding.UTF8;
                startInfo.StandardErrorEncoding = Encoding.UTF8;
                startInfo.StandardInputEncoding = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false);
                startInfo.EnvironmentVariables["TERM"] = "dumb";
                startInfo.EnvironmentVariables["NO_COLOR"] = "1";
            }

            if (settings.VerboseLogging)
            {
                Debug.Log($"[CliProcessHost] Executing CLI: {startInfo.FileName} {startInfo.Arguments}");
            }

            try
            {
                _cancellationTokenSource = new CancellationTokenSource();
                _processExitTcs = new TaskCompletionSource<int>(TaskCreationOptions.RunContinuationsAsynchronously);

                lock (_processLock)
                {
                    _process = new Process { StartInfo = startInfo };
                    _process.EnableRaisingEvents = true;
                    _process.Exited += HandleProcessExited;

                    if (!isDebugMode)
                    {
                        _process.OutputDataReceived += HandleOutputDataReceived;
                        _process.ErrorDataReceived += HandleErrorDataReceived;
                    }
                }

                _isRunning = true;
                _process.Start();
                _stdinOpen = !isDebugMode;

                if (!isDebugMode)
                {
                    _process.BeginOutputReadLine();
                    _process.BeginErrorReadLine();
                }

                if (settings.VerboseLogging)
                {
                    Debug.Log($"[CliProcessHost] Process started with PID: {_process.Id}");
                }

                OnProcessStarted?.Invoke();
                return true;
            }
            catch (Exception ex)
            {
                Debug.LogException(ex);
                _isRunning = false;
                Cleanup();
                return false;
            }
        }
        
        /// <summary>
        /// Write a line to stdin.
        /// </summary>
        public bool WriteLineToStdin(string line)
        {
            return WriteToStdin(line, appendNewLine: true);
        }

        /// <summary>
        /// Write raw text to stdin, optionally appending a trailing newline.
        /// </summary>
        public bool WriteToStdin(string text, bool appendNewLine = false)
        {
            lock (_processLock)
            {
                if (_process is { HasExited: false } && _stdinOpen)
                {
                    try
                    {
                        if (appendNewLine)
                            _process.StandardInput.WriteLine(text);
                        else
                            _process.StandardInput.Write(text);
                        _process.StandardInput.Flush();
                        return true;
                    }
                    catch (Exception ex)
                    {
                        Debug.LogError($"[CliProcessHost] WriteToStdin error: {ex}");
                        return false;
                    }
                }

                if (SidekickSettings.instance.VerboseLogging)
                {
                    var reason = _process == null ? "process is null" :
                                 _process.HasExited ? "process has exited" :
                                 !_stdinOpen ? "stdin is closed" : "unknown";
                    Debug.LogWarning($"[CliProcessHost] Cannot write to stdin: {reason}");
                }
                return false;
            }
        }

        /// <summary>
        /// Close stdin without killing the process.
        /// </summary>
        public void CloseStdin()
        {
            TryCloseStdin();
        }

        public bool TryCloseStdin()
        {
            lock (_processLock)
            {
                if (_process is not { HasExited: false } || !_stdinOpen)
                {
                    return false;
                }

                try
                {
                    _process.StandardInput.Close();
                    return true;
                }
                catch (Exception ex)
                {
                    if (SidekickSettings.instance.VerboseLogging)
                    {
                        Debug.LogWarning($"[CliProcessHost] CloseStdin error: {ex.Message}");
                    }
                    return false;
                }
                finally
                {
                    _stdinOpen = false;
                }
            }
        }

        /// <summary>
        /// Stop the process (force kill). Used for synchronous cleanup scenarios.
        /// </summary>
        public void Stop()
        {
            _cancellationTokenSource?.Cancel();

            lock (_processLock)
            {
                if (_process is { HasExited: false })
                {
                    // Close stdin to signal CLI to finish
                    try { _process.StandardInput.Close(); }
                    catch { /* ignore */ }

                    // Force kill - no waiting since this is synchronous cleanup
                    try { _process.Kill(); }
                    catch { /* ignore */ }
                }
            }

            // Clear any queued output to prevent stale events from being processed
            while (_outputQueue.TryDequeue(out _)) { }
            while (_errorQueue.TryDequeue(out _)) { }

            UnscheduleOutputPump();
            _isRunning = false;
            _stdinOpen = false;
        }

        /// <summary>
        /// Gracefully interrupt by closing stdin and waiting for exit.
        /// </summary>
        public async Task InterruptAsync()
        {
            if (!_isRunning || _process == null || _process.HasExited)
                return;

            try
            {
                if (_stdinOpen)
                {
                    CloseStdin();
                }

                var exitTask = _processExitTcs?.Task ?? Task.CompletedTask;
                var completed = await Task.WhenAny(exitTask, Task.Delay(3000));

                if (completed != exitTask && _process is { HasExited: false })
                {
                    if (SidekickSettings.instance.VerboseLogging)
                    {
                        Debug.Log("[CliProcessHost] Graceful shutdown timeout, force killing");
                    }
                    _process.Kill();
                }
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"[CliProcessHost] Error during interrupt: {ex.Message}");
            }
            finally
            {
                Cleanup();
            }
        }

        public void Cleanup()
        {
            _processExitTcs = null;
            _stdinOpen = false;
            UnscheduleOutputPump();

            lock (_processLock)
            {
                if (_process != null)
                {
                    try
                    {
                        _process.OutputDataReceived -= HandleOutputDataReceived;
                        _process.ErrorDataReceived -= HandleErrorDataReceived;
                        _process.Exited -= HandleProcessExited;
                    }
                    catch { /* ignore */ }

                    try
                    {
                        if (!_process.HasExited)
                        {
                            _process.Kill();
                        }
                    }
                    catch { /* ignore */ }

                    _process.Dispose();
                    _process = null;
                }
            }

            _cancellationTokenSource?.Dispose();
            _cancellationTokenSource = null;

            while (_outputQueue.TryDequeue(out _)) { }
            while (_errorQueue.TryDequeue(out _)) { }
        }

        public void Dispose()
        {
            Stop();
            Cleanup();
        }

        private void HandleOutputDataReceived(object sender, DataReceivedEventArgs e)
        {
            if (e.Data == null) return;
            _outputQueue.Enqueue(e.Data);
            EnsureOutputPumpScheduled();
        }

        private void HandleErrorDataReceived(object sender, DataReceivedEventArgs e)
        {
            if (string.IsNullOrWhiteSpace(e.Data)) return;
            _errorQueue.Enqueue(e.Data);
            EditorApplication.delayCall += ProcessQueuedErrors;
        }

        private void ProcessQueuedOutput()
        {
            var processed = 0;
            while (processed < OutputLinesPerPump && _outputQueue.TryDequeue(out var line))
            {
                OnOutputLine?.Invoke(line);
                processed++;
            }

            if (!_outputQueue.IsEmpty)
                return;
            UnscheduleOutputPump();
        }

        private void ProcessQueuedErrors()
        {
            while (_errorQueue.TryDequeue(out var line))
            {
                OnErrorLine?.Invoke(line);
            }
        }

        private void HandleProcessExited(object sender, EventArgs e)
        {
            try
            {
                var exitCode = _process?.ExitCode ?? -1;
                _isRunning = false;
                _stdinOpen = false;
                UnscheduleOutputPump();

                if (SidekickSettings.instance.VerboseLogging)
                {
                    Debug.Log($"[CliProcessHost] Process exited with code {exitCode}");
                }

                _processExitTcs?.TrySetResult(exitCode);
                EditorApplication.delayCall += () => OnProcessExited?.Invoke(exitCode);
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"[CliProcessHost] Error handling process exit: {ex.Message}");
                _processExitTcs?.TrySetException(ex);
            }
        }

        private void EnsureOutputPumpScheduled()
        {
            lock (_outputPumpLock)
            {
                if (_outputPumpScheduled)
                    return;

                _outputPumpScheduled = true;
                EditorApplication.update += ProcessQueuedOutput;
            }
        }

        private void UnscheduleOutputPump()
        {
            lock (_outputPumpLock)
            {
                if (!_outputPumpScheduled)
                    return;

                EditorApplication.update -= ProcessQueuedOutput;
                _outputPumpScheduled = false;
            }
        }
    }
}
