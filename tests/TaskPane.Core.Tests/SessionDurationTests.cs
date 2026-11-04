using TaskPane.Core.Entities;
using TaskPane.Core.Ids;
using TaskPane.Core.Time;

namespace TaskPane.Core.Tests;

public sealed class SessionDurationTests
{
    [Fact]
    public void Direct_total_ignores_discarded_and_clamps_negative()
    {
        var now = DateTimeOffset.Parse("2026-11-04T12:00:00Z");
        var sessions = new[]
        {
            new TimeSession
            {
                Id = SessionId.New(),
                TaskId = TaskId.New(),
                StartedUtc = now.AddMinutes(-30),
                EndedUtc = now.AddMinutes(-10),
                CreatedUtc = now,
                UpdatedUtc = now
            },
            new TimeSession
            {
                Id = SessionId.New(),
                TaskId = TaskId.New(),
                StartedUtc = now.AddMinutes(-5),
                EndedUtc = now.AddMinutes(-15),
                CreatedUtc = now,
                UpdatedUtc = now
            },
            new TimeSession
            {
                Id = SessionId.New(),
                TaskId = TaskId.New(),
                StartedUtc = now.AddMinutes(-60),
                EndedUtc = now.AddMinutes(-50),
                IsDiscarded = true,
                CreatedUtc = now,
                UpdatedUtc = now
            }
        };

        Assert.Equal(TimeSpan.FromMinutes(20), SessionDuration.DirectTotal(sessions, now));
    }

    [Fact]
    public void Rolled_up_adds_children_once()
    {
        var now = DateTimeOffset.Parse("2026-11-04T12:00:00Z");
        var parentSessions = new[]
        {
            new TimeSession
            {
                Id = SessionId.New(),
                TaskId = TaskId.New(),
                StartedUtc = now.AddMinutes(-10),
                EndedUtc = now,
                CreatedUtc = now,
                UpdatedUtc = now
            }
        };
        var childA = new[]
        {
            new TimeSession
            {
                Id = SessionId.New(),
                TaskId = TaskId.New(),
                StartedUtc = now.AddMinutes(-40),
                EndedUtc = now.AddMinutes(-20),
                CreatedUtc = now,
                UpdatedUtc = now
            }
        };
        var childB = new[]
        {
            new TimeSession
            {
                Id = SessionId.New(),
                TaskId = TaskId.New(),
                StartedUtc = now.AddMinutes(-15),
                EndedUtc = now.AddMinutes(-10),
                CreatedUtc = now,
                UpdatedUtc = now
            }
        };

        var rolled = SessionDuration.RolledUpTotal(parentSessions, [childA, childB], now);
        Assert.Equal(TimeSpan.FromMinutes(35), rolled);
    }

    [Fact]
    public void FormatClock_pads_hours_minutes_seconds()
    {
        Assert.Equal("01:24:38", SessionDuration.FormatClock(new TimeSpan(1, 24, 38)));
        Assert.Equal("00:00:05", SessionDuration.FormatClock(TimeSpan.FromSeconds(5)));
        Assert.Equal("00:00:00", SessionDuration.FormatClock(TimeSpan.FromSeconds(-3)));
    }
}
