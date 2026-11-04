using Microsoft.Extensions.Logging.Abstractions;
using TaskPane.Application.Models;
using TaskPane.Application.Services;
using TaskPane.Core.Entities;
using TaskPane.Core.Enums;
using TaskPane.Core.Errors;
using TaskPane.Core.Ids;
using TaskPane.Core.Interfaces;

namespace TaskPane.Application.Tests;

public sealed class TaskServiceTests
{
    [Fact]
    public async Task Create_requires_title()
    {
        var sut = CreateSut(out _);
        await Assert.ThrowsAsync<BusinessException>(() => sut.CreateAsync(new CreateTaskRequest { Title = " " }));
    }

    [Fact]
    public async Task Create_defaults_to_inbox_normal()
    {
        var sut = CreateSut(out var repo);
        var task = await sut.CreateAsync(new CreateTaskRequest { Title = "Capture me" });
        Assert.Equal(FocusBucket.Inbox, task.FocusBucket);
        Assert.Equal(TaskPriority.Normal, task.Priority);
        Assert.Single(repo.Items);
    }

    [Fact]
    public async Task Rejects_grandchild()
    {
        var sut = CreateSut(out _);
        var parent = await sut.CreateAsync(new CreateTaskRequest { Title = "Parent" });
        var child = await sut.CreateAsync(new CreateTaskRequest { Title = "Child", ParentTaskId = parent.Id });

        var ex = await Assert.ThrowsAsync<BusinessException>(() =>
            sut.CreateAsync(new CreateTaskRequest { Title = "Grand", ParentTaskId = child.Id }));
        Assert.Equal(ValidationCodes.ParentIsChild, ex.Code);
    }

    [Fact]
    public async Task Soft_delete_parent_cascades_and_restore_works()
    {
        var sut = CreateSut(out var repo);
        var parent = await sut.CreateAsync(new CreateTaskRequest { Title = "Parent" });
        await sut.CreateAsync(new CreateTaskRequest { Title = "Child", ParentTaskId = parent.Id });

        await sut.SoftDeleteAsync(parent.Id);
        Assert.Equal(2, repo.Items.Count(t => t.IsDeleted));
        var batch = repo.Items.First(t => t.Id.Equals(parent.Id)).DeletionBatchId!;
        await sut.RestoreBatchAsync(batch.Value);
        Assert.All(repo.Items, t => Assert.False(t.IsDeleted));
    }

    [Fact]
    public async Task Complete_parent_with_open_children_requires_choice()
    {
        var sut = CreateSut(out _);
        var parent = await sut.CreateAsync(new CreateTaskRequest { Title = "Parent" });
        await sut.CreateAsync(new CreateTaskRequest { Title = "Child", ParentTaskId = parent.Id });

        var blocked = await sut.CompleteAsync(parent.Id, null);
        Assert.False(blocked.Completed);
        Assert.Equal(1, blocked.IncompleteChildCount);

        var done = await sut.CompleteAsync(parent.Id, ParentCompletionChoice.CompleteParentAndChildren);
        Assert.True(done.Completed);
    }

    [Fact]
    public async Task Manual_order_and_focus_are_independent()
    {
        var sut = CreateSut(out _);
        var a = await sut.CreateAsync(new CreateTaskRequest { Title = "A", FocusBucket = FocusBucket.Today, Priority = TaskPriority.Low });
        var b = await sut.CreateAsync(new CreateTaskRequest { Title = "B", FocusBucket = FocusBucket.Today, Priority = TaskPriority.High });
        await sut.ReorderAsync(b.Id, a.ManualSortKey - 10);

        var list = await sut.ListVisibleRootsAsync(FocusBucket.Today);
        Assert.Equal(b.Id, list[0].Id);
        Assert.Equal(TaskPriority.High, list[0].Priority);
    }

    private static TaskService CreateSut(out InMemoryTaskRepository repo)
    {
        repo = new InMemoryTaskRepository();
        return new TaskService(repo, new PassthroughUnitOfWork(), NullLogger<TaskService>.Instance);
    }

