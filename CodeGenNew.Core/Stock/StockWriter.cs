namespace CodeGenNew.Core.Stock;

/// <summary> The C# of the stock routes (<see cref="StockPlan"/>), written into the ledger table's <c>&lt;Table&gt;Shape</c> class by <see cref="Api.RowShapeWriter"/>, and the save and delete checks of the level table.
/// Every change of the level is one guarded update (<c>WHERE OnHand - Reserved &gt;= quantity</c> for a reservation) and one or two ledger rows, in one transaction, so two buyers never take the last item and the level can be worked out again from the ledger. </summary>
public static class StockWriter
{
    private static string Sheet(StockShape s) => s.Ledger.Table.TableName;

    public static string MapLines(StockShape s)
    {
        string item = s.Item.CSharpBase(), holdKey = s.Reservation.Key.CSharpBase();
        return $"        app.MapGet(\"§R/{StockPlan.AvailableRoute}\", ([FromServices] §CTX context, [FromQuery(Name = \"item\")] {item}? item) => AvailableAsync(context, item))§AUTH;\n"
            + $"        app.MapGet(\"§R/{StockPlan.LowRoute}\", ([FromServices] §CTX context) => LowAsync(context))§AUTH;\n"
            + $"        app.MapGet(\"§R/{StockPlan.CheckRoute}\", ([FromServices] §CTX context) => CheckAsync(context))§AUTH;\n"
            + $"        app.MapPost(\"§R/{StockPlan.ReceiveRoute}\", ([FromServices] §CTX context, StockRequest request) => ReceiveAsync(context, request))§AUTH;\n"
            + $"        app.MapPost(\"§R/{StockPlan.ReserveRoute}\", ([FromServices] §CTX context, StockRequest request) => ReserveAsync(context, request))§AUTH;\n"
            + $"        app.MapPost(\"§R/{StockPlan.CommitRoute}/{{id}}\", ([FromServices] §CTX context, {holdKey} id) => CommitAsync(context, id))§AUTH;\n"
            + $"        app.MapPost(\"§R/{StockPlan.ReleaseRoute}/{{id}}\", ([FromServices] §CTX context, {holdKey} id) => ReleaseAsync(context, id))§AUTH;\n"
            + $"        app.MapPost(\"§R/{StockPlan.ExpireRoute}\", ([FromServices] §CTX context) => ExpireRouteAsync(context))§AUTH;\n"
            + $"        app.MapPost(\"§R/{StockPlan.AdjustRoute}\", ([FromServices] §CTX context, StockRequest request) => AdjustAsync(context, request))§AUTH;\n";
    }

    public const string LevelSaveLines = "        if (await CheckStockAsync(context, row, isNew) is { } stockProblem)\n            return stockProblem;\n";

    public const string LevelDeleteLines = "        if (await CheckStockDeleteAsync(context, id) is { } stockInUse)\n            return stockInUse;\n";

    /// <summary> The checks of the level table: a new row starts empty and an update leaves the quantities alone; a level that holds stock or reservations is not deleted. </summary>
    public static string LevelCode(StockShape s, string contextType)
    {
        string level = s.Level.TableName, key = s.Key.CSharpName(), onHand = s.OnHand.CSharpName(), reserved = s.Reserved.CSharpName(), keyType = s.Key.CSharpBase();
        return $$"""
    /// <summary> A new level starts with nothing on hand and nothing reserved, and an update does not change either quantity: they change through the stock routes of the ledger. </summary>
    public static async Task<IResult?> CheckStockAsync({{contextType}} context, {{level}} row, bool isNew)
    {
        if (isNew)
            return row.{{onHand}} != 0 || row.{{reserved}} != 0 ? RowShapeProblems.Problem(400, "{{StockWords.ManagedType}}", "{{StockWords.ManagedTitle}}", "{{StockWords.Managed}}") : null;
        var stored = await context.Set<{{level}}>().AsNoTracking().Where(x => x.{{key}} == row.{{key}}).Select(x => new { x.{{onHand}}, x.{{reserved}} }).FirstOrDefaultAsync();
        return stored is not null && (stored.{{onHand}} != row.{{onHand}} || stored.{{reserved}} != row.{{reserved}}) ? RowShapeProblems.Problem(400, "{{StockWords.ManagedType}}", "{{StockWords.ManagedTitle}}", "{{StockWords.Managed}}") : null;
    }

    /// <summary> A level that still has stock or reservations is not deleted (409). </summary>
    public static async Task<IResult?> CheckStockDeleteAsync({{contextType}} context, {{keyType}} id)
    {
        var stored = await context.Set<{{level}}>().AsNoTracking().Where(x => x.{{key}} == id).Select(x => new { x.{{onHand}}, x.{{reserved}} }).FirstOrDefaultAsync();
        return stored is not null && (stored.{{onHand}} != 0 || stored.{{reserved}} != 0) ? RowShapeProblems.Problem(409, "{{StockWords.ManagedType}}", "{{StockWords.ManagedTitle}}", "{{StockWords.InUse}}") : null;
    }


""";
    }

