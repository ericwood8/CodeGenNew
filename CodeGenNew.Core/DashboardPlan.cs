using System.Data;

namespace CodeGenNew.Core;

public enum DashboardWidgetKind
{
    /// <summary> A table's row count. </summary>
    Count,
    /// <summary> A master's row count beside the row count of its child table. </summary>
    ChildCount,
    /// <summary> The total, average and largest of a money column (one aggregate for a measure the project names). </summary>
    Money,
    /// <summary> The sum of a money column by the display name of a foreign key's parent, the ten largest. </summary>
    TopBy,
    /// <summary> The row count per value of a choice column or a lookup table. </summary>
    Breakdown,
    /// <summary> A count or sum per month (day, year) over the latest twelve months (30 days, 5 years) of the data. </summary>
    Trend,
    /// <summary> Active against inactive rows, or live against soft-deleted rows. </summary>
    Ratio,
    /// <summary> Rows that are current, expired or not yet started by their start and end dates. </summary>
    Status,
    /// <summary> The latest ten rows, each a display text and a date. </summary>
    Recent
}

/// <summary> One thing the dashboard shows. <see cref="Sql"/> is one fixed statement without a parameter that returns <c>Label</c> and <c>Value</c> (a double), or <c>Label</c> and <c>Detail</c>
/// (text) for <see cref="DashboardWidgetKind.Recent"/>. </summary>
public sealed record DashboardWidget(string Id, DashboardWidgetKind Kind, string Title, string Table, string Why, string Sql, int Score)
{
    public bool IsRows => Kind == DashboardWidgetKind.Recent;
    public bool IsCard => Kind is DashboardWidgetKind.Count or DashboardWidgetKind.ChildCount or DashboardWidgetKind.Money or DashboardWidgetKind.Ratio or DashboardWidgetKind.Status;

    /// <summary> The kind as the JSON and the pages name it: <c>count</c>, <c>childCount</c>, <c>topBy</c>. </summary>
    public string KindName => JsonNames.Camel(Kind.ToString());
}

/// <summary> A measure the project names itself (<c>DashboardMeasures=SalesInvoice.TotalAmount:sum:InvoiceDate:month</c>): a table, a column, an aggregate and, for a trend, a date column and a bucket. </summary>
public sealed record DashboardMeasure(string Table, string Column, string Aggregate, string? DateColumn, DateBucket? Bucket);

/// <summary> What the dashboard shows, worked out once from the schema's own facts and read by every stack: the money columns, the foreign-key graph, the lookup and choice columns, the active, start
/// and end and soft-delete columns, the dates and the display columns. The API, the SQL script, the document and the three front ends all render this list, so one test covers the choice. </summary>
public sealed class DashboardPlan
{
    /// <summary> The cards on the page: counts, sums, ratios and status counts. </summary>
    public const int MaxCards = 4;

    /// <summary> At most this many of the cards are sums of money columns, so the page is not four variations on one idea. </summary>
    public const int MaxMoneyCards = 2;
    public const int MaxTopBy = 2;
    public const int MaxBreakdowns = 2;
    public const int MaxTrends = 2;
    public const int MaxRecent = 1;

    /// <summary> The cards a table's own screen shows above its grid. </summary>
    public const int MaxStripCards = 3;

    private const int TopRows = 10;
    private const int MinBreakdownValues = 2;
    private const int MaxBreakdownValues = 8;
    private const int MaxLookupRows = 12;
    private const int EmptyTablePenalty = 30;

    private DashboardPlan(IReadOnlyList<DashboardWidget> widgets, IReadOnlyList<DashboardWidget> candidates, IReadOnlyList<string> problems)
    {
        Widgets = widgets;
        Candidates = candidates;
        Problems = problems;
    }

    /// <summary> The widgets of the dashboard page, cards first, in display order. </summary>
    public IReadOnlyList<DashboardWidget> Widgets { get; }

    /// <summary> Every widget the schema suggests, ranked, before the caps. </summary>
    public IReadOnlyList<DashboardWidget> Candidates { get; }

    /// <summary> What is wrong with the project's measures (a table or column that does not exist). </summary>
    public IReadOnlyList<string> Problems { get; }

