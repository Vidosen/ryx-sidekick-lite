// SPDX-License-Identifier: GPL-3.0-only
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using Xunit;

namespace Ryx.Sidekick.AgentHost.Tests;

public class AgentHostProtocolTests
{
    // ---- 1. START + WRITE(appendNewline) -> matching OUTPUT, seq increments ----

    [Fact]
    public void Start_Write_EchoesOutput_WithIncrementingSeq()
    {
        using var host = new DaemonHarness();
        using var client = host.ConnectAndHello();

        client.Send(new { t = "START", spec = DaemonHarness.CatSpec() });
        var started = client.WaitFor("STARTED");
        var handle = started.GetProperty("handle").GetString();
        Assert.False(string.IsNullOrEmpty(handle));

        client.Send(new { t = "WRITE", handle, data = "hello", appendNewline = true });
        var out1 = client.WaitFor("OUTPUT");
        Assert.Equal("hello", out1.GetProperty("line").GetString());
        Assert.Equal("stdout", out1.GetProperty("stream").GetString());
        Assert.Equal(1, out1.GetProperty("seq").GetInt64());
        Assert.Equal(handle, out1.GetProperty("handle").GetString());

        client.Send(new { t = "WRITE", handle, data = "world", appendNewline = true });
        var out2 = client.WaitFor("OUTPUT");
        Assert.Equal("world", out2.GetProperty("line").GetString());
        Assert.Equal(2, out2.GetProperty("seq").GetInt64());
    }

    // ---- 1b. spec.commandLine is preferred over args[] and tokenized by the OS, not jammed
    //          as one literal ArgumentList element (spawn-spec fidelity, Phase 4) ----

    [Fact]
    public void Start_CommandLineSpec_IsAssignedToArguments_AndTokenizedByOs()
    {
        // /bin/sh -c "printf '...'" — only works if commandLine is assigned to
        // ProcessStartInfo.Arguments (so sh sees -c and the script as separate tokens). If the
        // daemon instead pushed the whole string as ONE ArgumentList element, sh would get a single
        // bogus argument and print nothing.
        using var host = new DaemonHarness();
        using var client = host.ConnectAndHello();

        var spec = new
        {
            filename = "/bin/sh",
            commandLine = "-c \"printf 'CMDLINE_OK\\n'\"",
            workingDir = (string?)null,
            env = new Dictionary<string, string>(),
        };

        client.Send(new { t = "START", spec });
        var handle = client.WaitFor("STARTED").GetProperty("handle").GetString();
        Assert.False(string.IsNullOrEmpty(handle));

        var line = client.WaitFor("OUTPUT");
        Assert.Equal("CMDLINE_OK", line.GetProperty("line").GetString());
        Assert.Equal("stdout", line.GetProperty("stream").GetString());
    }

    // ---- 2. ATTACH after disconnect/reconnect replays buffered OUTPUT, then live ----

    [Fact]
    public void Attach_AfterReconnect_ReplaysInOrder_ThenLiveContinues()
    {
        using var host = new DaemonHarness();

        string handle;
        // Client A: start + produce two lines, then drop the socket.
        using (var a = host.ConnectAndHello())
        {
            a.Send(new { t = "START", spec = DaemonHarness.CatSpec() });
            handle = a.WaitFor("STARTED").GetProperty("handle").GetString()!;

            a.Send(new { t = "WRITE", handle, data = "one", appendNewline = true });
            a.Send(new { t = "WRITE", handle, data = "two", appendNewline = true });

            // Make sure both lines are buffered before we disconnect.
            var seen = a.CollectWhile("OUTPUT", l => l.Count >= 2);
            Assert.Equal(2, seen.Count);
        } // A disposed -> socket closed; child must survive.

        // Client B: reconnect and ATTACH from seq 0 -> replay 1,2 in order.
        using var b = host.ConnectAndHello();
        b.Send(new { t = "ATTACH", handle, afterSeq = 0L });

        var replay = b.CollectWhile("OUTPUT", l => l.Count >= 2);
        Assert.Equal(2, replay.Count);
        Assert.Equal(new long[] { 1, 2 }, replay.Select(e => e.GetProperty("seq").GetInt64()).ToArray());
        Assert.Equal(new[] { "one", "two" }, replay.Select(e => e.GetProperty("line").GetString()).ToArray());

        // Live continues on the same handle for the reattached client.
        b.Send(new { t = "WRITE", handle, data = "three", appendNewline = true });
        var live = b.WaitFor("OUTPUT");
        Assert.Equal("three", live.GetProperty("line").GetString());
        Assert.Equal(3, live.GetProperty("seq").GetInt64());
    }

    // ---- 3. durable-trim: TRIM(safeSeq) then ATTACH below/above the floor ----

