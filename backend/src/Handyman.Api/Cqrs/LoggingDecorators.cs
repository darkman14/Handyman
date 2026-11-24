using System.Diagnostics;
using Microsoft.Extensions.Logging;

namespace Handyman.Api.Cqrs;

internal static class UseCaseName
{
    /// <summary>"HireBid" for HireBid.Command, otherwise the type name. Never includes request contents.</summary>
    public static string Of(Type request) =>
        request.DeclaringType is { } outer && request.Name is "Command" or "Query" ? outer.Name : request.Name;

    public static long Elapsed(long started) => (long)Stopwatch.GetElapsedTime(started).TotalMilliseconds;
}

public sealed class LoggingCommandDecorator<TCommand, TResult>(
    ICommandHandler<TCommand, TResult> inner,
    ILogger<LoggingCommandDecorator<TCommand, TResult>> logger) : ICommandHandler<TCommand, TResult>
    where TCommand : ICommand<TResult>
{
    private static readonly string Name = UseCaseName.Of(typeof(TCommand));

    public async Task<TResult> Handle(TCommand command, CancellationToken ct)
    {
        var started = Stopwatch.GetTimestamp();
        try
        {
            var result = await inner.Handle(command, ct);
            logger.LogInformation("Command {UseCase} {Outcome} in {DurationMs} ms",
                Name, "success", UseCaseName.Elapsed(started));
            return result;
        }
        catch (Exception error)
        {
            // Only the exception type: messages may carry user data.
            logger.LogWarning("Command {UseCase} {Outcome} in {DurationMs} ms ({ErrorType})",
                Name, "failure", UseCaseName.Elapsed(started), error.GetType().Name);
            throw;
        }
    }
}

public sealed class LoggingQueryDecorator<TQuery, TResult>(
    IQueryHandler<TQuery, TResult> inner,
    ILogger<LoggingQueryDecorator<TQuery, TResult>> logger) : IQueryHandler<TQuery, TResult>
    where TQuery : IQuery<TResult>
{
    private const int SlowMilliseconds = 500;
    private static readonly string Name = UseCaseName.Of(typeof(TQuery));

    public async Task<TResult> Handle(TQuery query, CancellationToken ct)
    {
        var started = Stopwatch.GetTimestamp();
        try
        {
            var result = await inner.Handle(query, ct);
            var ms = UseCaseName.Elapsed(started);
            if (ms > SlowMilliseconds)
                logger.LogWarning("Query {UseCase} {Outcome} slowly in {DurationMs} ms", Name, "success", ms);
            else
                logger.LogInformation("Query {UseCase} {Outcome} in {DurationMs} ms", Name, "success", ms);
            return result;
        }
        catch (Exception error)
        {
            logger.LogWarning("Query {UseCase} {Outcome} in {DurationMs} ms ({ErrorType})",
                Name, "failure", UseCaseName.Elapsed(started), error.GetType().Name);
            throw;
        }
    }
}