    /// <summary> The cards for one table's own screen: its count, money, ratio and status widgets, best first. </summary>
    public IReadOnlyList<DashboardWidget> StripOf(string table) => Candidates
        .Where(w => w.IsCard && w.Kind != DashboardWidgetKind.ChildCount && w.Table.EqualsIgnoreCase(table))
        .Take(MaxStripCards).ToList();

    /// <summary> The tables with a strip, in name order. </summary>
    public IReadOnlyList<string> StripTables => Candidates.Where(w => w.IsCard && w.Kind != DashboardWidgetKind.ChildCount)
        .Select(w => w.Table).Distinct(StringComparer.OrdinalIgnoreCase).OrderBy(t => t, StringComparer.OrdinalIgnoreCase).ToList();

    public static DashboardPlan Build(DatabaseModel database, ProjectSettings project) => Build(database, project, database.Dialect);

    /// <summary> The plan with its statements written for <paramref name="dialect"/>, for a document that lists them for every database. </summary>
    public static DashboardPlan Build(DatabaseModel database, ProjectSettings project, SqlDialect dialect)
    {
        var builder = new Builder(database, project, DialectInfo.For(dialect));
        return builder.Build();
    }

    /// <summary> Reads <c>Table.Column:aggregate</c> or <c>Table.Column:aggregate:DateColumn:month</c>; the bucket is day, month or year. </summary>
    public static DashboardMeasure? ParseMeasure(string text)
    {
        var parts = text.Split(':', StringSplitOptions.TrimEntries);
        var target = parts[0].Split('.', StringSplitOptions.TrimEntries);
        if (target.Length != 2 || parts.Length is not (2 or 4) || target.Any(string.IsNullOrEmpty))
            return null;
        string aggregate = parts[1].ToLowerInvariant();
        if (aggregate is not ("sum" or "avg" or "max" or "min" or "count"))
            return null;
        if (parts.Length == 2)
            return new DashboardMeasure(target[0], target[1], aggregate, null, null);
        DateBucket? bucket = parts[3].ToLowerInvariant() switch { "day" => DateBucket.Day, "month" => DateBucket.Month, "year" => DateBucket.Year, _ => null };
        return bucket is null || parts[2].Length == 0 ? null : new DashboardMeasure(target[0], target[1], aggregate, parts[2], bucket);
    }

    private sealed class Builder(DatabaseModel database, ProjectSettings project, DialectInfo di)
    {
        private readonly List<DashboardWidget> _candidates = [];
        private readonly List<string> _problems = [];
        private readonly HashSet<string> _ids = new(StringComparer.OrdinalIgnoreCase);
        private readonly HashSet<string> _coveredByChildCount = new(StringComparer.OrdinalIgnoreCase);