    [Fact]
    public void Trim_DurableFloor_TruncatesBelow_ReplaysAtOrAbove()
    {
        using var host = new DaemonHarness();

        string handle;
        using (var a = host.ConnectAndHello())
        {
            a.Send(new { t = "START", spec = DaemonHarness.CatSpec() });
            handle = a.WaitFor("STARTED").GetProperty("handle").GetString()!;

            a.Send(new { t = "WRITE", handle, data = "L1", appendNewline = true });
            a.Send(new { t = "WRITE", handle, data = "L2", appendNewline = true });
            a.Send(new { t = "WRITE", handle, data = "L3", appendNewline = true });
            var seen = a.CollectWhile("OUTPUT", l => l.Count >= 3);
            Assert.Equal(3, seen.Count);

            // Durable-trim up to seq 2: the client says it can recover <=2 elsewhere.
            a.Send(new { t = "TRIM", handle, safeSeq = 2L });
            // PING/PONG round-trip guarantees the TRIM has been processed before
            // we tear down (ordered, single connection).
            a.Send(new { t = "PING" });
            a.WaitFor("PONG");
        }

        // ATTACH below the floor (afterSeq=1 < safeSeq=2) -> REPLAY_TRUNCATED.
        using (var b = host.ConnectAndHello())
        {
            b.Send(new { t = "ATTACH", handle, afterSeq = 1L });
            var trunc = b.WaitFor("REPLAY_TRUNCATED");
            Assert.Equal(handle, trunc.GetProperty("handle").GetString());
            Assert.Equal(2, trunc.GetProperty("floorSeq").GetInt64());
        }

        // ATTACH at the floor (afterSeq=2 >= safeSeq=2) -> replays seq>2 = {3}.
        using (var c = host.ConnectAndHello())
        {
            c.Send(new { t = "ATTACH", handle, afterSeq = 2L });
            var replay = c.CollectWhile("OUTPUT", l => l.Count >= 1);
            Assert.Single(replay);
            Assert.Equal(3, replay[0].GetProperty("seq").GetInt64());
            Assert.Equal("L3", replay[0].GetProperty("line").GetString());
        }
    }

    // ---- 4. grace: exit after last client; reconnect within grace keeps child ----

    [Fact]
    public void Grace_ReconnectWithinGrace_KeepsChildAlive()
    {
        using var host = new DaemonHarness(graceSeconds: 30);

        string handle;
        using (var a = host.ConnectAndHello())
        {
            a.Send(new { t = "START", spec = DaemonHarness.CatSpec() });
            handle = a.WaitFor("STARTED").GetProperty("handle").GetString()!;
            a.Send(new { t = "WRITE", handle, data = "x", appendNewline = true });
            a.WaitFor("OUTPUT");
        } // disconnect -> grace armed, but 30s is plenty of headroom.

        // Reconnect quickly: HELLO_OK must still list the session as alive.
        var (b, helloOk) = host.ConnectHelloCapture();
        using var _b = b;
        var sessions = helloOk.GetProperty("sessions").EnumerateArray().ToList();
        var session = sessions.FirstOrDefault(s => s.GetProperty("handle").GetString() == handle);
        Assert.Equal(JsonValueKind.Object, session.ValueKind);
        Assert.True(session.GetProperty("alive").GetBoolean());

        // And the daemon is still running (not stopped).
        Assert.False(host.Server.Stopped.IsCompleted);
    }

    [Fact]
    public void Grace_NoReconnect_DaemonExitsAfterGrace()
    {
        using var host = new DaemonHarness(graceSeconds: 1);

        using (var a = host.ConnectAndHello())
        {
            a.Send(new { t = "START", spec = DaemonHarness.CatSpec() });
            a.WaitFor("STARTED");
        } // last client gone -> grace (~1s) starts counting.

        // Daemon should self-terminate within grace + watchdog slack.
        // Intentional bounded blocking wait on the daemon's lifetime signal
        // (not on this test's own scheduling), so no deadlock risk here.
#pragma warning disable xUnit1031
        var stopped = host.Server.Stopped.Wait(TimeSpan.FromSeconds(6));
#pragma warning restore xUnit1031
        Assert.True(stopped, "Daemon did not stop within grace window after last client left.");
        Assert.Equal(0, host.Server.SessionCount);
    }

    // ---- 5. token auth: wrong token -> socket closed, no HELLO_OK ----

    [Fact]
    public void Hello_WrongToken_IsRejected_NoHelloOk()
    {
        using var host = new DaemonHarness();
        using var client = host.Connect();

        client.Send(new { t = "HELLO", token = "not-the-real-token", proto = 1, ownerPid = 0 });

        // No HELLO_OK should arrive...
        Assert.Throws<TimeoutException>(() => client.WaitFor("HELLO_OK", timeoutMs: 1500));
        // ...and the server closes the socket.
        Assert.True(client.WaitForClose(2000), "Server did not close the socket after a bad token.");
    }

    [Fact]
    public void Hello_ProtoMismatch_IsRejected()
    {
        using var host = new DaemonHarness();
        using var client = host.Connect();

        client.Send(new { t = "HELLO", token = host.Token, proto = 999, ownerPid = 0 });

        // Server replies ERROR (proto mismatch) and closes.
        var err = client.WaitFor("ERROR", timeoutMs: 2000);
        Assert.Contains("proto", err.GetProperty("message").GetString(), StringComparison.OrdinalIgnoreCase);
        Assert.True(client.WaitForClose(2000));
    }

