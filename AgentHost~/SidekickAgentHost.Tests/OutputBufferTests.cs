// SPDX-License-Identifier: GPL-3.0-only
using System.Linq;
using Xunit;

namespace Ryx.Sidekick.AgentHost.Tests;

public class OutputBufferTests
{
    private static OutputBuffer New(int lines = 1000, long bytes = 1024 * 1024) => new(lines, bytes);

    [Fact]
    public void Append_AssignsMonotonicSeq_AcrossStreams()
    {
        var b = New();
        Assert.Equal(1, b.Append(false, "a"));
        Assert.Equal(2, b.Append(true, "b"));   // stderr shares the seq line
        Assert.Equal(3, b.Append(false, "c"));
        Assert.Equal(3, b.LastSeq);
    }

    [Fact]
    public void Replay_ReturnsOnlyLinesAfterSeq_InOrder()
    {
        var b = New();
        b.Append(false, "one");
        b.Append(false, "two");
        b.Append(false, "three");

        var replay = b.Replay(1);
        Assert.Equal(new long[] { 2, 3 }, replay.Select(l => l.Seq).ToArray());
        Assert.Equal(new[] { "two", "three" }, replay.Select(l => l.Line).ToArray());
    }

    [Fact]
    public void Trim_DurableFloor_DiscardsAtOrBelowSafeSeq()
    {
        var b = New();
        b.Append(false, "1");
        b.Append(false, "2");
        b.Append(false, "3");

        b.Trim(2);

        Assert.Equal(2, b.FloorSeq);
        Assert.True(b.CanReplayFrom(2));      // at floor -> ok
        Assert.True(b.CanReplayFrom(3));      // above floor -> ok
        Assert.False(b.CanReplayFrom(1));     // below floor -> truncated
        Assert.False(b.CanReplayFrom(0));

        var replay = b.Replay(2);
        Assert.Single(replay);
        Assert.Equal(3, replay[0].Seq);
    }

    [Fact]
    public void Trim_NeverRegresses_AndClampsToLastSeq()
    {
        var b = New();
        b.Append(false, "1");
        b.Append(false, "2");

        b.Trim(5);                  // safeSeq beyond what exists -> clamp to lastSeq (2)
        Assert.Equal(2, b.FloorSeq);

        b.Trim(1);                  // lower than current floor -> no-op
        Assert.Equal(2, b.FloorSeq);
    }

    [Fact]
    public void DoesNotTrimOnAppend_RetainsEverythingUntilTrim()
    {
        var b = New();
        for (var i = 0; i < 100; i++)
            b.Append(false, "line" + i);

        // No TRIM was issued -> floor stays at 0, full history replayable.
        Assert.Equal(0, b.FloorSeq);
        Assert.True(b.CanReplayFrom(0));
        Assert.Equal(100, b.Replay(0).Count);
    }

    [Fact]
    public void SafetyCap_DropsOldest_AndAdvancesFloor_WhenExceededBeforeTrim()
    {
        // Cap of 3 lines, generous bytes. Append 5 with no TRIM.
        var b = new OutputBuffer(maxLines: 3, maxBytes: 1024 * 1024);
        for (var i = 1; i <= 5; i++)
            b.Append(false, "L" + i);

        // Oldest two (seq 1,2) dropped to honor the cap; floor advanced to 2.
        Assert.Equal(2, b.FloorSeq);
        Assert.False(b.CanReplayFrom(1));        // below forced floor -> truncated
        Assert.True(b.CanReplayFrom(2));

        var replay = b.Replay(2);
        Assert.Equal(new long[] { 3, 4, 5 }, replay.Select(l => l.Seq).ToArray());
    }
}