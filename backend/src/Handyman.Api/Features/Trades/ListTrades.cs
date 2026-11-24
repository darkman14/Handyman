using Handyman.Api.Cqrs;
using Handyman.Api.Data;
using Microsoft.EntityFrameworkCore;

namespace Handyman.Api.Features.Trades;

public sealed record TradeResponse(int Id, string Name);

public sealed record ListTradesQuery : IQuery<IReadOnlyList<TradeResponse>>;

public sealed class ListTradesHandler : IQueryHandler<ListTradesQuery, IReadOnlyList<TradeResponse>>
{
    // The display order is product-defined (urgent repairs first) and lives here, keyed by name: ids and row order
    // are not a contract (a database that had trades before the reference migration can hold them in any order).
    private static readonly string[] DisplayOrder =
        ["Appliance repair", "Electrical", "Plumbing", "Heating & AC", "Painting", "Carpentry"];

    private readonly ReadDbContext _db;

    public ListTradesHandler(ReadDbContext db) => _db = db;

    public async Task<IReadOnlyList<TradeResponse>> Handle(ListTradesQuery query, CancellationToken ct)
    {
        // Only the six reference trades (custom rows on a shared database are not offered); at most a handful of rows.
        var rows = await _db.Trades
            .Where(t => DisplayOrder.Contains(t.Name))
            .Select(t => new TradeResponse(t.Id, t.Name))
            .ToListAsync(ct);

        // Duplicate names (not expected): keep the lowest id, as the reference migration does.
        return rows
            .GroupBy(t => t.Name)
            .Select(g => g.MinBy(t => t.Id)!)
            .OrderBy(t => Array.IndexOf(DisplayOrder, t.Name))
            .ToList();
    }
}

public static class ListTrades
{
    public static void Map(RouteGroupBuilder api) =>
        api.MapGet("/trades", async (IQueryHandler<ListTradesQuery, IReadOnlyList<TradeResponse>> handler, CancellationToken ct) =>
                Results.Ok(await handler.Handle(new ListTradesQuery(), ct)))
            .AllowAnonymous()
            .WithName("ListTrades")
            .Produces<IReadOnlyList<TradeResponse>>(StatusCodes.Status200OK);
}