    // ---- 6. watchdog: owner Editor pid dead -> daemon self-terminates immediately, authoritative and
    //        independent of grace. Closes the orphan hole where a half-open client socket (Editor killed
    //        before TCP reaped the dead peer) would keep the daemon alive forever. ----

    [Fact]
    public async Task Watchdog_OwnerProcessGone_StopsDaemon_EvenWithGraceNotExpired()
    {
        // A child we immediately kill yields a deterministically-dead pid (vs. guessing an unused one).
        var doomed = System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
        {
            FileName = "cat",            // blocks on stdin until killed
            RedirectStandardInput = true,
            UseShellExecute = false,
        });
        Assert.NotNull(doomed);
        var deadPid = doomed!.Id;
        doomed.Kill();
        doomed.WaitForExit(2000);

        // Huge grace so ONLY the owner-gone path can stop the daemon (grace cannot be the cause).
        using var host = new DaemonHarness(graceSeconds: 3600, ownerPid: deadPid);

        var completed = await Task.WhenAny(host.Server.Stopped, Task.Delay(TimeSpan.FromSeconds(5)));
        Assert.True(ReferenceEquals(completed, host.Server.Stopped),
            "daemon must self-terminate when its owner Editor pid is dead, even though grace has not expired");
    }

    // ---- 7. late EXITED: attaching to a session that already exited (turn finished during the reload
    //        gap with no client connected) must replay the buffer AND then deliver EXITED, so the late
    //        re-subscriber learns the child is gone instead of waiting forever for a result. ----

    [Fact]
    public void Attach_ToExitedSession_ReplaysBufferedOutput_ThenSendsExited()
    {
        using var host = new DaemonHarness();

        string handle;
        using (var a = host.ConnectAndHello())
        {
            a.Send(new { t = "START", spec = DaemonHarness.CatSpec() });
            handle = a.WaitFor("STARTED").GetProperty("handle").GetString()!;

            // Produce two lines and confirm they are buffered BEFORE we make the child exit, so there is
            // no output-vs-exit drain race (cat echoes each line; once we've seen both, they're buffered).
            a.Send(new { t = "WRITE", handle, data = "r1", appendNewline = true });
            a.Send(new { t = "WRITE", handle, data = "r2", appendNewline = true });
            var seen = a.CollectWhile("OUTPUT", l => l.Count >= 2);
            Assert.Equal(2, seen.Count);

            // Close stdin so `cat` reaches EOF and exits cleanly (code 0) while A is still connected.
            a.Send(new { t = "CLOSE_STDIN", handle });
            var exit = a.WaitFor("EXITED");
            Assert.Equal(0, exit.GetProperty("code").GetInt32());
        } // A drops; the exited session is retained for reattach.

        // Client B reattaches from seq 0: it must replay r1,r2 in order AND then be told the child exited.
        using var b = host.ConnectAndHello();
        b.Send(new { t = "ATTACH", handle, afterSeq = 0L });

        var replay = b.CollectWhile("OUTPUT", l => l.Count >= 2);
        Assert.Equal(new[] { "r1", "r2" }, replay.Select(e => e.GetProperty("line").GetString()).ToArray());

        var exited = b.WaitFor("EXITED");
        Assert.Equal(handle, exited.GetProperty("handle").GetString());
        Assert.Equal(0, exited.GetProperty("code").GetInt32());
    }

    // ---- 8. bounded retention: exited sessions beyond the cap are evicted (oldest first); the newest
    //        exited session — a reload's reattach target — is always retained. ----

    [Fact]
    public void ExitedSessions_BeyondRetentionCap_AreEvicted_NewestKept()
    {
        // Cap retained exited sessions at 1 so the 2nd exit must evict the 1st.
        using var host = new DaemonHarness(maxRetainedExitedSessions: 1);

        string h1, h2;
        using (var a = host.ConnectAndHello())
        {
            a.Send(new { t = "START", spec = DaemonHarness.CatSpec() });
            h1 = a.WaitFor("STARTED").GetProperty("handle").GetString()!;
            a.Send(new { t = "CLOSE_STDIN", handle = h1 });
            a.WaitFor("EXITED");
        }
        using (var b = host.ConnectAndHello())
        {
            b.Send(new { t = "START", spec = DaemonHarness.CatSpec() });
            h2 = b.WaitFor("STARTED").GetProperty("handle").GetString()!;
            b.Send(new { t = "CLOSE_STDIN", handle = h2 });
            b.WaitFor("EXITED");
        }

        // Session 2's exit triggers eviction of the older exited session (cap = 1).
        Assert.True(
            System.Threading.SpinWait.SpinUntil(() => host.Server.SessionCount == 1, TimeSpan.FromSeconds(2)),
            "an exited session beyond the retention cap should be evicted once a newer one exits");

        // Reconnect: the HELLO_OK summary must list only the newest exited session; the oldest is gone.
        var (c, helloOk) = host.ConnectHelloCapture();
        using var _c = c;
        var handles = helloOk.GetProperty("sessions").EnumerateArray()
            .Select(s => s.GetProperty("handle").GetString()).ToList();
        Assert.Contains(h2, handles);
        Assert.DoesNotContain(h1, handles);
    }
}