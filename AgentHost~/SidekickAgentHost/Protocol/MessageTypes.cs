// SPDX-License-Identifier: GPL-3.0-only
namespace Ryx.Sidekick.AgentHost.Protocol;

/// <summary>
/// Wire message type tags (the "t" field of every JSON line).
/// Shared vocabulary between the daemon and the Unity client (Phase 2).
/// </summary>
internal static class MessageTypes
{
    // client -> daemon
    public const string Hello = "HELLO";
    public const string Start = "START";
    public const string Write = "WRITE";
    public const string CloseStdin = "CLOSE_STDIN";
    public const string Stop = "STOP";
    public const string Interrupt = "INTERRUPT";
    public const string Attach = "ATTACH";
    public const string Trim = "TRIM";
    public const string Ping = "PING";
    public const string Shutdown = "SHUTDOWN";

    // daemon -> client
    public const string HelloOk = "HELLO_OK";
    public const string Started = "STARTED";
    public const string Output = "OUTPUT";
    public const string Exited = "EXITED";
    public const string ReplayTruncated = "REPLAY_TRUNCATED";
    public const string Pong = "PONG";
    public const string Error = "ERROR";
}

internal static class StreamNames
{
    public const string Stdout = "stdout";
    public const string Stderr = "stderr";
}