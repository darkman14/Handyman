using System.Data.Common;
using Microsoft.EntityFrameworkCore.Diagnostics;

namespace Handyman.Api.Data;

/// <summary>
/// Makes <see cref="ReadDbContext"/> unable to write even through paths that skip SaveChanges
/// (ExecuteUpdate, ExecuteDelete, Database.ExecuteSql*). Two layers:
/// 1. Every opened connection is put in a read-only session (Postgres refuses any write, even inside a CTE).
///    It runs on every open, so it survives Npgsql's pool reset (DISCARD ALL) and works with the Supabase session pooler.
///    A transaction-mode pooler may hand out another server connection per transaction, so:
/// 2. Every command EF sends must be a SELECT/WITH query, and never a non-query.
/// </summary>
public sealed class ReadOnlySessionInterceptor : DbConnectionInterceptor, IDbCommandInterceptor
{
    private const string ReadOnlySql = "SET SESSION CHARACTERISTICS AS TRANSACTION READ ONLY";
    private const string Refused = "ReadDbContext is read-only: use AppDbContext in a command handler to write.";

    public override void ConnectionOpened(DbConnection connection, ConnectionEndEventData eventData)
    {
        using var command = connection.CreateCommand();
        command.CommandText = ReadOnlySql;
        command.ExecuteNonQuery();
    }

    public override async Task ConnectionOpenedAsync(DbConnection connection, ConnectionEndEventData eventData, CancellationToken cancellationToken = default)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = ReadOnlySql;
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    public InterceptionResult<DbDataReader> ReaderExecuting(DbCommand command, CommandEventData eventData, InterceptionResult<DbDataReader> result)
    {
        RequireSelect(command);
        return result;
    }

    public ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(DbCommand command, CommandEventData eventData, InterceptionResult<DbDataReader> result, CancellationToken cancellationToken = default)
    {
        RequireSelect(command);
        return new(result);
    }

    public InterceptionResult<object> ScalarExecuting(DbCommand command, CommandEventData eventData, InterceptionResult<object> result)
    {
        RequireSelect(command);
        return result;
    }

    public ValueTask<InterceptionResult<object>> ScalarExecutingAsync(DbCommand command, CommandEventData eventData, InterceptionResult<object> result, CancellationToken cancellationToken = default)
    {
        RequireSelect(command);
        return new(result);
    }

    public InterceptionResult<int> NonQueryExecuting(DbCommand command, CommandEventData eventData, InterceptionResult<int> result) =>
        throw new InvalidOperationException(Refused);

    public ValueTask<InterceptionResult<int>> NonQueryExecutingAsync(DbCommand command, CommandEventData eventData, InterceptionResult<int> result, CancellationToken cancellationToken = default) =>
        throw new InvalidOperationException(Refused);

    private static void RequireSelect(DbCommand command)
    {
        // EF can prefix its SQL with "-- tag" comment lines (TagWith); skip those, refuse anything else that is not a query.
        var text = command.CommandText.TrimStart();
        while (text.StartsWith("--", StringComparison.Ordinal))
        {
            var end = text.IndexOf('\n');
            if (end < 0) break;
            text = text[(end + 1)..].TrimStart();
        }
        if (!text.StartsWith("SELECT", StringComparison.OrdinalIgnoreCase) && !text.StartsWith("WITH", StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException(Refused);
    }
}
