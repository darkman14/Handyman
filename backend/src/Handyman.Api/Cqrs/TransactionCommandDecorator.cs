using Handyman.Api.Data;
using Microsoft.Extensions.Logging;

namespace Handyman.Api.Cqrs;

/// <summary>Runs the handler in one transaction: commit on success, roll back on any exception (including cancellation).
/// Pushes queued by the handler are handed to the push queue only after the commit.</summary>
public sealed class TransactionCommandDecorator<TCommand, TResult>(
    ICommandHandler<TCommand, TResult> inner,
    AppDbContext db,
    PendingPushes pushes,
    IPushQueue queue,
    ILogger<TransactionCommandDecorator<TCommand, TResult>> logger) : ICommandHandler<TCommand, TResult>
    where TCommand : ICommand<TResult>
{
    public async Task<TResult> Handle(TCommand command, CancellationToken ct)
    {
        await using var tx = await db.Database.BeginTransactionAsync(ct);
        TResult result;
        try
        {
            result = await inner.Handle(command, ct);
            await tx.CommitAsync(ct);
        }
        catch
        {
            pushes.Clear();
            try { await tx.RollbackAsync(CancellationToken.None); }
            catch (Exception rollbackError)
            {
                logger.LogError("Rollback failed: {ErrorType}", rollbackError.GetType().Name);
            }
            throw; // the caller sees the original exception
        }

        Flush();
        return result;
    }

    private void Flush()
    {
        var items = pushes.Items.ToArray();
        pushes.Clear();
        foreach (var push in items)
        {
            try { queue.Enqueue(push); }
            catch (Exception error)
            {
                // The change is already committed: a lost push must not turn into a failed command.
                logger.LogError("Push could not be queued: {ErrorType}", error.GetType().Name);
            }
        }
    }
}
