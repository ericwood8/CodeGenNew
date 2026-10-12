namespace Shop.Api.Apis;

using Shop.Api.Repositories;
using Microsoft.EntityFrameworkCore;
using static Microsoft.AspNetCore.Http.TypedResults;

public record SalesOrderSearchResult(IReadOnlyList<SalesOrder> Items, int Page, int PageSize, int TotalCount, int TotalPages);

public class SalesOrderSearchApi<T> : BaseApi<T> where T : class
{
    // the most rows one page may hold; a larger pageSize is lowered to this
    private const int MaxPageSize = 1000;

    public override void Register(WebApplication app)
    {
        BreakIntoStrings(out string _singular, out string plural, out string _apiSubDir);

        app.MapGet(_apiSubDir + "/search", Search)
        .WithName($"Search{plural}")
        .WithOpenApi()
        .Produces<SalesOrderSearchResult>()
        .ProducesProblem(400)
        .ProducesProblem(500);
    }

    private static async Task<IResult> Search(
        [FromServices] ShopContext context,
        [FromQuery] int pageNumber = 1,
        [FromQuery] int pageSize = 100,
        [FromQuery] string? sortBy = null,
        [FromQuery] string? sortDir = null)
    {
        // A page size of 0 or less is an error inside the database (FETCH NEXT 0 ROWS) and a huge one reads the whole table: keep it between 1 and MaxPageSize.
        // The answer carries the size that was used, so a caller that asked for more sees the cap.
        pageSize = Math.Clamp(pageSize, 1, MaxPageSize);
        if (pageNumber < 1)
            return Results.Problem(statusCode: 400, type: "/problems/invalid-page", title: "Invalid page", detail: "pageNumber must be 1 or greater.");
        // the first row of the page is a 32 bit number in the database (OFFSET), so a page number this large would be an overflow error there
        if ((long)(pageNumber - 1) * pageSize > int.MaxValue)
            return Results.Problem(statusCode: 400, type: "/problems/invalid-page", title: "Invalid page", detail: "pageNumber is too large for that pageSize.");

        // The grid's chosen sort: a column name and "asc" / "desc". The routine compares the name with its own list of sortable columns, so an unknown or
        // missing name just gives the default order; the name is never pasted into SQL.
        string? sortColumn = string.IsNullOrWhiteSpace(sortBy) || sortBy.Length > 128 ? null : sortBy.Trim();
        bool sortDescending = string.Equals(sortDir, "desc", StringComparison.OrdinalIgnoreCase);

        var query = SalesOrderSearchQuery.Filter(context.Set<SalesOrder>().AsNoTracking());
        int totalCount = await query.CountAsync();
        var items = await SalesOrderSearchQuery.Sort(query, sortColumn, sortDescending)
            .Skip((pageNumber - 1) * pageSize).Take(pageSize).ToListAsync();

        int totalPages = totalCount == 0 ? 0 : (int)Math.Ceiling(totalCount / (double)pageSize);
        return Ok(new SalesOrderSearchResult(items, pageNumber, pageSize, totalCount, totalPages));
    }
}
