// SPDX-License-Identifier: GPL-3.0-only
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Ryx.Sidekick.Editor.Domain.Commands;
using Ryx.Sidekick.Editor.Providers;
using Ryx.Sidekick.Editor.UseCases.Contracts;

namespace Ryx.Sidekick.Editor.Infrastructure.Providers.Claude
{
    /// <summary>
    /// Fetches live capabilities (model catalog + slash commands) from the Claude CLI
    /// using the <c>initialize</c> control request handshake.
    ///
    /// <para>
    /// Both <see cref="LoadModelsAsync"/> and <see cref="LoadCommandsAsync"/> share a
    /// single lazy fetch; the CLI process is spawned at most once per scope lifetime.
    /// A failed fetch is NOT permanently cached — the next call re-tries.
    /// </para>
    /// </summary>
    internal sealed class ClaudeCliCapabilitiesClient :
        IProviderCapabilitySources,
        IProviderModelCatalogSource,
        IProviderSlashCommandSource
    {
        // ──────────────────────────────────────────────────────────────────────
        // Transport delegate type

        /// <summary>
        /// Transport abstraction used for unit testing.
        /// Takes the stdin JSON line and a cancellation token; returns the matching
        /// control-response JSON line from stdout.
        /// </summary>
        internal delegate Task<string> TransportDelegate(string stdinLine, CancellationToken cancellationToken);

        // ──────────────────────────────────────────────────────────────────────
        // Fields

        private const string InitializeRequestLine =
            "{\"type\":\"control_request\",\"request_id\":\"req_1\",\"request\":{\"subtype\":\"initialize\"}}";

        private readonly ISettingsStore _settingsStore;
        private readonly ILogger _logger;
        private readonly TransportDelegate _transport;

        private readonly object _lock = new object();
        private readonly CancellationTokenSource _disposeCts = new CancellationTokenSource();
        private Task<ClaudeCliCapabilities> _sharedFetch;
        private bool _disposed;

        // ──────────────────────────────────────────────────────────────────────
        // IProviderCapabilitySources — aggregate properties return `this`

        public IProviderModelCatalogSource ModelCatalogSource => this;
        public IProviderSlashCommandSource SlashCommandSource => this;

        // ──────────────────────────────────────────────────────────────────────
        // Exposed account info (null until a successful fetch)

        public ProviderAccountInfo Account { get; private set; }

        // ──────────────────────────────────────────────────────────────────────
        // Construction

        /// <summary>
        /// Production constructor.
        /// </summary>
        public ClaudeCliCapabilitiesClient(ISettingsStore settingsStore, ILogger logger)
            : this(settingsStore, logger, transport: null)
        {
        }

        /// <summary>
        /// Testability constructor — supply a fake <paramref name="transport"/> to avoid
        /// spawning a real process.
        /// </summary>
        internal ClaudeCliCapabilitiesClient(
            ISettingsStore settingsStore,
            ILogger logger,
            TransportDelegate transport)
        {
            _settingsStore = settingsStore ?? throw new ArgumentNullException(nameof(settingsStore));
            _logger = logger;
            _transport = transport ?? RealTransport;
        }

        // ──────────────────────────────────────────────────────────────────────
        // IProviderModelCatalogSource

        public async Task<ProviderModelCatalog> LoadModelsAsync(CancellationToken cancellationToken)
        {
            var capabilities = await FetchCapabilitiesAsync(cancellationToken).ConfigureAwait(false);

            if (capabilities.Models == null || capabilities.Models.Count == 0)
                throw new InvalidOperationException(
                    "[ClaudeCliCapabilitiesClient] Initialize response contained no models.");

            return new ProviderModelCatalog("claude", capabilities.Models);
        }

        // ──────────────────────────────────────────────────────────────────────
        // IProviderSlashCommandSource

        public async Task<IReadOnlyList<SlashCommand>> LoadCommandsAsync(CancellationToken cancellationToken)
        {
            try
            {
                var capabilities = await FetchCapabilitiesAsync(cancellationToken).ConfigureAwait(false);
                return capabilities.Commands ?? (IReadOnlyList<SlashCommand>)Array.Empty<SlashCommand>();
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (ObjectDisposedException)
            {
                throw;
            }
            catch
            {
                // On any other failure fall back to the hardcoded list.
                return GetFallbackCommands();
            }
        }

        // ──────────────────────────────────────────────────────────────────────
        // IDisposable

        public void Dispose()
        {
            lock (_lock)
            {
                if (_disposed)
                    return;
                _disposed = true;
            }

            _disposeCts.Cancel();
            _disposeCts.Dispose();
        }

        // ──────────────────────────────────────────────────────────────────────
        // Core fetch

        private Task<ClaudeCliCapabilities> FetchCapabilitiesAsync(CancellationToken externalCt)
        {
            lock (_lock)
            {
                if (_disposed)
                    throw new ObjectDisposedException(nameof(ClaudeCliCapabilitiesClient));

                // If there is no cached task, or the cached task is faulted/cancelled,
                // start a fresh fetch so the caller can retry.
                if (_sharedFetch == null
                    || _sharedFetch.IsFaulted
                    || _sharedFetch.IsCanceled)
                {
                    var linked = CancellationTokenSource.CreateLinkedTokenSource(
                        externalCt, _disposeCts.Token);
                    _sharedFetch = RunFetchAsync(linked);
                }

                return _sharedFetch;
            }
        }

        private async Task<ClaudeCliCapabilities> RunFetchAsync(CancellationTokenSource linkedCts)
        {
            try
            {
                using (linkedCts)
                {
                    var ct = linkedCts.Token;
                    var responseLine = await _transport(InitializeRequestLine, ct).ConfigureAwait(false);
                    var capabilities = ClaudeInitializeResponseParser.Parse(responseLine);
                    Account = capabilities.Account;
                    return capabilities;
                }
            }
            catch (Exception ex) when (!(ex is OperationCanceledException))
            {
                _logger?.LogWarning(
                    $"[ClaudeCliCapabilitiesClient] Initialize fetch failed: {ex.Message}");
                throw;
            }
        }

        // ──────────────────────────────────────────────────────────────────────
        // Real process transport

        private async Task<string> RealTransport(string stdinLine, CancellationToken ct)
        {
            // Build args independently of BuildArguments (no --model / --effort / etc.)
            const string args = "-p --input-format stream-json --output-format stream-json --verbose";

            var startInfo = _settingsStore.CreateProcessStartInfo(args);
            startInfo.StandardOutputEncoding = Encoding.UTF8;
            startInfo.StandardErrorEncoding = Encoding.UTF8;
            startInfo.EnvironmentVariables["TERM"] = "dumb";
            startInfo.EnvironmentVariables["NO_COLOR"] = "1";

            var errorBuilder = new StringBuilder();
            string responseLine = null;

            using var process = new Process { StartInfo = startInfo, EnableRaisingEvents = true };
            var tcs = new TaskCompletionSource<int>(TaskCreationOptions.RunContinuationsAsynchronously);

            // Cancellation → kill process and cancel TCS
            using var reg = ct.Register(() =>
            {
                tcs.TrySetCanceled(ct);
                try { process.Kill(); } catch { }
            });

            process.OutputDataReceived += (_, e) =>
            {
                if (e.Data == null) return;
                // Look for the response to our initialize request specifically
                if (responseLine == null
                    && e.Data.Contains("\"control_response\"")
                    && e.Data.Contains("\"req_1\""))
                    responseLine = e.Data;
            };

            process.ErrorDataReceived += (_, e) =>
            {
                if (e.Data != null)
                    errorBuilder.AppendLine(e.Data);
            };

            process.Exited += (_, _) =>
            {
                try { tcs.TrySetResult(process.ExitCode); }
                catch { tcs.TrySetResult(-1); }
            };

            process.Start();
            process.BeginOutputReadLine();
            process.BeginErrorReadLine();

            // Write the initialize request then close stdin so the CLI proceeds
            await process.StandardInput.WriteLineAsync(stdinLine).ConfigureAwait(false);
            process.StandardInput.Close();

            // Wait for exit with 30 s timeout
            var timeout = Task.Delay(TimeSpan.FromSeconds(30), ct);
            var completed = await Task.WhenAny(tcs.Task, timeout).ConfigureAwait(false);

            if (completed == timeout)
            {
                try { process.Kill(); } catch { }
                var stderrSoFar = errorBuilder.ToString().Trim();
                throw new TimeoutException(
                    $"[ClaudeCliCapabilitiesClient] Initialize handshake timed out after 30s." +
                    (string.IsNullOrEmpty(stderrSoFar) ? string.Empty : $" stderr: {stderrSoFar}"));
            }

            ct.ThrowIfCancellationRequested();

            if (responseLine == null)
            {
                // Treat a missing response as a fetch failure (e.g. an older CLI without
                // the control protocol) so LoadCommandsAsync falls back instead of
                // surfacing an empty command list.
                var stderr = errorBuilder.ToString().Trim();
                throw new InvalidOperationException(
                    "[ClaudeCliCapabilitiesClient] No initialize control_response in CLI output." +
                    (string.IsNullOrEmpty(stderr) ? string.Empty : $" stderr: {stderr}"));
            }

            return responseLine;
        }

        // ──────────────────────────────────────────────────────────────────────
        // Fallback commands

        /// <summary>
        /// Returns the hardcoded fallback list of Claude CLI slash commands.
        /// Moved here from <c>CliConfigService.GetFallbackCommands()</c>.
        /// All entries receive <see cref="SlashCommandOrigin.Builtin"/>.
        /// </summary>
        internal static List<SlashCommand> GetFallbackCommands()
        {
            return new List<SlashCommand>
            {
                new SlashCommand("compact", "Free up context by summarizing the conversation so far",
                    acceptsArguments: false),
                new SlashCommand("context", "View or manage context files",
                    acceptsArguments: true),
                new SlashCommand("cost", "Show token usage and cost for this session",
                    acceptsArguments: false),
                new SlashCommand("init", "Initialize Claude CLI in current directory",
                    acceptsArguments: false),
                new SlashCommand("pr-comments", "Address PR review comments",
                    acceptsArguments: false),
                new SlashCommand("review", "Review code changes in the current branch",
                    acceptsArguments: false),
                new SlashCommand("release-notes", "Generate release notes from recent commits",
                    acceptsArguments: false),
                new SlashCommand("security-review", "Perform a security review of the codebase",
                    acceptsArguments: false),
            };
        }
    }
}
