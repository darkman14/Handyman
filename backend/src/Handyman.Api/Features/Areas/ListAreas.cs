using Handyman.Api.Cqrs;
using Handyman.Api.Data;

namespace Handyman.Api.Features.Areas;

/// <summary>Kind is "city", "municipality" or "neighborhood".</summary>
public sealed record AreaResponse(int Id, string Name, string Kind);

/// <summary>ParentId null (or empty in the query string) means the top level.</summary>
public sealed record ListAreasQuery(int? ParentId) : IQuery<IReadOnlyList<AreaResponse>>;

public sealed class ListAreasHandler : IQueryHandler<ListAreasQuery, IReadOnlyList<AreaResponse>>
{
    private readonly ReadDbContext _db;

    public ListAreasHandler(ReadDbContext db) => _db = db;

    public Task<IReadOnlyList<AreaResponse>> Handle(ListAreasQuery query, CancellationToken ct) =>
        throw new NotImplementedException();
}

public static class ListAreas
{
    public static void Map(RouteGroupBuilder api) =>
        api.MapGet("/areas", async (int? parentId, IQueryHandler<ListAreasQuery, IReadOnlyList<AreaResponse>> handler, CancellationToken ct) =>
                Results.Ok(await handler.Handle(new ListAreasQuery(parentId), ct)))
            .AllowAnonymous()
            .WithName("ListAreas")
            .Produces<IReadOnlyList<AreaResponse>>(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status400BadRequest);
}