        public DashboardPlan Build()
        {
            var skipped = project.NoDashboardTables;
            var tables = database.ScreenTables(project).Where(t => !skipped.Contains(t.TableName, StringComparer.OrdinalIgnoreCase)).ToList();

            foreach (var table in tables)
                ChildCount(table);
            foreach (var table in tables)
            {
                Count(table);
                Money(table);
                Breakdowns(table);
                Trend(table);
                Ratio(table);
                Status(table);
                Recent(table);
            }

            var chosen = new List<DashboardWidget>();
            var measureWidgets = new List<DashboardWidget>();
            foreach (string text in project.DashboardMeasures)
            {
                var measure = ParseMeasure(text);
                var table = measure is null ? null : database.Tables.FirstOrDefault(t => t.TableName.EqualsIgnoreCase(measure.Table));
                var column = table?.Columns.FirstOrDefault(c => c.Name.EqualsIgnoreCase(measure!.Column));
                var dateColumn = measure?.DateColumn is null ? null : table?.Columns.FirstOrDefault(c => c.Name.EqualsIgnoreCase(measure.DateColumn));
                if (measure is null || table is null || column is null || (measure.DateColumn is not null && dateColumn is null))
                {
                    _problems.Add($"DashboardMeasures lists '{text}', which is not Table.Column:aggregate or Table.Column:aggregate:DateColumn:day|month|year naming a column of the database.");
                    continue;
                }
                measureWidgets.Add(MeasureWidget(table, column, measure, dateColumn));
            }

            var ranked = _candidates.OrderByDescending(w => w.Score).ThenBy(w => w.Table, StringComparer.OrdinalIgnoreCase).ThenBy(w => w.Id, StringComparer.Ordinal).ToList();
            var replaced = measureWidgets.Select(m => (m.Table, m.Kind)).ToHashSet();
            ranked = ranked.Where(w => !replaced.Contains((w.Table, w.Kind))).ToList();

            chosen.AddRange(measureWidgets);
            int money = 0;
            foreach (var card in ranked.Where(w => w.IsCard && !(w.Kind == DashboardWidgetKind.Count && _coveredByChildCount.Contains(w.Table))))
            {
                if (chosen.Count(c => c.IsCard) >= MaxCards || (card.Kind == DashboardWidgetKind.Money && money >= MaxMoneyCards))
                    continue;
                money += card.Kind == DashboardWidgetKind.Money ? 1 : 0;
                chosen.Add(card);
            }
            chosen.AddRange(ranked.Where(w => w.Kind == DashboardWidgetKind.TopBy).Take(MaxTopBy));
            chosen.AddRange(ranked.Where(w => w.Kind == DashboardWidgetKind.Breakdown).Take(MaxBreakdowns));
            chosen.AddRange(ranked.Where(w => w.Kind == DashboardWidgetKind.Trend).Take(MaxTrends));
            chosen.AddRange(ranked.Where(w => w.Kind == DashboardWidgetKind.Recent).Take(MaxRecent));

            return new DashboardPlan(Ordered(chosen), _candidates.OrderByDescending(w => w.Score).ThenBy(w => w.Id, StringComparer.Ordinal).ToList(), _problems);
        }

        /// <summary> Cards, then rankings, then trends, then the recent list; the best first within each. </summary>
        private static List<DashboardWidget> Ordered(List<DashboardWidget> widgets) => widgets
            .Select((w, i) => (w, i))
            .OrderBy(x => x.w.IsCard ? 0 : x.w.Kind is DashboardWidgetKind.TopBy or DashboardWidgetKind.Breakdown ? 1 : x.w.Kind == DashboardWidgetKind.Trend ? 2 : 3)
            .ThenBy(x => x.i).Select(x => x.w).ToList();

        private void Add(DashboardWidgetKind kind, string title, TableModel table, string? part, string why, string sql, int score)
        {
            string id = ScreenNames.Route(table.TableName) + "-" + ScreenNames.Route(kind.ToString()) + (part is null ? "" : "-" + ScreenNames.Route(part));
            for (int n = 2; !_ids.Add(id); n++)
                id = id.TrimEnd('0', '1', '2', '3', '4', '5', '6', '7', '8', '9') + n;
            _candidates.Add(new DashboardWidget(id, kind, title, table.TableName, why, sql, score));
        }

        /// <summary> Up to ten points for a table with more rows (a base-two logarithm, so a hundred rows is worth about as much as a thousand halves). </summary>
        private static int RowBonus(TableModel table) => table.LookupShape.RowCount > 0 ? (int)Math.Min(10, Math.Log2(table.LookupShape.RowCount + 1)) : 0;

        private int Penalty(TableModel table) => table.LookupShape.RowCount > 0 ? 0 : EmptyTablePenalty;

        private string QLabel => di.Quote("Label");
        private string QValue => di.Quote("Value");
        private string QDetail => di.Quote("Detail");

        private string T(TableModel table) => di.QuoteTable(table.SchemaName, table.DbTableName);
        private string C(ColumnModel column, string? alias = null) => (alias is null ? "" : alias + ".") + di.Quote(column.DbName);
        private static string Lit(string text) => "'" + text.Replace("'", "''") + "'";
        private static string Words(string table) => ScreenNames.Words(ScreenNames.BaseName(table));
        private static string Plural(string table) => Words(table).Pluralize();

        private static string ColumnWords(ColumnModel column)
        {
            string name = column.Name.EndsWith("Id", StringComparison.Ordinal) && column.Name.Length > 2 ? column.Name[..^2] : column.Name;
            return ScreenNames.Words(name);
        }

        private string Number(string expression) => di.ToDouble(expression);

