namespace CodeGenNew.Core.Catalog;

/// <summary> The C# of the facet routes (<see cref="CatalogPlan"/>), written into the product table's <c>&lt;Table&gt;Shape</c> class by <see cref="Api.RowShapeWriter"/>. <c>facets</c> counts the products that carry each value of each attribute
/// for the current keyword, values and ranges (an attribute is counted without its own filter, so the other values of the attribute that was chosen stay on the list); <c>narrow</c> answers the page of products that match.
/// The keyword is read in the product's text columns and in the attribute values. </summary>
public static class CatalogWriter
{
    public static string MapLines(CatalogShape s)
    {
        return "        app.MapGet(\"§R/" + CatalogPlan.FacetsRoute + "\", ([FromServices] §CTX context, [FromQuery(Name = \"q\")] string? q, [FromQuery(Name = \"filter\")] string[]? filter, [FromQuery(Name = \"range\")] string[]? range, [FromQuery(Name = \"maxFacets\")] int? maxFacets, [FromQuery(Name = \"maxValues\")] int? maxValues) => FacetsAsync(context, q, filter, range, maxFacets, maxValues))§AUTH;\n"
            + "        app.MapGet(\"§R/" + CatalogPlan.NarrowRoute + "\", ([FromServices] §CTX context, [FromQuery(Name = \"q\")] string? q, [FromQuery(Name = \"filter\")] string[]? filter, [FromQuery(Name = \"range\")] string[]? range, [FromQuery(Name = \"page\")] int? page, [FromQuery(Name = \"pageSize\")] int? pageSize) => NarrowAsync(context, q, filter, range, page, pageSize))§AUTH;\n";
    }

    /// <summary> The routes' C#: the filter reader, the engine the routes and the desktop list share, and the two answers. </summary>
    public static string Code(CatalogShape s, string contextType)
    {
        return Reader() + Engine(s, contextType) + Wrappers(contextType);
    }

    private static string Reader() => $$"""
    private static IResult? ReadFilters(string[]? filter, string[]? range, out List<(string Name, string[] Values)> filters, out List<(string Name, decimal? Min, decimal? Max)> ranges)
    {
        filters = [];
        ranges = [];
        var byName = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);
        foreach (string raw in filter ?? [])
        {
            int colon = raw.IndexOf(':');
            string name = colon > 0 ? raw[..colon].Trim() : "";
            string value = colon > 0 ? raw[(colon + 1)..].Trim() : "";
            if (name.Length == 0 || value.Length == 0)
                return RowShapeProblems.Problem(400, "{{CatalogWords.BadFilterType}}", "{{CatalogWords.BadFilterTitle}}", "{{CatalogWords.BadFilter}}");
            if (!byName.TryGetValue(name, out var values))
                byName[name] = values = [];
            if (!values.Contains(value, StringComparer.Ordinal))
                values.Add(value);
        }
        filters = byName.Select(p => (p.Key, p.Value.ToArray())).ToList();
        foreach (string raw in range ?? [])
        {
            int last = raw.LastIndexOf(':');
            int first = last > 0 ? raw.LastIndexOf(':', last - 1) : -1;
            string name = first > 0 ? raw[..first].Trim() : "";
            string low = first > 0 ? raw[(first + 1)..last].Trim() : "", high = first > 0 ? raw[(last + 1)..].Trim() : "";
            decimal lowValue = 0, highValue = 0;
            bool lowOk = low.Length == 0 || decimal.TryParse(low, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out lowValue);
            bool highOk = high.Length == 0 || decimal.TryParse(high, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out highValue);
            if (name.Length == 0 || !lowOk || !highOk || (low.Length == 0 && high.Length == 0))
                return RowShapeProblems.Problem(400, "{{CatalogWords.BadFilterType}}", "{{CatalogWords.BadFilterTitle}}", "{{CatalogWords.BadFilter}}");
            ranges.Add((name, low.Length == 0 ? null : lowValue, high.Length == 0 ? null : highValue));
        }
        return null;
    }


""";

