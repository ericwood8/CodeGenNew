namespace CodeGenNew.Core.Price;

/// <summary> The C# of the price rule and the price lookup of a <see cref="PriceListShape"/>, written into the table's <c>&lt;Table&gt;Shape</c> class by <see cref="RowShapeWriter"/>. The minimum quantities are compared in
/// memory, as decimals, so a SQLite database (which holds a decimal as text) answers as the others do. </summary>
public static class PriceListWriter
{
    /// <summary> <c>CheckPriceAsync</c>, <c>FindPriceAsync</c> and <c>PriceOnAsync</c>. </summary>
    public static string Code(PriceListShape s, string contextType)
    {
        string item = s.Item.CSharpBase();
        string group = s.Group is null ? "" : "\n        listed = held is null ? listed.Where(l => l." + s.Group.CSharpName() + " == null) : listed.Where(l => l." + s.Group.CSharpName() + " != null && l." + s.Group.CSharpName() + "!.ToUpper() == held);";
        bool scoped = s.Owner is not null || s.ListOwner is not null;
        string scopeLists = s.ListOwner is null ? "" : $"\n            if (visible is not null)\n                listed = listed.Where(l => visible.Contains(l.{s.ListOwner.CSharpName()}));";
        string scopeRows = s.Owner is null ? "" : $"\n            if (visible is not null)\n                rows = rows.Where(x => visible.Contains(x.{s.Owner.CSharpName()})).ToList();";
        var values = new (string Token, string Value)[]
        {
            ("§SCOPEPARAM", scoped ? ", int[]? visible" : ""), ("§SCOPELISTS", scopeLists), ("§SCOPEROWS", scopeRows), ("§SCOPEARG", scoped ? ", visible" : ""), ("§HTTPPARAM", scoped ? "HttpContext http, " : ""),
            ("§SCOPEVALUE", scoped ? "\n        int[]? visible = (await http.RequestServices.GetRequiredService<IOwnerScope>().VisibleOwnersAsync(http.User, http.RequestAborted))?.ToArray();" : ""),
            ("§CTX", contextType), ("§T", s.Table.TableName), ("§LT", s.ListTable.TableName), ("§K", s.Key.CSharpName()), ("§LK", s.ListKey.CSharpName()), ("§LIST", s.List.CSharpName()), ("§ITEM", s.Item.CSharpName()),
            ("§MIN", s.MinQuantity.CSharpName()), ("§PRICE", s.Price.CSharpName()), ("§CUR", s.Currency.CSharpName()), ("§FROM", s.ValidFrom.CSharpName()), ("§TO", s.ValidTo.CSharpName()), ("§IT", item),
            ("§GROUPLINE", group), ("§GROUPPARAM", s.Group is null ? "" : ", string? customerGroup"), ("§GROUPARG", s.Group is null ? "null" : "held"), ("§GROUPVALUE", s.Group is null ? "" : "\n        string? held = string.IsNullOrWhiteSpace(customerGroup) ? null : customerGroup.Trim().ToUpperInvariant();"),
            ("§MISSING", "$\"" + PriceWords.MissingFormat.Replace("{0}", "{item}").Replace("{1}", "{asked}") + "\""),
            ("§LISTCHECK", s.Owner is not null && s.ListOwner is not null ? "        if (!await context.Set<§LT>().AsNoTracking().AnyAsync(l => l.§LK == row.§LIST && l." + s.ListOwner.CSharpName() + " == row." + s.Owner.CSharpName() + "))\n            return RowShapeProblems.PriceInvalid(\"" + PriceWords.ListMissing + "\");\n"
                : s.IsLimited && s.Owner is null && s.ListOwner is null ? "        if (!await context.Set<§LT>().AsNoTracking().AnyAsync(l => l.§LK == row.§LIST))\n            return RowShapeProblems.PriceInvalid(\"" + PriceWords.ListMissing + "\");\n" : ""), ("§W_MIN", PriceWords.QuantityNotPositive), ("§W_PRICE", PriceWords.PriceNegative), ("§W_TAKEN", PriceWords.Taken), ("§W_LOOKUP", PriceWords.NeedsLookup), ("§W_QTY", PriceWords.AskedQuantity)
        };
        return TemplateText.FillAll(Text, values);
    }