        /// <summary> The column as a number an aggregate accepts: PostgreSQL's money type has no AVG and no cast to double, so it becomes numeric first. </summary>
        private string Amount(ColumnModel column, string? alias = null) =>
            di.Dialect == SqlDialect.PostgreSql && column.SqlType is SqlDbType.Money or SqlDbType.SmallMoney ? $"CAST({C(column, alias)} AS numeric)" : C(column, alias);

        private string CountSql(TableModel table, string label) => $"SELECT {Lit(label)} AS {QLabel}, {Number("COUNT(*)")} AS {QValue} FROM {T(table)}";

        private ColumnModel? DisplayOf(TableModel table) => table.DisplayColumns.Where(c => c.IsStringColumn && !c.IsLongTextColumn)
            .OrderBy(c => c.DisplayRank ?? int.MaxValue).ThenBy(c => c.OrdinalPosition).FirstOrDefault();

        /// <summary> How much a money column's name says "the amount this row is worth", so a total is something a person wants: a total or balance, an amount, a price or cost; a limit, a rate, a
        /// percentage or a factor says nothing when added up. Below <see cref="MinMoneyNameScore"/> the column is not a dashboard measure. </summary>
        private static int MoneyNameScore(string name)
        {
            bool Has(string word) => name.ContainsIgnoreCase(word);
            if (Has("Rate") || Has("Percent") || Has("Pct") || Has("Ratio") || Has("Factor") || Has("Limit") || Has("Quantity"))
                return 0;
            int score = Has("Total") ? 100 : Has("Balance") ? 70 : Has("Amount") ? (Has("Tax") ? 25 : 60) : Has("Price") ? 40 : Has("Cost") ? 35 : Has("Fee") || Has("Freight") ? 25 : 0;
            if (Has("Extended") || Has("Current")) score += 15;
            return score;
        }

        private const int MinMoneyNameScore = 30;

        private static ColumnModel? MoneyOf(TableModel table) => table.Columns
            .Where(c => (c.IsMoneyColumn || c.IsCurrencyColumn) && !c.IsPrimaryKey && !c.IsSystemFilled() && !IsForeignKey(table, c) && MoneyNameScore(c.Name) >= MinMoneyNameScore)
            .OrderByDescending(c => MoneyNameScore(c.Name)).ThenBy(c => c.OrdinalPosition).FirstOrDefault();

        private static bool IsForeignKey(TableModel table, ColumnModel column) =>
            table.ForeignKeys.Any(fk => fk.ReferencingColumns.Any(c => c.EqualsIgnoreCase(column.Name)));

        private static ColumnModel? DateOf(TableModel table)
        {
            var dates = table.Columns.Where(c => c.IsDateColumn && !c.IsPrimaryKey && !c.IsAuditColumn && !IsForeignKey(table, c)).ToList();
            return dates.FirstOrDefault(c => !c.IsNullable && c.Name.ContainsIgnoreCase("Date"))
                ?? dates.FirstOrDefault(c => c.Name.ContainsIgnoreCase("Date"))
                ?? dates.FirstOrDefault()
                ?? table.Columns.FirstOrDefault(c => c.IsCreateDateColumn && c.IsDateColumn);
        }

        private void ChildCount(TableModel parent)
        {
            var child = parent.ChildForeignKeys
                .Where(fk => fk.ReferencingColumns.Count == 1 && !fk.ReferencingTable.EqualsIgnoreCase(parent.TableName))
                .Select(fk => database.Tables.FirstOrDefault(t => t.TableName.EqualsIgnoreCase(fk.ReferencingTable)))
                .OfType<TableModel>().Where(t => t.HasPrimaryKey)
                .OrderByDescending(t => t.LookupShape.RowCount).ThenBy(t => t.TableName, StringComparer.OrdinalIgnoreCase).FirstOrDefault();
            if (child is null)
                return;

            _coveredByChildCount.Add(parent.TableName);
            _coveredByChildCount.Add(child.TableName);
            string sql = CountSql(parent, Plural(parent.TableName)) + " UNION ALL " + CountSql(child, Plural(child.TableName));
            Add(DashboardWidgetKind.ChildCount, $"{Plural(parent.TableName)} and {Plural(child.TableName)}", parent, null,
                $"{Words(parent.TableName)} is a master with {Words(child.TableName)} rows under it, so both counts sit on one card.", sql, 60 + RowBonus(parent) - Penalty(parent));
        }