    private sealed class PassthroughUnitOfWork : IUnitOfWork
    {
        public Task ExecuteInTransactionAsync(Func<CancellationToken, Task> action, CancellationToken cancellationToken = default)
            => action(cancellationToken);
    }

    private sealed class InMemoryTaskRepository : ITaskRepository
    {
        public List<TaskItem> Items { get; } = [];

        public Task<TaskItem?> GetByIdAsync(TaskId id, CancellationToken cancellationToken = default)
            => Task.FromResult(Items.FirstOrDefault(t => t.Id.Equals(id)));

        public Task<IReadOnlyList<TaskItem>> ListAsync(bool includeDeleted = false, CancellationToken cancellationToken = default)
            => Task.FromResult<IReadOnlyList<TaskItem>>(Items.Where(t => includeDeleted || !t.IsDeleted).ToList());

        public Task<IReadOnlyList<TaskItem>> ListChildrenAsync(TaskId parentId, bool includeDeleted = false, CancellationToken cancellationToken = default)
            => Task.FromResult<IReadOnlyList<TaskItem>>(
                Items.Where(t => t.ParentTaskId.Equals(parentId) && (includeDeleted || !t.IsDeleted)).ToList());

        public Task InsertAsync(TaskItem task, CancellationToken cancellationToken = default)
        {
            Items.Add(Clone(task));
            return Task.CompletedTask;
        }

        public Task UpdateAsync(TaskItem task, CancellationToken cancellationToken = default)
        {
            var idx = Items.FindIndex(t => t.Id.Equals(task.Id));
            Items[idx] = Clone(task);
            return Task.CompletedTask;
        }

        public Task SoftDeleteBatchAsync(IReadOnlyList<TaskId> ids, Guid deletionBatchId, DateTimeOffset deletedUtc, CancellationToken cancellationToken = default)
        {
            foreach (var item in Items.Where(t => ids.Contains(t.Id)))
            {
                item.DeletedUtc = deletedUtc;
                item.DeletionBatchId = deletionBatchId;
                item.UpdatedUtc = deletedUtc;
                item.Revision++;
            }

            return Task.CompletedTask;
        }

        public Task RestoreBatchAsync(Guid deletionBatchId, CancellationToken cancellationToken = default)
        {
            foreach (var item in Items.Where(t => t.DeletionBatchId == deletionBatchId))
            {
                item.DeletedUtc = null;
                item.DeletionBatchId = null;
                item.UpdatedUtc = DateTimeOffset.UtcNow;
                item.Revision++;
            }

            return Task.CompletedTask;
        }

        public Task<IReadOnlyList<TaskItem>> ListByDeletionBatchAsync(Guid deletionBatchId, CancellationToken cancellationToken = default)
            => Task.FromResult<IReadOnlyList<TaskItem>>(Items.Where(t => t.DeletionBatchId == deletionBatchId).ToList());

        public Task<double> GetNextSortKeyAsync(TaskId? parentId, FocusBucket? focusBucket, CancellationToken cancellationToken = default)
        {
            var query = Items.Where(t => !t.IsDeleted);
            query = parentId is { } pid
                ? query.Where(t => t.ParentTaskId.Equals(pid))
                : query.Where(t => t.IsRoot && (focusBucket is null || t.FocusBucket == focusBucket));
            var max = query.Select(t => t.ManualSortKey).DefaultIfEmpty(0).Max();
            return Task.FromResult(max + 1000);
        }

        private static TaskItem Clone(TaskItem task) => new()
        {
            Id = task.Id,
            ParentTaskId = task.ParentTaskId,
            Title = task.Title,
            Notes = task.Notes,
            Priority = task.Priority,
            FocusBucket = task.FocusBucket,
            Status = task.Status,
            ManualSortKey = task.ManualSortKey,
            CreatedUtc = task.CreatedUtc,
            UpdatedUtc = task.UpdatedUtc,
            CompletedUtc = task.CompletedUtc,
            DeletedUtc = task.DeletedUtc,
            DeletionBatchId = task.DeletionBatchId,
            Revision = task.Revision
        };
    }
}
