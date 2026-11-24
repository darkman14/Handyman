using Handyman.Api.Cqrs;

namespace Handyman.Api.Features.Trades;

public sealed record TradeResponse(int Id, string Name);

public sealed record ListTradesQuery : IQuery<IReadOnlyList<TradeResponse>>;

public sealed class ListTradesHandler : IQueryHandler<ListTradesQuery, IReadOnlyList<TradeResponse>>
{
    // CONTRACT STUB: the real query (ReadDbContext, fixed order) is built in the next step.
    public Task<IReadOnlyList<TradeResponse>> Handle(ListTradesQuery query, CancellationToken ct) =>
        throw new NotImplementedException();
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
