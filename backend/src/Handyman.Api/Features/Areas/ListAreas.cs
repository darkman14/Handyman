using Handyman.Api.Cqrs;
using Handyman.Api.Data;
using Microsoft.EntityFrameworkCore;

namespace Handyman.Api.Features.Areas;

/// <summary>Kind is "city", "municipality" or "neighborhood".</summary>
public sealed record AreaResponse(int Id, string Name, string Kind);

/// <summary>ParentId null (or empty in the query string) means the top level.</summary>
public sealed record ListAreasQuery(int? ParentId) : IQuery<IReadOnlyList<AreaResponse>>
{
    /// <summary>Empty or missing is the top level; anything that is not an integer is a 400 naming parentId.</summary>
    public static ListAreasQuery From(string? parentId)
    {
        if (string.IsNullOrWhiteSpace(parentId)) return new ListAreasQuery((int?)null);
        if (!int.TryParse(parentId, System.Globalization.NumberStyles.AllowLeadingSign, System.Globalization.CultureInfo.InvariantCulture, out var id))
            throw new Infrastructure.InvalidParameterException("parentId", "parentId must be a whole number.");
        return new ListAreasQuery(id);
    }
}

public sealed class ListAreasHandler : IQueryHandler<ListAreasQuery, IReadOnlyList<AreaResponse>>
{
    private readonly ReadDbContext _db;

    public ListAreasHandler(ReadDbContext db) => _db = db;

    public async Task<IReadOnlyList<AreaResponse>> Handle(ListAreasQuery query, CancellationToken ct)
    {
        var rows = await _db.Areas
            .Where(a => a.ParentId == query.ParentId)
            .Select(a => new { a.Id, a.Name, a.Kind })
            .ToListAsync(ct);

        // Sorted in memory: the database collation does not know Serbian Latin (Č after C, Đ after D).
        return rows
            .OrderBy(a => a.Name, SerbianLatinComparer.Instance)
            .ThenBy(a => a.Id)
            .Select(a => new AreaResponse(a.Id, a.Name, a.Kind.ToString().ToLowerInvariant()))
            .ToList();
    }
}

/// <summary>Serbian Latin alphabetical order, case-insensitive; independent of the host's ICU/globalization mode.</summary>
public sealed class SerbianLatinComparer : IComparer<string>
{
    public static readonly SerbianLatinComparer Instance = new();

    private const string Alphabet = "abcčćdđefghijklmnoprsštuvzž";

    public int Compare(string? x, string? y)
    {
        if (ReferenceEquals(x, y)) return 0;
        if (x is null) return -1;
        if (y is null) return 1;

        var a = x.ToLowerInvariant();
        var b = y.ToLowerInvariant();
        for (var i = 0; i < Math.Min(a.Length, b.Length); i++)
        {
            var c = Rank(a[i]).CompareTo(Rank(b[i]));
            if (c != 0) return c;
        }
        var byLength = a.Length.CompareTo(b.Length);
        return byLength != 0 ? byLength : string.CompareOrdinal(a, b);
    }

    // Letters in alphabet order, then everything else (space, digits, other letters) by code point after them.
    private static int Rank(char ch)
    {
        var i = Alphabet.IndexOf(ch);
        return i >= 0 ? 1000 + i : ch < 1000 ? ch - 1000 : ch + 1000;
    }
}

public static class ListAreas
{
    public static void Map(RouteGroupBuilder api) =>
        api.MapGet("/areas", async (string? parentId, IQueryHandler<ListAreasQuery, IReadOnlyList<AreaResponse>> handler, CancellationToken ct) =>
                Results.Ok(await handler.Handle(ListAreasQuery.From(parentId), ct)))
            .AllowAnonymous()
            .WithName("ListAreas")
            .Produces<IReadOnlyList<AreaResponse>>(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status400BadRequest);
}