    /// <summary> The route that answers the price in force, mapped next to the table's other routes. </summary>
    public static string MapLine(PriceListShape s) =>
        "        app.MapGet(\"§R/price\", (" + (s.Owner is not null || s.ListOwner is not null ? "HttpContext http, " : "") + "[FromServices] §CTX context, [FromQuery(Name = \"item\")] " + s.Item.CSharpBase() + "? item, [FromQuery(Name = \"quantity\")] decimal? quantity, [FromQuery(Name = \"on\")] DateTime? day, [FromQuery(Name = \"currency\")] string? currency"
        + (s.Group is null ? "" : ", [FromQuery(Name = \"group\")] string? customerGroup") + ") => PriceOnAsync(" + (s.Owner is not null || s.ListOwner is not null ? "http, " : "") + "context, item, quantity, day, currency" + (s.Group is null ? "" : ", customerGroup") + "))§AUTH;\n";

    /// <summary> The lines a save runs before it writes the row. </summary>
    public const string SaveLines = "        if (await CheckPriceAsync(context, row) is { } priceProblem)\n            return priceProblem;\n";

    private const string Text = """
    /// <summary> The minimum quantity is above zero, the price is not negative, and no other row of the list holds a price for the item from the same quantity (409). </summary>
    public static async Task<IResult?> CheckPriceAsync(§CTX context, §T row)
    {
§LISTCHECK        if ((decimal)row.§MIN <= 0)
            return RowShapeProblems.PriceInvalid("§W_MIN");
        if ((decimal)row.§PRICE < 0)
            return RowShapeProblems.PriceInvalid("§W_PRICE");
        var held = await context.Set<§T>().AsNoTracking().Where(x => x.§K != row.§K && x.§LIST == row.§LIST && x.§ITEM == row.§ITEM).Select(x => x.§MIN).ToListAsync();
        return held.Any(m => (decimal)m == (decimal)row.§MIN) ? RowShapeProblems.PriceTaken("§W_TAKEN") : null;
    }

    /// <summary> The row in force: of the lists in the currency that are valid on the day the row of the item with the largest minimum quantity not above the quantity. A group's lists come before the lists with no group. </summary>
    public static async Task<§T?> FindPriceAsync(§CTX context, §IT item, decimal quantity, DateTime day, string currency, string? group§SCOPEPARAM)
    {
        foreach (string? held in group is null ? new string?[] { null } : new string?[] { group, null })
        {
            var listed = context.Set<§LT>().AsNoTracking().Where(l => l.§CUR.ToUpper() == currency && l.§FROM <= day && (l.§TO == null || l.§TO >= day));§GROUPLINE§SCOPELISTS
            var lists = listed.Select(l => l.§LK);
            var rows = await context.Set<§T>().AsNoTracking().Where(x => x.§ITEM == item && lists.Contains(x.§LIST)).ToListAsync();§SCOPEROWS
            var best = rows.Where(x => (decimal)x.§MIN <= quantity).OrderByDescending(x => (decimal)x.§MIN).ThenByDescending(x => x.§K).FirstOrDefault();
            if (best is not null)
                return best;
        }
        return null;
    }

    /// <summary> The price in force for an item and a quantity (1 when none is given) on a day (today when none is given) in a currency. </summary>
    public static async Task<IResult> PriceOnAsync(§HTTPPARAM§CTX context, §IT? item, decimal? quantity, DateTime? day, string? currency§GROUPPARAM)
    {
        if (item is null || string.IsNullOrWhiteSpace(currency))
            return Results.Problem(statusCode: 400, title: "§W_LOOKUP");
        decimal wanted = quantity ?? 1m;
        if (wanted <= 0)
            return RowShapeProblems.PriceInvalid("§W_QTY");
        string asked = currency.Trim().ToUpperInvariant();§GROUPVALUE§SCOPEVALUE
        var row = await FindPriceAsync(context, item.Value, wanted, (day ?? DateTime.Today).Date, asked, §GROUPARG§SCOPEARG);
        return row is null ? RowShapeProblems.PriceMissing(§MISSING) : Results.Ok(row);
    }


""";
}