        private void Count(TableModel table)
        {
            Add(DashboardWidgetKind.Count, Plural(table.TableName), table, null, "Every screen's table gets its row count.", CountSql(table, Plural(table.TableName)), 50 - Penalty(table));
        }

        private void Money(TableModel table)
        {
            var money = MoneyOf(table);
            if (money is null)
                return;

            string x = Number("SUM(" + Amount(money) + ")"), a = Number("AVG(" + Amount(money) + ")"), m = Number("MAX(" + Amount(money) + ")");
            string sql = $"SELECT 'Total' AS {QLabel}, COALESCE({x}, 0) AS {QValue} FROM {T(table)} UNION ALL SELECT 'Average', COALESCE({a}, 0) FROM {T(table)} UNION ALL SELECT 'Largest', COALESCE({m}, 0) FROM {T(table)}";
            string title = $"{Words(table.TableName)} {ColumnWords(money)}";
            int nameScore = MoneyNameScore(money.Name);
            Add(DashboardWidgetKind.Money, title, table, money.Name, $"{money.Name} is a money column, so its total, average and largest are the numbers a person looks for.", sql, 60 + nameScore / 4 - Penalty(table));

            foreach (var fk in table.ForeignKeys.Where(fk => fk.ReferencingColumns.Count == 1 && !table.IsSelfReferencing(fk)).Take(4))
            {
                var parent = database.Tables.FirstOrDefault(t => t.TableName.EqualsIgnoreCase(fk.ReferencedTable));
                var display = parent is null ? null : DisplayOf(parent);
                var key = parent?.PrimaryKeyColumns.FirstOrDefault(c => c.Name.EqualsIgnoreCase(fk.ReferencedColumns[0]));
                if (parent is null || display is null || key is null || IsSmallLookup(parent))
                    continue;

                var fkColumn = table.Columns.First(c => c.Name.EqualsIgnoreCase(fk.ReferencingColumns[0]));
                string topSql = $"SELECT COALESCE({C(display, "p")}, '(none)') AS {QLabel}, {Number("SUM(" + Amount(money, "t") + ")")} AS {QValue} FROM {T(table)} t INNER JOIN {T(parent)} p ON {C(key, "p")} = {C(fkColumn, "t")} "
                    + $"GROUP BY {C(display, "p")} ORDER BY SUM({Amount(money, "t")}) DESC {di.Paging("0", TopRows.ToString())}";
                Add(DashboardWidgetKind.TopBy, $"{title} by {Words(parent.TableName)}", table, fkColumn.Name,
                    $"{table.TableName} points at {parent.TableName}, so the money column can be added up per {Words(parent.TableName)} (the ten largest).", topSql, 55 + nameScore / 4 - Penalty(table));
                break;
            }
        }

        private bool IsSmallLookup(TableModel parent) =>
            parent.LookupShape.LooksLikeLookup && parent.LookupShape.RowCount is >= MinBreakdownValues and <= MaxLookupRows;

        private void Breakdowns(TableModel table)
        {
            foreach (var column in table.Columns.Where(c => c.HasChoices && c.Choices!.Count is >= MinBreakdownValues and <= MaxBreakdownValues && !c.IsAuditColumn))
            {
                string label = column.DbEnumType is not null || !column.IsStringColumn ? di.ToText(C(column)) : C(column);
                string sql = $"SELECT COALESCE({label}, '(none)') AS {QLabel}, {Number("COUNT(*)")} AS {QValue} FROM {T(table)} GROUP BY {C(column)} ORDER BY COUNT(*) DESC";
                Add(DashboardWidgetKind.Breakdown, $"{Plural(table.TableName)} by {ColumnWords(column)}", table, column.Name,
                    $"{column.Name} accepts only {column.Choices!.Count} values, so the rows can be counted per value.", sql, 70 - Penalty(table));
            }

            foreach (var fk in table.ForeignKeys.Where(fk => fk.ReferencingColumns.Count == 1 && !table.IsSelfReferencing(fk)))
            {
                var parent = database.Tables.FirstOrDefault(t => t.TableName.EqualsIgnoreCase(fk.ReferencedTable));
                var display = parent is null ? null : DisplayOf(parent);
                var key = parent?.PrimaryKeyColumns.FirstOrDefault(c => c.Name.EqualsIgnoreCase(fk.ReferencedColumns[0]));
                if (parent is null || display is null || key is null || !IsSmallLookup(parent))
                    continue;

                var fkColumn = table.Columns.First(c => c.Name.EqualsIgnoreCase(fk.ReferencingColumns[0]));
                string sql = $"SELECT COALESCE({C(display, "p")}, '(none)') AS {QLabel}, {Number("COUNT(*)")} AS {QValue} FROM {T(table)} t INNER JOIN {T(parent)} p ON {C(key, "p")} = {C(fkColumn, "t")} "
                    + $"GROUP BY {C(display, "p")} ORDER BY COUNT(*) DESC";
                Add(DashboardWidgetKind.Breakdown, $"{Plural(table.TableName)} by {ColumnWords(fkColumn)}", table, fkColumn.Name,
                    $"{parent.TableName} is a lookup of {parent.LookupShape.RowCount} rows, so the rows can be counted per {Words(parent.TableName)}.", sql, 68 - Penalty(table));
            }
        }

