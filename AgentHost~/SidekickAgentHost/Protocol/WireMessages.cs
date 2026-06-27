// SPDX-License-Identifier: GPL-3.0-only
using System.Collections.Generic;

namespace Ryx.Sidekick.AgentHost.Protocol;
// Outgoing (daemon -> client) message DTOs. Field names ARE the wire names
// (no naming policy is applied), so they map 1:1 to the JSON keys.
//
// Incoming messages are read field-by-field from a JsonDocument in
// WireCodec.ReadIncoming so we never throw on an unknown/partial shape.

internal sealed class SpawnSpec
{
    public string filename { get; set; } = "";

    /// <summary>
    /// Pre-built, OS-ready command-line string assigned VERBATIM to
    /// <see cref="System.Diagnostics.ProcessStartInfo.Arguments"/> — byte-for-byte what the
    /// in-process <c>CliProcessHost</c> hands to <c>Process.Start</c>. Preferred over
    /// <see cref="args"/> when present so the daemon never re-tokenizes (and never mismatches) the
    /// argument string the Unity-side <c>ICliPlatform</c> already produced. The Unity client (which
    /// resolves PATH/profile/nvm/.cmd) is the single source of truth for the command line.
    /// </summary>
    public string? commandLine { get; set; }

    /// <summary>
    /// Token list, used only when <see cref="commandLine"/> is absent (forward/back-compat). Each
    /// element is added to <c>ProcessStartInfo.ArgumentList</c> (runtime-escaped).
    /// </summary>
    public List<string> args { get; set; } = new();

    public string? workingDir { get; set; }
    public Dictionary<string, string> env { get; set; } = new();
}

internal sealed class SessionSummary
{
    public string handle { get; set; } = "";
    public bool alive { get; set; }
    public long lastSeq { get; set; }
}

internal sealed class HelloOkMessage
{
    public string t { get; set; } = MessageTypes.HelloOk;
    public string daemonVersion { get; set; } = "";
    public int proto { get; set; }
    public List<SessionSummary> sessions { get; set; } = new();
}

internal sealed class StartedMessage
{
    public string t { get; set; } = MessageTypes.Started;
    public string handle { get; set; } = "";
}

internal sealed class OutputMessage
{
    public string t { get; set; } = MessageTypes.Output;
    public string handle { get; set; } = "";
    public long seq { get; set; }
    public string stream { get; set; } = StreamNames.Stdout;
    public string line { get; set; } = "";
}

internal sealed class ExitedMessage
{
    public string t { get; set; } = MessageTypes.Exited;
    public string handle { get; set; } = "";
    public int code { get; set; }
}

internal sealed class ReplayTruncatedMessage
{
    public string t { get; set; } = MessageTypes.ReplayTruncated;
    public string handle { get; set; } = "";
    public long floorSeq { get; set; }
}

internal sealed class PongMessage
{
    public string t { get; set; } = MessageTypes.Pong;
}

internal sealed class ErrorMessage
{
    public string t { get; set; } = MessageTypes.Error;
    public string? handle { get; set; }
    public string message { get; set; } = "";
}