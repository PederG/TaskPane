using TaskPane.Core.Dependencies;
using TaskPane.Core.Entities;
using TaskPane.Core.Enums;
using TaskPane.Core.Ids;

namespace TaskPane.Core.Tests;

public sealed class DependencyGraphTests
{
    [Fact]
    public void Rejects_self_edge_as_cycle()
    {
        var id = TaskId.New();
        Assert.True(DependencyGraph.WouldCreateCycle([], id, id));
    }

    [Fact]
    public void Detects_indirect_cycle_A_B_A()
    {
        var a = TaskId.New();
        var b = TaskId.New();
        var existing = new[]
        {
            Edge(a, b)
        };

        Assert.True(DependencyGraph.WouldCreateCycle(existing, b, a));
        Assert.False(DependencyGraph.WouldCreateCycle(existing, a, TaskId.New()));
    }

    [Fact]
    public void Detects_longer_cycle()
    {
        var a = TaskId.New();
        var b = TaskId.New();
        var c = TaskId.New();
        var existing = new[]
        {
            Edge(a, b),
            Edge(b, c)
        };

        Assert.True(DependencyGraph.WouldCreateCycle(existing, c, a));
        Assert.False(DependencyGraph.WouldCreateCycle(existing, a, c));
    }

    [Fact]
    public void Blocked_when_predecessor_open_clears_when_completed()
    {
        var pred = Task(open: true);
        var succ = Task(open: true);
        var edges = new[] { Edge(pred.Id, succ.Id) };
        var map = new Dictionary<TaskId, TaskItem> { [pred.Id] = pred, [succ.Id] = succ };

        var blocked = DependencyGraph.EvaluateBlocked(succ.Id, edges, map);
        Assert.True(blocked.IsBlocked);
        Assert.Contains(pred.Id, blocked.UnfinishedPredecessorIds);

        pred.Status = TaskLifecycleStatus.Completed;
        pred.CompletedUtc = DateTimeOffset.UtcNow;
        var ready = DependencyGraph.EvaluateBlocked(succ.Id, edges, map);
        Assert.False(ready.IsBlocked);
    }

    [Fact]
    public void Deleted_predecessor_keeps_blocked_with_warning()
    {
        var pred = Task(open: true);
        pred.DeletedUtc = DateTimeOffset.UtcNow;
        var succ = Task(open: true);
        var edges = new[] { Edge(pred.Id, succ.Id) };
        var map = new Dictionary<TaskId, TaskItem> { [pred.Id] = pred, [succ.Id] = succ };

        var state = DependencyGraph.EvaluateBlocked(succ.Id, edges, map);
        Assert.True(state.IsBlocked);
        Assert.True(state.HasDeletedPredecessorWarning);
        Assert.Contains(pred.Id, state.DeletedOrMissingPredecessorIds);
    }

    [Fact]
    public void Missing_predecessor_surfaces_warning()
    {
        var missing = TaskId.New();
        var succ = Task(open: true);
        var edges = new[] { Edge(missing, succ.Id) };
        var map = new Dictionary<TaskId, TaskItem> { [succ.Id] = succ };

        var state = DependencyGraph.EvaluateBlocked(succ.Id, edges, map);
        Assert.True(state.IsBlocked);
        Assert.True(state.HasDeletedPredecessorWarning);
    }

    private static DependencyEdge Edge(TaskId pred, TaskId succ) => new()
    {
        Id = DependencyId.New(),
        PredecessorTaskId = pred,
        SuccessorTaskId = succ,
        Kind = DependencyKind.Required,
        CreatedUtc = DateTimeOffset.UtcNow
    };

    private static TaskItem Task(bool open) => new()
    {
        Id = TaskId.New(),
        Title = "t",
        Status = open ? TaskLifecycleStatus.Open : TaskLifecycleStatus.Completed,
        CreatedUtc = DateTimeOffset.UtcNow,
        UpdatedUtc = DateTimeOffset.UtcNow
    };
}