        private static int Window(DateBucket bucket) => bucket switch { DateBucket.Day => 30, DateBucket.Month => 12, _ => 5 };
        private static string BucketWord(DateBucket bucket) => bucket switch { DateBucket.Day => "Day", DateBucket.Month => "Month", _ => "Year" };

        /// <summary> A value per bucket over the newest <see cref="Window"/> buckets of the data (measured from the table's latest date, so a table with old rows still shows them). <paramref name="valueExpression"/> is
        /// the aggregate over the inner column <c>v</c>, or null for a count. </summary>
        private string TrendSql(TableModel table, ColumnModel date, DateBucket bucket, string aggregate, ColumnModel? value)
        {
            string newest = $"(SELECT MAX({C(date)}) FROM {T(table)})";
            string from = di.DateBefore(di.DateBucketExpression(newest, bucket), bucket, Window(bucket) - 1);
            string inner = $"SELECT {di.DateBucketExpression(C(date), bucket)} AS b" + (value is null ? "" : $", {Amount(value)} AS v") + $" FROM {T(table)} WHERE {C(date)} >= {from}";
            string measure = value is null ? "COUNT(*)" : aggregate.ToUpperInvariant() + "(v)";
            return $"SELECT {di.DateText("b")} AS {QLabel}, {Number(measure)} AS {QValue} FROM ({inner}) x GROUP BY b ORDER BY b";
        }

        private void Trend(TableModel table)
        {
            var date = DateOf(table);
            if (date is null)
                return;

            var money = MoneyOf(table);
            string title = money is null ? $"{Plural(table.TableName)} per Month" : $"{Words(table.TableName)} {ColumnWords(money)} per Month";
            Add(DashboardWidgetKind.Trend, title, table, date.Name,
                $"{date.Name} dates each {Words(table.TableName)} row" + (money is null ? ", so the rows can be counted per month." : $", so {money.Name} can be added up per month."),
                TrendSql(table, date, DateBucket.Month, "sum", money), (money is null ? 50 : 55 + MoneyNameScore(money.Name) / 8) - Penalty(table));
        }

        private void Ratio(TableModel table)
        {
            string sql;
            string why;
            if (table.HasActiveInactivePair && table.ActiveColumn is { IsBooleanColumn: true } active)
            {
                string yes = di.BooleanLiteral(true), no = di.BooleanLiteral(false);
                sql = $"SELECT 'Active' AS {QLabel}, {Number("COUNT(*)")} AS {QValue} FROM {T(table)} WHERE {C(active)} = {yes} UNION ALL SELECT 'Inactive', {Number("COUNT(*)")} FROM {T(table)} WHERE {C(active)} = {no}";
                why = $"{active.Name} marks a row active or inactive.";
            }
            else if (table.HasSoftDelete && table.IsDeletedColumn is { IsBooleanColumn: true } deleted)
            {
                string yes = di.BooleanLiteral(true), no = di.BooleanLiteral(false);
                sql = $"SELECT 'Live' AS {QLabel}, {Number("COUNT(*)")} AS {QValue} FROM {T(table)} WHERE {C(deleted)} = {no} UNION ALL SELECT 'Deleted', {Number("COUNT(*)")} FROM {T(table)} WHERE {C(deleted)} = {yes}";
                why = $"{deleted.Name} marks a row deleted without removing it.";
            }
            else
                return;
            Add(DashboardWidgetKind.Ratio, $"{Plural(table.TableName)}: " + (table.HasActiveInactivePair ? "Active and Inactive" : "Live and Deleted"), table, null, why, sql, 60 - Penalty(table));
        }

