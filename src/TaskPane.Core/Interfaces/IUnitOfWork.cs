namespace TaskPane.Core.Interfaces;

public interface IUnitOfWork
{
    Task ExecuteInTransactionAsync(
        Func<CancellationToken, Task> action,
        CancellationToken cancellationToken = default);
}