    public static string Code(StockShape s, string contextType, int holdMinutes)
    {
        var l = s.Ledger;
        var h = s.Reservation;
        string level = s.Level.TableName, ledger = l.Table.TableName, hold = h.Table.TableName, items = s.ItemTable.TableName;
        string itemType = s.Item.CSharpBase(), q = s.QuantityType, holdKeyType = h.Key.CSharpBase();
        string lvItem = s.Item.CSharpName(), lvOn = s.OnHand.CSharpName(), lvRes = s.Reserved.CSharpName(), itemKey = s.ItemKey.CSharpName();
        string lgItem = l.Item.CSharpName(), lgKind = l.Kind.CSharpName(), lgQty = l.Quantity.CSharpName(), lgStamp = l.Stamp.CSharpName();
        string hKey = h.Key.CSharpName(), hItem = h.Item.CSharpName(), hQty = h.Quantity.CSharpName(), hStatus = h.Status.CSharpName(), hCreated = h.Created.CSharpName(), hExpires = h.Expires.CSharpName();
        string reorderOrder = s.Reorder is null ? "0" : "x." + s.Reorder.CSharpName();
        string reorderSelect = s.Reorder is null ? "0" : "x." + s.Reorder.CSharpName();
        string reorderRow = s.Reorder is null ? "0" : "row.Reorder";
        string reorderPick = s.Reorder is null ? "Reorder = 0" : "Reorder = x." + s.Reorder.CSharpName();
        int refMax = new[] { l.Reference?.CharacterLength ?? 0, h.Reference?.CharacterLength ?? 0 }.Where(n => n > 0).DefaultIfEmpty(0).Min();
        string ledgerRef = l.Reference is null ? "" : $", {l.Reference.CSharpName()} = reference";
        string ledgerRefHold = l.Reference is null ? "" : $", {l.Reference.CSharpName()} = {(h.Reference is null ? $"\"reservation \" + hold.{hKey}" : $"hold.{h.Reference.CSharpName()} ?? \"reservation \" + hold.{hKey}")}";
        string ledgerExpires = l.Expires is null ? "" : $", {l.Expires.CSharpName()} = expires";
        string holdRef = h.Reference is null ? "" : $", {h.Reference.CSharpName()} = reference";
        string resolvedSet = h.Resolved is null ? "" : $".SetProperty(x => x.{h.Resolved.CSharpName()}, (DateTime?)now)";
        string clip = refMax > 0 ? $"text.Length > {refMax} ? text[..{refMax}] : text" : "text";
        return $$"""
    /// <summary> What the stock routes read from a request. </summary>
    public sealed record StockRequest(long? Item, long? Quantity, long? Counted, string? Reference, int? Minutes, string? Kind);

    private sealed record Parsed(IResult? Problem, {{itemType}} Item, {{q}} Quantity, string? Reference);

    private static IResult Problem(int status, string type, string title, string detail) => RowShapeProblems.Problem(status, type, title, detail);
    private static IResult Invalid(string detail) => Problem(400, "{{StockWords.InvalidType}}", "{{StockWords.InvalidTitle}}", detail);
    private static IResult Unknown(string detail) => Problem(404, "{{StockWords.UnknownType}}", "{{StockWords.UnknownTitle}}", detail);
    private static IResult Short(string detail) => Problem(409, "{{StockWords.ShortType}}", "{{StockWords.ShortTitle}}", detail);
    private static IResult Closed(string detail) => Problem(409, "{{StockWords.ClosedType}}", "{{StockWords.ClosedTitle}}", detail);
    private static IResult Conflict(string detail) => Problem(409, "{{StockWords.ConflictType}}", "{{StockWords.ConflictTitle}}", detail);

    private static string? Clip(string? text)
    {
        text = text?.Trim();
        return string.IsNullOrEmpty(text) ? null : {{clip}};
    }

    /// <summary> The item, the quantity (above zero when the route needs one) and the reference of a request, or the problem. </summary>
    private static Parsed Read(StockRequest request, bool needsQuantity)
    {
        if (request.Item is null || (long)({{itemType}})request.Item.Value != request.Item.Value)
            return new(Invalid("{{StockWords.NeedsItem}}"), default, default, null);
        if (needsQuantity && (request.Quantity is null || request.Quantity <= 0 || (long)({{q}})request.Quantity.Value != request.Quantity.Value))
            return new(Invalid("{{StockWords.QuantityPositive}}"), default, default, null);
        return new(null, ({{itemType}})request.Item.Value, ({{q}})(request.Quantity ?? 0), Clip(request.Reference));
    }

    /// <summary> What an item has: on hand, reserved, available (on hand less reserved), the reorder level and whether it is at or under it. </summary>
    private static async Task<object> LevelAsync({{contextType}} context, {{itemType}} item)
    {
        var row = await context.Set<{{level}}>().AsNoTracking().Where(x => x.{{lvItem}} == item).Select(x => new { x.{{lvOn}}, x.{{lvRes}}, {{reorderPick}} }).FirstOrDefaultAsync();
        long onHand = row is null ? 0 : row.{{lvOn}}, reserved = row is null ? 0 : row.{{lvRes}}, reorder = row is null ? 0 : {{reorderRow}};
        return new { item, onHand, reserved, available = onHand - reserved, reorderLevel = reorder, low = onHand - reserved <= reorder };
    }

    public static async Task<IResult> AvailableAsync({{contextType}} context, {{itemType}}? item)
    {
        if (item is null)
            return Invalid("{{StockWords.NeedsItem}}");
        if (!await context.Set<{{items}}>().AsNoTracking().AnyAsync(x => x.{{itemKey}} == item))
            return Unknown("{{StockWords.ItemMissing}}");
        return Results.Ok(await LevelAsync(context, item.Value));
    }

    /// <summary> The items whose available stock (on hand less reserved) is at or under the reorder level, the lowest first. </summary>
    public static async Task<IResult> LowAsync({{contextType}} context)
    {
        var rows = await context.Set<{{level}}>().AsNoTracking().Where(x => x.{{lvOn}} - x.{{lvRes}} <= {{reorderOrder}}).OrderBy(x => x.{{lvOn}} - x.{{lvRes}}).ThenBy(x => x.{{lvItem}})
            .Select(x => new { item = x.{{lvItem}}, onHand = x.{{lvOn}}, reserved = x.{{lvRes}}, available = x.{{lvOn}} - x.{{lvRes}}, reorderLevel = {{reorderSelect}} }).ToListAsync();
        return Results.Ok(rows);
    }

    /// <summary> Works the level of every item out again from the ledger (receipts, sales, returns and adjustments for what is on hand; reservations and releases for what is reserved) and lists the items where the table differs. </summary>
    public static async Task<IResult> CheckAsync({{contextType}} context)
    {
        var levels = await context.Set<{{level}}>().AsNoTracking().Select(x => new { Item = x.{{lvItem}}, OnHand = x.{{lvOn}}, Reserved = x.{{lvRes}} }).ToListAsync();
        var sums = await context.Set<{{ledger}}>().AsNoTracking().GroupBy(x => x.{{lgItem}}).Select(g => new
        {
            Item = g.Key,
            OnHand = g.Sum(x => x.{{lgKind}} == "{{StockPlan.Receipt}}" || x.{{lgKind}} == "{{StockPlan.Sale}}" || x.{{lgKind}} == "{{StockPlan.Return}}" || x.{{lgKind}} == "{{StockPlan.Adjustment}}" ? x.{{lgQty}} : ({{q}})0),
            Reserved = g.Sum(x => x.{{lgKind}} == "{{StockPlan.Reservation}}" || x.{{lgKind}} == "{{StockPlan.Release}}" ? x.{{lgQty}} : ({{q}})0),
        }).ToListAsync();
        var byItem = sums.ToDictionary(x => x.Item);
        var differences = new List<object>();
        foreach (var row in levels)
        {
            byItem.TryGetValue(row.Item, out var sum);
            long ledgerOnHand = sum is null ? 0 : sum.OnHand, ledgerReserved = sum is null ? 0 : sum.Reserved;
            if (ledgerOnHand != row.OnHand || ledgerReserved != row.Reserved)
                differences.Add(new { item = row.Item, onHand = row.OnHand, ledgerOnHand, reserved = row.Reserved, ledgerReserved });
        }
        var known = levels.Select(x => x.Item).ToHashSet();
        foreach (var sum in sums.Where(x => !known.Contains(x.Item) && (x.OnHand != 0 || x.Reserved != 0)))
            differences.Add(new { item = sum.Item, onHand = 0L, ledgerOnHand = (long)sum.OnHand, reserved = 0L, ledgerReserved = (long)sum.Reserved });
        return Results.Ok(new { levels = levels.Count, mismatches = differences.Count, differences });
    }

    /// <summary> Puts stock on hand (a receipt, or a return when kind is return): the level goes up and a ledger row says why. The first receipt of an item adds its level row. </summary>
    public static async Task<IResult> ReceiveAsync({{contextType}} context, StockRequest request)
    {
        var read = Read(request, true);
        if (read.Problem is not null)
            return read.Problem;
        string kind = (request.Kind ?? "{{StockPlan.Receipt}}").Trim().ToLowerInvariant();
        if (kind is not ("{{StockPlan.Receipt}}" or "{{StockPlan.Return}}"))
            return Invalid("{{StockWords.KindBad}}");
        var item = read.Item;
        var quantity = read.Quantity;
        var reference = read.Reference;
        if (!await context.Set<{{items}}>().AsNoTracking().AnyAsync(x => x.{{itemKey}} == item))
            return Invalid("{{StockWords.ItemMissing}}");
        var now = DateTime.Now;
        await using var transaction = await context.Database.BeginTransactionAsync();
        int changed = await context.Set<{{level}}>().Where(x => x.{{lvItem}} == item).ExecuteUpdateAsync(s => s.SetProperty(x => x.{{lvOn}}, x => x.{{lvOn}} + quantity));
        if (changed == 0)
            context.Set<{{level}}>().Add(new {{level}} { {{lvItem}} = item, {{lvOn}} = quantity });
        context.Set<{{ledger}}>().Add(new {{ledger}} { {{lgItem}} = item, {{lgKind}} = kind, {{lgQty}} = quantity, {{lgStamp}} = now{{ledgerRef}} });
        try
        {
            await context.SaveChangesAsync();
        }
        catch (DbUpdateException)
        {
            return Conflict("{{StockWords.Conflict}}");
        }
        await transaction.CommitAsync();
        return Results.Created("{{StockPlan.AvailableRoute}}?item=" + item, await LevelAsync(context, item));
    }

    /// <summary> Holds stock for a buyer: one guarded update takes the quantity out of what is available (on hand less reserved), so two buyers never take the last item; the hold lasts the minutes asked (default {{holdMinutes}}). </summary>
    public static async Task<IResult> ReserveAsync({{contextType}} context, StockRequest request)
    {
        var read = Read(request, true);
        if (read.Problem is not null)
            return read.Problem;
        if (request.Minutes is <= 0)
            return Invalid("{{StockWords.MinutesBad}}");
        var item = read.Item;
        var quantity = read.Quantity;
        var reference = read.Reference;
        await ExpireAsync(context, item);
        var now = DateTime.Now;
        var expires = now.AddMinutes(request.Minutes ?? {{holdMinutes}});
        await using var transaction = await context.Database.BeginTransactionAsync();
        int taken = await context.Set<{{level}}>().Where(x => x.{{lvItem}} == item && x.{{lvOn}} - x.{{lvRes}} >= quantity).ExecuteUpdateAsync(s => s.SetProperty(x => x.{{lvRes}}, x => x.{{lvRes}} + quantity));
        if (taken == 0)
            return Short("{{StockWords.ShortStock}}");
        var hold = new {{hold}} { {{hItem}} = item, {{hQty}} = quantity, {{hStatus}} = "{{StockPlan.Held}}", {{hCreated}} = now, {{hExpires}} = expires{{holdRef}} };
        context.Set<{{hold}}>().Add(hold);
        context.Set<{{ledger}}>().Add(new {{ledger}} { {{lgItem}} = item, {{lgKind}} = "{{StockPlan.Reservation}}", {{lgQty}} = quantity, {{lgStamp}} = now{{ledgerRef}}{{ledgerExpires}} });
        await context.SaveChangesAsync();
        await transaction.CommitAsync();
        return Results.Created("{{StockPlan.ReserveRoute}}/" + hold.{{hKey}}, new { reservation = hold.{{hKey}}, item, quantity, expiresOn = expires, level = await LevelAsync(context, item) });
    }

    /// <summary> Closes one hold that is still held: it is committed (the stock leaves: a sale), released or expired (the stock is available again). Only one caller moves a hold out of held; the level moves with a guarded update. </summary>
    private static async Task<bool> ResolveAsync({{contextType}} context, {{hold}} hold, string status, DateTime now, bool sell)
    {
        await using var transaction = await context.Database.BeginTransactionAsync();
        int claimed = await context.Set<{{hold}}>().Where(x => x.{{hKey}} == hold.{{hKey}} && x.{{hStatus}} == "{{StockPlan.Held}}").ExecuteUpdateAsync(s => s.SetProperty(x => x.{{hStatus}}, status){{resolvedSet}});
        if (claimed == 0)
            return false;
        var item = hold.{{hItem}};
        var quantity = hold.{{hQty}};
        int moved = sell
            ? await context.Set<{{level}}>().Where(x => x.{{lvItem}} == item && x.{{lvRes}} >= quantity && x.{{lvOn}} >= quantity).ExecuteUpdateAsync(s => s.SetProperty(x => x.{{lvOn}}, x => x.{{lvOn}} - quantity).SetProperty(x => x.{{lvRes}}, x => x.{{lvRes}} - quantity))
            : await context.Set<{{level}}>().Where(x => x.{{lvItem}} == item && x.{{lvRes}} >= quantity).ExecuteUpdateAsync(s => s.SetProperty(x => x.{{lvRes}}, x => x.{{lvRes}} - quantity));
        if (moved == 0)
            return false;
        context.Set<{{ledger}}>().Add(new {{ledger}} { {{lgItem}} = item, {{lgKind}} = "{{StockPlan.Release}}", {{lgQty}} = -quantity, {{lgStamp}} = now{{ledgerRefHold}} });
        if (sell)
            context.Set<{{ledger}}>().Add(new {{ledger}} { {{lgItem}} = item, {{lgKind}} = "{{StockPlan.Sale}}", {{lgQty}} = -quantity, {{lgStamp}} = now{{ledgerRefHold}} });
        await context.SaveChangesAsync();
        await transaction.CommitAsync();
        return true;
    }

    private static async Task<IResult> CloseAsync({{contextType}} context, {{holdKeyType}} id, string status, bool sell)
    {
        var hold = await context.Set<{{hold}}>().AsNoTracking().FirstOrDefaultAsync(x => x.{{hKey}} == id);
        if (hold is null)
            return Unknown("{{StockWords.ReservationMissing}}");
        var now = DateTime.Now;
        if (hold.{{hStatus}} == "{{StockPlan.Held}}" && hold.{{hExpires}} <= now)
        {
            await ResolveAsync(context, hold, "{{StockPlan.Expired}}", now, false);
            return Closed("{{StockWords.ReservationClosed}}");
        }
        if (hold.{{hStatus}} != "{{StockPlan.Held}}")
            return Closed("{{StockWords.ReservationClosed}}");
        if (!await ResolveAsync(context, hold, status, now, sell))
            return await context.Set<{{hold}}>().AsNoTracking().AnyAsync(x => x.{{hKey}} == id && x.{{hStatus}} == "{{StockPlan.Held}}") ? Conflict("{{StockWords.ReservationInconsistent}}") : Closed("{{StockWords.ReservationClosed}}");
        return Results.Ok(new { reservation = id, status, level = await LevelAsync(context, hold.{{hItem}}) });
    }

    /// <summary> Turns a hold into a sale: the quantity leaves what is on hand and what is reserved, and the ledger gets a release row and a sale row. A hold that is not held any more, or has run out, is refused (409). </summary>
    public static Task<IResult> CommitAsync({{contextType}} context, {{holdKeyType}} id) => CloseAsync(context, id, "{{StockPlan.Committed}}", true);

    /// <summary> Gives a hold back: the quantity is available again. </summary>
    public static Task<IResult> ReleaseAsync({{contextType}} context, {{holdKeyType}} id) => CloseAsync(context, id, "{{StockPlan.Released}}", false);

    /// <summary> Gives back every hold (of one item, or of all) whose time has run out; the number of holds closed. </summary>
    public static async Task<int> ExpireAsync({{contextType}} context, {{itemType}}? item)
    {
        var now = DateTime.Now;
        var due = await context.Set<{{hold}}>().AsNoTracking().Where(x => x.{{hStatus}} == "{{StockPlan.Held}}" && x.{{hExpires}} <= now && (item == null || x.{{hItem}} == item)).ToListAsync();
        int closed = 0;
        foreach (var hold in due)
            if (await ResolveAsync(context, hold, "{{StockPlan.Expired}}", now, false))
                closed++;
        return closed;
    }

    public static async Task<IResult> ExpireRouteAsync({{contextType}} context) => Results.Ok(new { expired = await ExpireAsync(context, null) });

    /// <summary> A stock-take: the counted quantity becomes what is on hand and a ledger row (an adjustment of the difference, which may be zero) says so. A count below what is reserved is refused. </summary>
    public static async Task<IResult> AdjustAsync({{contextType}} context, StockRequest request)
    {
        var read = Read(request, false);
        if (read.Problem is not null)
            return read.Problem;
        if (request.Counted is null || request.Counted < 0 || (long)({{q}})request.Counted.Value != request.Counted.Value)
            return Invalid("{{StockWords.CountNeeded}}");
        var item = read.Item;
        var reference = read.Reference ?? "stock-take";
        var counted = ({{q}})request.Counted.Value;
        if (!await context.Set<{{items}}>().AsNoTracking().AnyAsync(x => x.{{itemKey}} == item))
            return Invalid("{{StockWords.ItemMissing}}");
        var stored = await context.Set<{{level}}>().AsNoTracking().Where(x => x.{{lvItem}} == item).Select(x => new { x.{{lvOn}}, x.{{lvRes}} }).FirstOrDefaultAsync();
        {{q}} onHand = stored is null ? 0 : stored.{{lvOn}}, reserved = stored is null ? 0 : stored.{{lvRes}};
        if (counted < reserved)
            return Problem(409, "{{StockWords.BelowReservedType}}", "{{StockWords.BelowReservedTitle}}", "{{StockWords.BelowReserved}}");
        var now = DateTime.Now;
        await using var transaction = await context.Database.BeginTransactionAsync();
        if (stored is null)
            context.Set<{{level}}>().Add(new {{level}} { {{lvItem}} = item, {{lvOn}} = counted });
        else if (await context.Set<{{level}}>().Where(x => x.{{lvItem}} == item && x.{{lvOn}} == onHand && x.{{lvRes}} == reserved).ExecuteUpdateAsync(s => s.SetProperty(x => x.{{lvOn}}, counted)) == 0)
            return Conflict("{{StockWords.Conflict}}");
        context.Set<{{ledger}}>().Add(new {{ledger}} { {{lgItem}} = item, {{lgKind}} = "{{StockPlan.Adjustment}}", {{lgQty}} = ({{q}})(counted - onHand), {{lgStamp}} = now{{(l.Reference is null ? "" : ", " + l.Reference.CSharpName() + " = reference")}} });
        try
        {
            await context.SaveChangesAsync();
        }
        catch (DbUpdateException)
        {
            return Conflict("{{StockWords.Conflict}}");
        }
        await transaction.CommitAsync();
        return Results.Ok(new { item, counted, difference = counted - onHand, level = await LevelAsync(context, item) });
    }


""";
    }
}
