// SPDX-License-Identifier: GPL-3.0-only
using System.Collections.Generic;

namespace Ryx.Sidekick.AgentHost;

/// <summary>One buffered child output line with its session-monotonic seq.</summary>
internal readonly struct BufferedLine
{
    public readonly long Seq;
    public readonly bool IsStderr;
    public readonly string Line;

    public BufferedLine(long seq, bool isStderr, string line)
    {
        Seq = seq;
        IsStderr = isStderr;
        Line = line;
    }
}

/// <summary>
/// Per-session, seq-numbered output buffer implementing <b>durable-trim</b>
/// (plan risk #5): output is retained until the client explicitly tells the
/// daemon — via TRIM(safeSeq) — that it can recover up to that seq WITHOUT
/// the daemon. Output is NEVER trimmed merely because it was delivered.
///
/// <para><b>Floor:</b> <see cref="FloorSeq"/> is the highest seq that has been
/// discarded; the buffer therefore holds lines with seq &gt; FloorSeq. An
/// ATTACH(afterSeq) can be served iff afterSeq &gt;= FloorSeq (every line the
/// client still needs, seq &gt; afterSeq, is present). afterSeq &lt; FloorSeq
/// means lines in (afterSeq, FloorSeq] were discarded ⇒ REPLAY_TRUNCATED.</para>
///
/// <para><b>Safety cap:</b> if a session exceeds the hard cap before any TRIM,
/// the oldest lines are dropped and FloorSeq advances — bounding memory while
/// still preferring durability. Such a forced drop is what later produces
/// REPLAY_TRUNCATED for an ATTACH below the new floor.</para>
///
/// Not thread-safe on its own; callers (<see cref="ChildSession"/>) guard it.
/// </summary>
internal sealed class OutputBuffer
{
    private readonly int _maxLines;
    private readonly long _maxBytes;
    private readonly LinkedList<BufferedLine> _lines = new();

    private long _bufferedBytes;    // approximate retained payload size

    public OutputBuffer(int maxLines, long maxBytes)
    {
        _maxLines = maxLines;
        _maxBytes = maxBytes;
    }

    public long LastSeq { get; private set; }

    public long FloorSeq { get; private set; }

    public int Count => _lines.Count;

    /// <summary>Append a line, assigning the next monotonic seq. Returns that seq.</summary>
    public long Append(bool isStderr, string line)
    {
        var seq = ++LastSeq;
        var entry = new BufferedLine(seq, isStderr, line);
        _lines.AddLast(entry);
        _bufferedBytes += EstimateSize(line);
        EnforceCap();
        return seq;
    }

    /// <summary>
    /// Durable-trim: discard buffered lines with seq &lt;= safeSeq. Advances the
    /// floor to safeSeq (clamped to never regress and never exceed the highest
    /// seq seen). A safeSeq at or below the current floor is a no-op.
    /// </summary>
    public void Trim(long safeSeq)
    {
        if (safeSeq <= FloorSeq)
            return;

        var newFloor = safeSeq > LastSeq ? LastSeq : safeSeq;

        var node = _lines.First;
        while (node != null && node.Value.Seq <= newFloor)
        {
            var next = node.Next;
            _bufferedBytes -= EstimateSize(node.Value.Line);
            _lines.Remove(node);
            node = next;
        }

        if (newFloor > FloorSeq)
            FloorSeq = newFloor;
    }

    /// <summary>
    /// True if an ATTACH(afterSeq) can be replayed in full. False ⇒ the caller
    /// must respond REPLAY_TRUNCATED with <see cref="FloorSeq"/>.
    /// </summary>
    public bool CanReplayFrom(long afterSeq) => afterSeq >= FloorSeq;

    /// <summary>
    /// Snapshot of buffered lines with seq &gt; afterSeq, in seq order, for replay.
    /// Always call <see cref="CanReplayFrom"/> first.
    /// </summary>
    public List<BufferedLine> Replay(long afterSeq)
    {
        var result = new List<BufferedLine>();
        foreach (var entry in _lines)
        {
            if (entry.Seq > afterSeq)
                result.Add(entry);
        }
        return result;
    }

    private void EnforceCap()
    {
        // Drop oldest while over either bound, but always keep at least one line
        // so we never lose the most recent output to the cap.
        while (_lines.Count > 1 && (_lines.Count > _maxLines || _bufferedBytes > _maxBytes))
        {
            var oldest = _lines.First!;
            if (oldest.Value.Seq > FloorSeq)
                FloorSeq = oldest.Value.Seq;
            _bufferedBytes -= EstimateSize(oldest.Value.Line);
            _lines.RemoveFirst();
        }
    }

    private static long EstimateSize(string line) => (line?.Length ?? 0) + 1L;
}