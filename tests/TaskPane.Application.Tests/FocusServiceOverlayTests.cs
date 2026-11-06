using TaskPane.Application.Models;
using TaskPane.Application.Services;
using TaskPane.Core.Entities;
using TaskPane.Core.Enums;
using TaskPane.Core.Ids;

namespace TaskPane.Application.Tests;

public sealed class FocusServiceOverlayTests
{
    [Fact]
    public void OrderForOverlay_puts_waiting_then_blocked_after_ready()
    {
        var ready = Root("Ready");
        var waiting = Root("Waiting", waitingFor: "Vendor");
        var blocked = Root("Blocked");
        var waitingAndBlocked = Root("Both", waitingFor: "Also waiting");

        var ordered = FocusService.OrderForOverlay(
            [waiting, blocked, ready, waitingAndBlocked],
            new HashSet<TaskId> { blocked.Id, waitingAndBlocked.Id });

        Assert.Equal(
            new[] { ready.Id, waiting.Id, blocked.Id, waitingAndBlocked.Id },
            ordered.Select(t => t.Id).ToArray());
    }

    [Fact]
    public void OrderForOverlay_preserves_relative_order_within_band()
    {
        var a = Root("A");
        var b = Root("B");
        var w1 = Root("W1", waitingFor: "X");
        var w2 = Root("W2", waitingFor: "Y");

        var ordered = FocusService.OrderForOverlay(
            [w1, a, w2, b],
            blockedTaskIds: new HashSet<TaskId>());

        Assert.Equal(new[] { a.Id, b.Id, w1.Id, w2.Id }, ordered.Select(t => t.Id).ToArray());
    }

    [Fact]
    public void Matches_overlay_visibility_without_focus_bucket_includes_all_buckets()
    {
        var inbox = Root("Inbox", FocusBucket.Inbox);
        var today = Root("Today", FocusBucket.Today);
        var next = Root("Next", FocusBucket.Next);
        var sleeping = Root("Sleep", FocusBucket.Someday);
        sleeping.SleepIndefinitely = true;

        var query = new TaskQuery { OverlayVisibility = true };
        var now = DateTimeOffset.Parse("2026-11-06T12:00:00Z");

        Assert.True(FocusService.Matches(inbox, query, now, taggedTaskIds: null, parent: null));
        Assert.True(FocusService.Matches(today, query, now, taggedTaskIds: null, parent: null));
        Assert.True(FocusService.Matches(next, query, now, taggedTaskIds: null, parent: null));
        Assert.False(FocusService.Matches(sleeping, query, now, taggedTaskIds: null, parent: null));
    }

    private static TaskItem Root(string title, FocusBucket bucket = FocusBucket.Inbox, string? waitingFor = null)
        => new()
        {
            Id = TaskId.New(),
            Title = title,
            FocusBucket = bucket,
            Status = TaskLifecycleStatus.Open,
            WaitingFor = waitingFor
        };
}