    private static string Wrappers(string contextType) => $$"""
    /// <summary> The attributes of the matching products (the most common first) with their values and counts. </summary>
    public static async Task<IResult> FacetsAsync({{contextType}} context, string? q, string[]? filter, string[]? range, int? maxFacets, int? maxValues)
    {
        if (ReadFilters(filter, range, out var filters, out var ranges) is { } bad)
            return bad;
        var (total, groups) = await FacetsCoreAsync(context, q, filters, ranges, Math.Clamp(maxFacets ?? {{CatalogPlan.MaxFacets}}, 1, 50), Math.Clamp(maxValues ?? {{CatalogPlan.MaxValues}}, 1, 100));
        return Results.Ok(new { total, facets = groups });
    }

    /// <summary> The page of products that match the keyword and the filters, in key order. </summary>
    public static async Task<IResult> NarrowAsync({{contextType}} context, string? q, string[]? filter, string[]? range, int? page, int? pageSize)
    {
        if (ReadFilters(filter, range, out var filters, out var ranges) is { } bad)
            return bad;
        if (page is < 1 || pageSize is < 1)
            return RowShapeProblems.Problem(400, "{{CatalogWords.BadPageType}}", "{{CatalogWords.BadPageTitle}}", "{{CatalogWords.BadPage}}");
        int size = Math.Min(pageSize ?? {{CatalogPlan.DefaultPageSize}}, {{CatalogPlan.MaxPageSize}}), number = page ?? 1;
        var (total, items) = await NarrowCoreAsync(context, q, filters, ranges, number, size);
        return Results.Ok(new { total, page = number, pageSize = size, items });
    }


""";

    private static string Contains(ColumnModel c, string term) => c.IsNullable ? $"(x.{c.CSharpName()} != null && x.{c.CSharpName()}!.ToLower().Contains({term}))" : $"x.{c.CSharpName()}.ToLower().Contains({term})";

    private const string FullTextHelper = """
    /// <summary> The text as a CONTAINS condition: each word as a prefix, all of them required. </summary>
    private static string? FullTextQuery(string? text)
    {
        var words = (text ?? "").Split([' ', '\t'], StringSplitOptions.RemoveEmptyEntries).Select(w => w.Replace("\"", "").Replace("*", "")).Where(w => w.Length > 0).ToList();
        return words.Count == 0 ? null : string.Join(" AND ", words.Select(w => "\"" + w + "*\""));
    }


""";