        private void Status(TableModel table)
        {
            if (!table.HasStartEndDatePair || table.StartDateColumn is not { IsDateColumn: true } start || table.EndDateColumn is not { IsDateColumn: true } end)
                return;
            string now = di.CurrentTimestamp;
            string sql = $"SELECT 'Current' AS {QLabel}, {Number("COUNT(*)")} AS {QValue} FROM {T(table)} WHERE ({C(start)} IS NULL OR {C(start)} <= {now}) AND ({C(end)} IS NULL OR {C(end)} >= {now}) "
                + $"UNION ALL SELECT 'Expired', {Number("COUNT(*)")} FROM {T(table)} WHERE {C(end)} < {now} "
                + $"UNION ALL SELECT 'Not started', {Number("COUNT(*)")} FROM {T(table)} WHERE {C(start)} > {now}";
            Add(DashboardWidgetKind.Status, $"{Plural(table.TableName)} by Dates", table, null, $"{start.Name} and {end.Name} bound when a {Words(table.TableName)} row applies.", sql, 55 - Penalty(table));
        }

        private void Recent(TableModel table)
        {
            var display = DisplayOf(table);
            if (display is null || table.PrimaryKeyColumns.Count != 1)
                return;
            var date = DateOf(table);
            string detail = date is not null ? di.DateText(C(date)) : di.ToText(C(table.PrimaryKeyColumns[0]));
            string order = (date is not null ? C(date) + " DESC, " : "") + C(table.PrimaryKeyColumns[0]) + " DESC";
            string sql = $"SELECT {di.ToText(C(display))} AS {QLabel}, {detail} AS {QDetail} FROM {T(table)} ORDER BY {order} {di.Paging("0", TopRows.ToString())}";
            int master = table.HasAtLeastOneChildForeignKey ? 6 : 0;
            Add(DashboardWidgetKind.Recent, $"Latest {Plural(table.TableName)}", table, null,
                $"The newest rows by {(date is null ? table.PrimaryKeyColumns[0].Name : date.Name)}, each shown by {display.Name}.", sql, 30 + RowBonus(table) + master - Penalty(table));
        }

        private DashboardWidget MeasureWidget(TableModel table, ColumnModel column, DashboardMeasure measure, ColumnModel? date)
        {
            string aggregate = measure.Aggregate;
            string label = char.ToUpperInvariant(aggregate[0]) + aggregate[1..];
            string title = $"{Words(table.TableName)} {ColumnWords(column)}";
            string why = $"The project's DashboardMeasures names it: {aggregate} of {table.TableName}.{column.Name}" + (date is null ? "." : $" per {BucketWord(measure.Bucket!.Value).ToLowerInvariant()} of {date.Name}.");
            string sql;
            DashboardWidgetKind kind;
            if (date is null)
            {
                string expression = aggregate == "count" ? "COUNT(*)" : aggregate.ToUpperInvariant() + "(" + Amount(column) + ")";
                sql = $"SELECT {Lit(label)} AS {QLabel}, COALESCE({Number(expression)}, 0) AS {QValue} FROM {T(table)}";
                kind = DashboardWidgetKind.Money;
            }
            else
            {
                sql = TrendSql(table, date, measure.Bucket!.Value, aggregate, aggregate == "count" ? null : column);
                title += $" per {BucketWord(measure.Bucket.Value)}";
                kind = DashboardWidgetKind.Trend;
            }

            string id = ScreenNames.Route(table.TableName) + "-measure-" + ScreenNames.Route(column.Name) + "-" + aggregate + (date is null ? "" : "-" + BucketWord(measure.Bucket!.Value).ToLowerInvariant());
            return new DashboardWidget(id, kind, title, table.TableName, why, sql, int.MaxValue);
        }
    }
}
