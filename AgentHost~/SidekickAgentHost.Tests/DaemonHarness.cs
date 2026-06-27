// SPDX-License-Identifier: GPL-3.0-only
using System;
using System.Collections.Generic;
using System.IO;

namespace Ryx.Sidekick.AgentHost.Tests;

/// <summary>
/// Boots an in-process <see cref="AgentHostServer"/> bound to a real loopback
/// port, with discovery files in a throwaway temp dir and a known token, so a
/// <see cref="TestClient"/> can talk to it over the wire. Disposing stops the
/// daemon and removes the temp dir.
/// </summary>
internal sealed class DaemonHarness : IDisposable
{
    public AgentHostServer Server { get; }
    public string Token { get; }
    private string TempDir { get; }
    private int Port => Server.Port;

    public DaemonHarness(int graceSeconds = 30, int ownerPid = 0,
        int maxBufferLines = 200_000, long maxBufferBytes = 8L * 1024 * 1024,
        int maxRetainedExitedSessions = 32)
    {
        TempDir = Path.Combine(Path.GetTempPath(), "sidekick-agenthost-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(TempDir);

        Token = Guid.NewGuid().ToString("N");

        var options = ParseOptions([
            "--port-file", Path.Combine(TempDir, "daemon.port"),
            "--token-file", Path.Combine(TempDir, "daemon.token"),
            "--pid-file", Path.Combine(TempDir, "daemon.pid"),
            "--grace-seconds", graceSeconds.ToString(),
            "--owner-pid", ownerPid.ToString()
        ]);

        Server = new AgentHostServer(options, Token, maxBufferLines, maxBufferBytes, maxRetainedExitedSessions);
        Server.Start();
    }

    /// <summary>Connect a fresh client and complete the HELLO handshake.</summary>
    public TestClient ConnectAndHello()
    {
        var client = new TestClient(Port);
        client.Send(new { t = "HELLO", token = Token, proto = 1, ownerPid = 0 });
        client.WaitFor("HELLO_OK");
        return client;
    }

    /// <summary>
    /// Connect, send HELLO, and return both the client and the HELLO_OK payload
    /// (used to assert session summaries reported on reconnect).
    /// </summary>
    public (TestClient client, System.Text.Json.JsonElement helloOk) ConnectHelloCapture()
    {
        var client = new TestClient(Port);
        client.Send(new { t = "HELLO", token = Token, proto = 1, ownerPid = 0 });
        var helloOk = client.WaitFor("HELLO_OK");
        return (client, helloOk);
    }

    /// <summary>Connect a client WITHOUT sending HELLO (for auth tests).</summary>
    public TestClient Connect() => new(Port);

    /// <summary>Spec for a line-buffered `cat` echo child (stdin -&gt; stdout).</summary>
    public static object CatSpec() => new
    {
        filename = "cat",
        args = new[] { "-u" },
        workingDir = (string?)null,
        env = new Dictionary<string, string>(),
    };

    private static DaemonOptions ParseOptions(string[] args) => DaemonOptions.Parse(args);

    public void Dispose()
    {
        try { Server.Stop(); } catch { /* ignore */ }
        try { Server.Stopped.Wait(TimeSpan.FromSeconds(5)); } catch { /* ignore */ }
        try { if (Directory.Exists(TempDir)) Directory.Delete(TempDir, true); } catch { /* ignore */ }
    }
}