    /// <summary> The part the API routes and the WinUI3 list share: the records, the query of the matching products and the two reads (facets with counts, a page of products). </summary>
    public static string Engine(CatalogShape s, string contextType)
    {
        string product = s.Product.TableName, attr = s.Attributes.TableName;
        string key = s.Key.CSharpName(), aProduct = s.AttributeProduct.CSharpName(), aName = s.AttributeName.CSharpName(), aValue = s.AttributeValue.CSharpName();
        string valueNull = s.AttributeValue.IsNullable ? $"a.{aValue} != null && " : "";
        string valueText = s.AttributeValue.IsNullable ? $"a.{aValue}!" : $"a.{aValue}";
        string attributeHit = s.FullText
            ? $"context.Set<{attr}>().Any(a => a.{aProduct} == x.{key} && EF.Functions.Contains(a.{aValue}, query))"
            : $"context.Set<{attr}>().Any(a => a.{aProduct} == x.{key} && {valueNull}{valueText}.ToLower().Contains(term))";
        string conditions = string.Join(" || ", s.SearchColumns.Select(c => s.FullText ? $"EF.Functions.Contains(x.{c.CSharpName()}, query)" : Contains(c, "term")).Append(attributeHit));
        string search = s.FullText
            ? $"        string? query = FullTextQuery(text);\n        if (query is not null)\n            products = products.Where(x => {conditions});\n"
            : $"        if (!string.IsNullOrWhiteSpace(text))\n        {{\n            string term = text.Trim().ToLower();\n            products = products.Where(x => {conditions});\n        }}\n";
        string Pick(ColumnModel? c) => c is null ? "(string?)null" : "x." + c.CSharpName();
        string numberName = s.AttributeNumber?.CSharpName() ?? "";
        string rangeFilter = s.AttributeNumber is null ? "" : $$"""
        foreach (var (name, min, max) in ranges)
        {
            if (except is not null && string.Equals(name, except, StringComparison.OrdinalIgnoreCase))
                continue;
            string attribute = name;
            decimal? low = min, high = max;
            products = products.Where(x => context.Set<{{attr}}>().Any(a => a.{{aProduct}} == x.{{key}} && a.{{aName}} == attribute && (decimal?)a.{{numberName}} != null && (low == null || (decimal?)a.{{numberName}} >= low) && (high == null || (decimal?)a.{{numberName}} <= high)));
        }

""";
        string bounds = s.AttributeNumber is null ? "decimal? low = null, high = null;" : $$"""
var numbers = context.Set<{{attr}}>().AsNoTracking().Where(a => a.{{aName}} == attribute && (decimal?)a.{{numberName}} != null && scope.Contains(a.{{aProduct}}));
            decimal? low = await numbers.MinAsync(a => (decimal?)a.{{numberName}}), high = await numbers.MaxAsync(a => (decimal?)a.{{numberName}});
""";
        return $$"""
    /// <summary> One value of an attribute and the number of products that carry it. </summary>
    public sealed record FacetValue(string Value, int Count, bool Selected);

    /// <summary> One attribute with its values, the lowest and highest number it has among the matching products (when the attribute table has a number column) and the range that is chosen. </summary>
    public sealed record FacetGroup(string Name, List<FacetValue> Values, decimal? Min, decimal? Max, decimal? SelectedMin, decimal? SelectedMax);

    /// <summary> One product of a page: the key, a title, a code and a detail. </summary>
    public sealed record NarrowItem({{s.Key.CSharpBase()}} Id, string? Title, string? Code, string? Detail);

{{(s.FullText ? FullTextHelper : "")}}    /// <summary> The products that match the keyword (in the product's text columns and the attribute values) and every filter but the one named in <paramref name="except"/>: several values of one attribute are alternatives, different attributes all have to match. </summary>
    private static IQueryable<{{product}}> Matching({{contextType}} context, string? text, List<(string Name, string[] Values)> filters, List<(string Name, decimal? Min, decimal? Max)> ranges, string? except)
    {
        IQueryable<{{product}}> products = context.Set<{{product}}>().AsNoTracking();
{{search}}        foreach (var (name, values) in filters)
        {
            if (except is not null && string.Equals(name, except, StringComparison.OrdinalIgnoreCase))
                continue;
            string attribute = name;
            string[] chosen = values;
            products = products.Where(x => context.Set<{{attr}}>().Any(a => a.{{aProduct}} == x.{{key}} && a.{{aName}} == attribute && chosen.Contains(a.{{aValue}}!)));
        }
{{rangeFilter}}        return products;
    }

    /// <summary> The attributes of the matching products (the most common first) with their values and counts; an attribute is counted without its own filter. </summary>
    public static async Task<(int Total, List<FacetGroup> Facets)> FacetsCoreAsync({{contextType}} context, string? q, List<(string Name, string[] Values)> filters, List<(string Name, decimal? Min, decimal? Max)> ranges, int facetLimit, int valueLimit)
    {
        var matching = Matching(context, q, filters, ranges, null).Select(x => x.{{key}});
        var names = await context.Set<{{attr}}>().AsNoTracking().Where(a => matching.Contains(a.{{aProduct}}))
            .GroupBy(a => a.{{aName}}).Select(g => new { Name = g.Key, Products = g.Select(a => a.{{aProduct}}).Distinct().Count() })
            .OrderByDescending(g => g.Products).ThenBy(g => g.Name).Take(facetLimit).ToListAsync();
        var shown = names.Select(n => n.Name).ToList();
        foreach (string chosenName in filters.Select(f => f.Name).Concat(ranges.Select(r => r.Name)))
            if (!shown.Contains(chosenName, StringComparer.OrdinalIgnoreCase))
                shown.Add(chosenName);
        var groups = new List<FacetGroup>();
        foreach (string name in shown)
        {
            var scope = Matching(context, q, filters, ranges, name).Select(x => x.{{key}});
            var selected = filters.FirstOrDefault(f => string.Equals(f.Name, name, StringComparison.OrdinalIgnoreCase)).Values ?? [];
            var chosenRange = ranges.FirstOrDefault(r => string.Equals(r.Name, name, StringComparison.OrdinalIgnoreCase));
            string attribute = name;
            var counted = await context.Set<{{attr}}>().AsNoTracking().Where(a => a.{{aName}} == attribute && a.{{aValue}} != null && scope.Contains(a.{{aProduct}}))
                .GroupBy(a => a.{{aValue}}!).Select(g => new { Value = g.Key, Count = g.Select(a => a.{{aProduct}}).Distinct().Count() })
                .OrderByDescending(v => v.Count).ThenBy(v => v.Value).Take(valueLimit).ToListAsync();
            var list = counted.Select(v => new FacetValue(v.Value, v.Count, selected.Contains(v.Value))).ToList();
            foreach (string value in selected)
                if (!list.Any(v => v.Value == value))
                    list.Add(new FacetValue(value, 0, true));
            {{bounds}}
            groups.Add(new FacetGroup(name, list, low, high, chosenRange.Name is null ? null : chosenRange.Min, chosenRange.Name is null ? null : chosenRange.Max));
        }
        return (await Matching(context, q, filters, ranges, null).CountAsync(), groups);
    }

    /// <summary> One page of the products that match the keyword and the filters, in key order. </summary>
    public static async Task<(int Total, List<NarrowItem> Items)> NarrowCoreAsync({{contextType}} context, string? q, List<(string Name, string[] Values)> filters, List<(string Name, decimal? Min, decimal? Max)> ranges, int page, int size)
    {
        var matching = Matching(context, q, filters, ranges, null);
        int total = await matching.CountAsync();
        var items = await matching.OrderBy(x => x.{{key}}).Skip((page - 1) * size).Take(size)
            .Select(x => new NarrowItem(x.{{key}}, {{Pick(s.Title)}}, {{Pick(s.Code)}}, {{Pick(s.Detail)}})).ToListAsync();
        return (total, items);
    }


""";
    }
}
