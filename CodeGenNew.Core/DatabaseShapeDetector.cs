using CodeGenNew.Core.Allowance;
using System.Text.RegularExpressions;
using CodeGenNew.Core.Party;
using CodeGenNew.Core.Rate;
using CodeGenNew.Core.Tax;
using CodeGenNew.Core.Tenant;

namespace CodeGenNew.Core;

/// <summary> The shapes that need more than one table to see: a polymorphic link (its tables come from a check constraint), a snapshot (the parents it copies from), an archive beside its table, and the project-wide sets
/// (tenant table, user table, roles and permissions, feature flags). Like <see cref="TableShapeDetector"/> it only suggests. </summary>

public static class DatabaseShapeDetector
{
    private static readonly string[] TypeSuffixes = ["Type", "Table", "Kind"];
    private static readonly string[] TenantNames = ["TenantId", "OrganizationId", "AccountId", "CompanyId", "OrgId"];
    private static readonly string[] ArchiveSuffixes = ["Archive", "_Archive", "History", "_History"];
    private static readonly string[] FeatureDefaultNames = ["DefaultEnabled", "DefaultOn", "IsEnabled", "Enabled"];

    public static IReadOnlyList<DatabaseShapeDetection> Of(DatabaseModel database, ProjectSettings project) => [.. Detect(database, project)];

    private static IEnumerable<DatabaseShapeDetection> Detect(DatabaseModel database, ProjectSettings project)
    {
        foreach (var table in database.Tables.OrderBy(t => t.TableName, StringComparer.OrdinalIgnoreCase))
        {
            foreach (var s in TableShapeDetector.Of(table, project))
                yield return new(table.TableName, s);
            foreach (var s in new[] { Polymorphic(table, database, project), Snapshot(table, database, project), Archive(table, database, project), Party(table, database, project), Rate(table, database, project), TaxLine(table, database, project), Allowance(table, database, project),
                Approval(table, database, project), RateDocument(table, database, project) })
                if (s is not null)
                    yield return new(table.TableName, s);
        }
        foreach (var d in new[] { Tenant(database, project), User(database, project), Permissions(database, project), Features(database, project), Subscription(database, project) })
            if (d is not null)
                yield return d;
    }

    // a project-wide choice is still about one table, the one that holds it, so the right-click menu of that table offers it
    private static DatabaseShapeDetection ProjectWide(DatabaseModel database, string table, ShapeSuggestion suggestion) =>
        new(database.Tables.FirstOrDefault(t => t.TableName.EqualsIgnoreCase(table))?.TableName ?? table, suggestion, true);

    private static readonly string[] DocCurrencyNames = ["CurrencyCode", "Currency"];
    private static readonly string[] DocRateNames = ["ExchangeRate", "FxRate", "ConversionRate"];
    private static readonly string[] DocAmountNames = ["Total", "TotalAmount", "Amount", "GrandTotal"];
    private static readonly string[] DocBaseNames = ["BaseTotal", "BaseAmount", "TotalBase", "ConvertedTotal", "ConvertedAmount"];
    private static readonly string[] DocDayNames = ["InvoiceDate", "DocumentDate", "OrderDate", "IssueDate", "TaxPoint"];
    private static readonly string[] ApprovalSideColumns = ["ApprovalStatusId", "RequestedBy", "RequestedOn", "DecidedBy", "Revision"];

    /// <summary> A table with a <c>&lt;Table&gt;Approval</c> side table that has the columns of a request cycle (the table SP_ApprovalTable writes). </summary>
    private static ShapeSuggestion? Approval(TableModel table, DatabaseModel database, ProjectSettings project)
    {
        if (project.RowApprovals.Contains(table.TableName, StringComparer.OrdinalIgnoreCase) || table.PrimaryKeyColumns is not [{ IsIntegerColumn: true }])
            return null;
        var side = database.Tables.FirstOrDefault(t => t.TableName.EqualsIgnoreCase(ApprovalPlan.SideName(table.TableName)));
        if (side is null || !ApprovalSideColumns.Append(table.TableName + "Id").All(n => side.Columns.Any(c => c.Name.EqualsIgnoreCase(n))))
            return null;
        var listed = project.WithOverrides([new("RowApprovals", string.Join(",", project.RowApprovals.Append(table.TableName)))]);
        string? problem = ApprovalPlan.Resolve(database, listed).Problems.FirstOrDefault(p => p.StartsWith($"RowApprovals: {table.TableName}", StringComparison.OrdinalIgnoreCase) || p.StartsWith("RowApprovals needs", StringComparison.Ordinal));
        string why = $"{side.TableName} holds one row for each request to approve a row of {table.TableName}";
        return new ShapeSuggestion("Row approvals", "RowApprovals", table.TableName, problem is null, problem is null ? why : $"{why}, but {problem}");
    }

    /// <summary> A document with a currency, the rate it was written with, an amount and the amount in the base currency. </summary>
    private static ShapeSuggestion? RateDocument(TableModel table, DatabaseModel database, ProjectSettings project)
    {
        if (RateDocumentPlan.IsListed(table.TableName, project) || RatePlan.IsListed(table.TableName, project) || table.PrimaryKeyColumns is not [{ IsIntegerColumn: true }])
            return null;
        ColumnModel? Find(string[] names, Func<ColumnModel, bool> kind) => table.Columns.FirstOrDefault(c => !c.IsPrimaryKey && kind(c) && names.Any(n => c.Name.EqualsIgnoreCase(n)));
        bool Number(ColumnModel c) => !c.IsNullable && c.CSharpBase() is "decimal" or "double" or "float";
        var currency = Find(DocCurrencyNames, c => c.IsStringColumn && !c.IsNullable);
        var rate = Find(DocRateNames, Number);
        var amount = Find(DocAmountNames, Number);
        var baseAmount = Find(DocBaseNames, Number);
        if (currency is null || rate is null || amount is null || baseAmount is null)
            return null;
        var day = Find(DocDayNames, c => c.IsDateColumn);
        string item = $"{table.TableName}:{currency.Name}:{rate.Name}:{amount.Name}:{baseAmount.Name}" + (day is null ? "" : ":" + day.Name);
        var listed = project.WithOverrides([new("RateDocuments", string.Join(",", project.RateDocuments.Append(item)))]);
        string? problem = RateDocumentPlan.Problems(database, listed).FirstOrDefault(p => p.StartsWith($"RateDocuments: {table.TableName}", StringComparison.OrdinalIgnoreCase));
        string why = $"{currency.Name} and {rate.Name} turn {amount.Name} into {baseAmount.Name}, the way a document keeps its exchange rate";
        return new ShapeSuggestion("Exchange rate on a document", "RateDocuments", item, problem is null, problem is null ? why : $"{why}, but {problem}");
    }

    private static readonly string[] SubscriptionStatusNames = ["Status", "StatusId", "SubscriptionStatusId"];

    /// <summary> The one table with Subscription in its name, a plan and a status and a tenant column. It also needs <c>PlanTable</c>, so it fits only when the project names that. </summary>
    private static DatabaseShapeDetection? Subscription(DatabaseModel database, ProjectSettings project)
    {
        if (project.SubscriptionTable is not null)
            return null;
        var found = database.Tables.Where(t => t.TableName.ContainsIgnoreCase("Subscription") && t.Columns.Any(c => c.Name.EqualsIgnoreCase("PlanId") && c.IsInt32Column && !c.IsNullable)
            && t.Columns.Any(c => !c.IsNullable && SubscriptionStatusNames.Any(n => c.Name.EqualsIgnoreCase(n))) && t.Columns.Any(c => !c.IsNullable && c.IsInt32Column && TenantNames.Any(n => c.Name.EqualsIgnoreCase(n)))).ToList();
        if (found is not [var table])
            return null;
        var plan = table.ForeignKeys.FirstOrDefault(f => f.ReferencingColumns.Count == 1 && f.ReferencingColumns[0].EqualsIgnoreCase("PlanId"))?.ReferencedTable;
        var overrides = new List<KeyValuePair<string, string>> { new("SubscriptionTable", table.TableName) };
        if (project.PlanTable is null && plan is not null)
            overrides.Add(new("PlanTable", plan));
        var listed = project.WithOverrides(overrides);
        string? problem = project.PlanTable is null ? $"PlanTable={plan ?? "the plan table"} also has to be set" : SubscriptionFeature.Problems(database, listed).FirstOrDefault(p => p.StartsWith("SubscriptionTable:", StringComparison.Ordinal));
        string why = "has a plan, a status and a tenant column";
        return ProjectWide(database, table.TableName, new ShapeSuggestion("Subscription table", "SubscriptionTable", table.TableName, problem is null, problem is null ? why : $"{why}, but {problem}"));
    }

    private static ShapeSuggestion? Polymorphic(TableModel table, DatabaseModel database, ProjectSettings project)
    {
        if (PolymorphicPlan.IsLinkTable(table.TableName, project) || table.PrimaryKeyColumns is not [{ IsIntegerColumn: true }])
            return null;
        foreach (var type in table.Columns.Where(c => c.IsStringColumn && !c.IsPrimaryKey))
        {
            string? suffix = TypeSuffixes.FirstOrDefault(s => type.Name.EndsWithIgnoreCase(s) && type.Name.Length > s.Length);
            if (suffix is null)
                continue;
            string stem = type.Name[..^suffix.Length];
            var id = table.Columns.FirstOrDefault(c => c.IsIntegerColumn && !c.IsPrimaryKey && c.Name.EqualsIgnoreCase(stem + "Id"));
            if (id is null || table.ForeignKeys.Any(f => f.ReferencingColumns.Any(c => c.EqualsIgnoreCase(id.Name) || c.EqualsIgnoreCase(type.Name))))
                continue;
            var targets = database.Tables.Where(t => !t.TableName.EqualsIgnoreCase(table.TableName)
                && table.CheckConstraints.Any(k => k.Definition.ContainsIgnoreCase(type.Name) && Regex.IsMatch(k.Definition, $"'{Regex.Escape(t.TableName)}'", RegexOptions.IgnoreCase)))
                .Select(t => t.TableName).ToList();
            if (targets.Count < 2)
                return new ShapeSuggestion("Polymorphic link", "PolymorphicLinks", $"{table.TableName}:{type.Name}:{id.Name}", false,
                    $"{type.Name} and {id.Name} look like a link to rows of several tables, but no check constraint names the tables; name them as PolymorphicLinks={table.TableName}:{type.Name}:{id.Name}:Table1|Table2");
            string item = $"{table.TableName}:{type.Name}:{id.Name}:{string.Join("|", targets)}";
            var listed = project.WithOverrides([new("PolymorphicLinks", string.Join(",", project.PolymorphicLinks.Append(item)))]);
            string? problem = PolymorphicPlan.Problems(database, listed).FirstOrDefault(p => p.StartsWith($"PolymorphicLinks: {table.TableName}", StringComparison.Ordinal));
            return new ShapeSuggestion("Polymorphic link", "PolymorphicLinks", item, problem is null, problem is null ? $"{type.Name} names one of {string.Join(", ", targets)} and {id.Name} is its key" : $"{type.Name} and {id.Name} look like a link, but {problem}");
        }
        return null;
    }

    private static readonly string[] PartySchemeNames = ["Scheme", "SchemeCode", "IdentifierType", "IdentifierScheme"];
    private static readonly string[] PartyValueNames = ["Value", "Identifier", "IdentifierValue", "Number"];

    /// <summary> A table of identifiers: a scheme and a value, both text that cannot be NULL, under one foreign key to another table. </summary>
    private static ShapeSuggestion? Party(TableModel table, DatabaseModel database, ProjectSettings project)
    {
        if (PartyPlan.IsListed(table.TableName, project))
            return null;
        var scheme = table.Columns.FirstOrDefault(c => c.IsStringColumn && !c.IsNullable && PartySchemeNames.Any(n => c.Name.EqualsIgnoreCase(n)));
        var value = table.Columns.FirstOrDefault(c => c.IsStringColumn && !c.IsNullable && PartyValueNames.Any(n => c.Name.EqualsIgnoreCase(n)));
        var parents = table.ForeignKeys.Where(f => f.ReferencingColumns.Count == 1 && !f.ReferencedTable.EqualsIgnoreCase(table.TableName)).Select(f => f.ReferencedTable).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        if (scheme is null || value is null || parents.Count != 1 || PartyPlan.IsPartyTable(parents[0], project))
            return null;
        string item = $"{parents[0]}:{table.TableName}" + (scheme.Name.EqualsIgnoreCase(PartyPlan.DefaultScheme) && value.Name.EqualsIgnoreCase(PartyPlan.DefaultValue) ? "" : $":{scheme.Name}:{value.Name}");
        var listed = project.WithOverrides([new("PartyTables", string.Join(",", project.PartyTables.Append(item)))]);
        string? problem = PartyPlan.Problems(database, listed).FirstOrDefault(p => p.StartsWith($"PartyTables: {parents[0]}", StringComparison.OrdinalIgnoreCase));
        string why = $"{scheme.Name} and {value.Name} identify a row of {parents[0]}";
        return new ShapeSuggestion("Party identifiers", "PartyTables", item, problem is null, problem is null ? why : $"{why}, but {problem}");
    }

    private static readonly string[] RateFromNames = [RatePlan.DefaultFrom, "FromCurrencyCode", "BaseCurrency", "BaseCurrencyCode", "SourceCurrency"];
    private static readonly string[] RateToNames = [RatePlan.DefaultTo, "ToCurrencyCode", "QuoteCurrency", "QuoteCurrencyCode", "TargetCurrency"];
    private static readonly string[] RateNames = [RatePlan.DefaultRate, "ExchangeRate", "FxRate", "ConversionRate"];
    private static readonly string[] RateDayNames = [RatePlan.DefaultDay, "ExchangeDate", "EffectiveDate", "AsOfDate", "OnDate", "ValidFrom"];

    /// <summary> A table of exchange rates: two currency columns, a rate and a day. </summary>
    private static ShapeSuggestion? Rate(TableModel table, DatabaseModel database, ProjectSettings project)
    {
        if (RatePlan.IsListed(table.TableName, project))
            return null;
        ColumnModel? Find(string[] names, Func<ColumnModel, bool> kind) => table.Columns.FirstOrDefault(c => !c.IsNullable && kind(c) && names.Any(n => c.Name.EqualsIgnoreCase(n)));
        var from = Find(RateFromNames, c => c.IsStringColumn);
        var to = Find(RateToNames, c => c.IsStringColumn);
        var rate = Find(RateNames, c => c.IsNumericColumn);
        var day = Find(RateDayNames, c => c.IsDateColumn);
        if (from is null || to is null || rate is null || day is null)
            return null;
        bool plain = from.Name.EqualsIgnoreCase(RatePlan.DefaultFrom) && to.Name.EqualsIgnoreCase(RatePlan.DefaultTo) && rate.Name.EqualsIgnoreCase(RatePlan.DefaultRate) && day.Name.EqualsIgnoreCase(RatePlan.DefaultDay);
        string item = plain ? table.TableName : $"{table.TableName}:{from.Name}:{to.Name}:{rate.Name}:{day.Name}";
        var listed = project.WithOverrides([new("RateTables", string.Join(",", project.RateTables.Append(item)))]);
        string? problem = RatePlan.Problems(database, listed).FirstOrDefault(p => p.StartsWith($"RateTables: {table.TableName}", StringComparison.OrdinalIgnoreCase));
        string why = $"{from.Name} and {to.Name} with a {rate.Name} from {day.Name} look like exchange rates";
        return new ShapeSuggestion("Exchange rates", "RateTables", item, problem is null, problem is null ? why : $"{why}, but {problem}");
    }

    private static readonly string[] TaxRateNames = ["TaxRate", "VatRate", "SalesTaxRate", "TaxPercent", "TaxPercentage"];
    private static readonly string[] TaxAmountNames = ["TaxAmount", "VatAmount", "Tax", "Vat", "SalesTax"];
    private static readonly string[] TaxableNames = ["LineTotal", "LineAmount", "TaxableAmount", "NetAmount", "Net", "Subtotal", "Amount"];
    private static readonly string[] CategoryRateNames = [TaxWords.DefaultCategoryRate, "Percent", "Percentage", "TaxRate", "RatePercent"];
    private static readonly string[] CategoryFromNames = ["ValidFrom", "EffectiveFrom", "StartDate", "EffectiveDate"];
    private static readonly string[] CategoryToNames = ["ValidTo", "EffectiveTo", "EndDate", "ExpiryDate"];
    private static readonly string[] LineDayNames = ["LineDate", "TaxPoint", "TaxDate", "InvoiceDate", "DocumentDate"];
    private static readonly string[] DocumentNames = ["InvoiceId", "DocumentId", "OrderId", "SalesInvoiceId", "SalesOrderId"];

    /// <summary> A document line with a key to a tax category table, a tax rate, a taxable amount and a tax amount. </summary>
    private static ShapeSuggestion? TaxLine(TableModel table, DatabaseModel database, ProjectSettings project)
    {
        if (TaxLinePlan.IsListed(table.TableName, project) || table.PrimaryKeyColumns is not [{ IsIntegerColumn: true }])
            return null;
        ColumnModel? Find(IEnumerable<ColumnModel> columns, string[] names, Func<ColumnModel, bool> kind) => columns.FirstOrDefault(c => kind(c) && names.Any(n => c.Name.EqualsIgnoreCase(n)));
        bool Number(ColumnModel c) => !c.IsNullable && c.CSharpBase() is "decimal" or "double" or "float";
        var rate = Find(table.Columns, TaxRateNames, Number);
        var tax = Find(table.Columns, TaxAmountNames, Number);
        var taxable = Find(table.Columns, TaxableNames, Number);
        if (rate is null || tax is null || taxable is null)
            return null;
        foreach (var fk in table.ForeignKeys.Where(f => f.ReferencingColumns.Count == 1 && !f.ReferencedTable.EqualsIgnoreCase(table.TableName)))
        {
            var category = table.Columns.FirstOrDefault(c => c.Name.EqualsIgnoreCase(fk.ReferencingColumns[0]));
            var parent = database.Tables.FirstOrDefault(t => t.TableName.EqualsIgnoreCase(fk.ReferencedTable));
            if (category is not { IsIntegerColumn: true } || parent is null || !parent.TableName.ContainsIgnoreCase("Tax") && !parent.TableName.ContainsIgnoreCase("Vat") && !parent.TableName.ContainsIgnoreCase("Levy"))
                continue;
            var parentRate = Find(parent.Columns, CategoryRateNames, Number);
            if (parentRate is null)
                continue;
            var from = Find(parent.Columns, CategoryFromNames, c => !c.IsNullable && c.IsDateColumn);
            var to = from is null ? null : Find(parent.Columns, CategoryToNames, c => c.IsDateColumn);
            var day = from is null ? null : Find(table.Columns, LineDayNames, c => !c.IsNullable && c.IsDateColumn);
            var document = Find(table.Columns, DocumentNames, c => c.IsIntegerColumn && !c.IsPrimaryKey);
            var label = parent.Columns.FirstOrDefault(c => c.IsStringColumn && (c.Name.EqualsIgnoreCase("Code") || c.Name.EqualsIgnoreCase("Name")));
            var parts = new List<string> { table.TableName, category.Name, rate.Name, taxable.Name, tax.Name, parent.TableName, parentRate.Name, from?.Name ?? "", to?.Name ?? "", day?.Name ?? "", document?.Name ?? "", document is null ? "" : label?.Name ?? "" };
            while (parts[^1].Length == 0)
                parts.RemoveAt(parts.Count - 1);
            string item = string.Join(":", parts);
            var listed = project.WithOverrides([new("TaxLines", string.Join(",", project.TaxLines.Append(item)))]);
            string? problem = TaxLinePlan.Problems(database, listed).FirstOrDefault(p => p.StartsWith($"TaxLines: {table.TableName}", StringComparison.OrdinalIgnoreCase));
            string why = $"{category.Name} points at {parent.TableName}, whose {parentRate.Name} looks like the rate the line carries in {rate.Name}, with the tax in {tax.Name}";
            return new ShapeSuggestion("Tax lines", "TaxLines", item, problem is null, problem is null ? why : $"{why}, but {problem}");
        }
        return null;
    }

    private static readonly string[] AllowanceFlagNames = [AllowanceChargeWords.DefaultFlag, "IsAllowance", "ChargeIndicator", "IsSurcharge", "IsDiscount"];
    private static readonly string[] AllowanceBaseNames = [AllowanceChargeWords.DefaultBase, "BasisAmount", "BaseValue"];
    private static readonly string[] AllowancePercentNames = [AllowanceChargeWords.DefaultPercent, "PercentageRate", "Percent"];

    /// <summary> A table of discounts and surcharges: a yes/no column, a base amount, a percentage and an amount, in a table whose name says allowance, charge or discount. </summary>
    private static ShapeSuggestion? Allowance(TableModel table, DatabaseModel database, ProjectSettings project)
    {
        if (AllowanceChargePlan.IsListed(table.TableName, project) || table.PrimaryKeyColumns is not [{ IsIntegerColumn: true }]
            || !new[] { "Allowance", "Charge", "Discount" }.Any(n => table.TableName.ContainsIgnoreCase(n)))
            return null;
        ColumnModel? Find(string[] names, Func<ColumnModel, bool> kind) => table.Columns.FirstOrDefault(c => kind(c) && names.Any(n => c.Name.EqualsIgnoreCase(n)));
        var flag = Find(AllowanceFlagNames, c => !c.IsNullable && c.CSharpBase() == "bool");
        var baseColumn = Find(AllowanceBaseNames, AllowanceChargePlan.IsFractional);
        var percent = Find(AllowancePercentNames, AllowanceChargePlan.IsFractional);
        var amount = Find(["Amount", "ChargeAmount", "AllowanceAmount"], c => !c.IsNullable && AllowanceChargePlan.IsFractional(c));
        if (flag is null || baseColumn is null || percent is null || amount is null)
            return null;
        var document = table.ForeignKeys.Where(f => f.ReferencingColumns.Count == 1 && !f.ReferencedTable.EqualsIgnoreCase(table.TableName))
            .Select(f => table.Columns.FirstOrDefault(c => c.Name.EqualsIgnoreCase(f.ReferencingColumns[0]))).FirstOrDefault(c => c is { IsIntegerColumn: true } && DocumentNames.Any(n => c.Name.EqualsIgnoreCase(n)));
        string item = $"{table.TableName}:{flag.Name}:{baseColumn.Name}:{percent.Name}:{amount.Name}" + (document is null ? "" : ":" + document.Name);
        var listed = project.WithOverrides([new("AllowanceCharges", string.Join(",", project.AllowanceCharges.Append(item)))]);
        string? problem = AllowanceChargePlan.Problems(database, listed).FirstOrDefault(p => p.StartsWith($"AllowanceCharges: {table.TableName}", StringComparison.OrdinalIgnoreCase));
        string why = $"{flag.Name} says charge or allowance, and {amount.Name} can be worked out from {baseColumn.Name} and {percent.Name}";
        return new ShapeSuggestion("Allowances and charges", "AllowanceCharges", item, problem is null, problem is null ? why : $"{why}, but {problem}");
    }

    private static ShapeSuggestion? Snapshot(TableModel table, DatabaseModel database, ProjectSettings project)
    {
        if (SnapshotPlan.IsListed(table.TableName, project) || table.PrimaryKeyColumns.Count != 1 || !table.Columns.Any(c => c.Name.EqualsIgnoreCase("Quantity")))
            return null;
        var copies = new List<string>();
        foreach (var fk in table.ForeignKeys.Where(f => f.ReferencingColumns.Count == 1 && f.ReferencedColumns.Count == 1))
        {
            var parent = database.Tables.FirstOrDefault(t => t.TableName.EqualsIgnoreCase(fk.ReferencedTable));
            if (parent is null || parent.TableName.EqualsIgnoreCase(table.TableName))
                continue;
            foreach (var source in parent.Columns.Where(c => !c.IsPrimaryKey && !c.IsAuditColumn && !c.IsComputed))
                foreach (var own in table.Columns.Where(c => !c.IsPrimaryKey && !c.Name.EqualsIgnoreCase(fk.ReferencingColumns[0]) && !copies.Any(x => x.StartsWith(c.Name + "<-", StringComparison.OrdinalIgnoreCase))))
                {
                    bool named = own.Name.EqualsIgnoreCase(source.Name) || own.Name.EqualsIgnoreCase(parent.TableName + source.Name) || (source.Name.EqualsIgnoreCase("Price") && own.Name.EqualsIgnoreCase("UnitPrice"));
                    if (named && own.HasSameTypeAs(source) && own.CharacterLength == source.CharacterLength)
                        copies.Add($"{own.Name}<-{parent.TableName}.{source.Name}");
                }
        }
        return copies.Count == 0 ? null
            : new ShapeSuggestion("Snapshot at checkout", "SnapshotColumns", $"{table.TableName}:{string.Join("+", copies)}", false,
                $"has {table.Columns.First(c => c.Name.EqualsIgnoreCase("Quantity")).Name} and columns that look like copies of its parents' columns; if they are copies taken when the row is saved, SnapshotColumns={table.TableName}:{string.Join("+", copies)} writes them (check the list, this is a guess)");
    }

    private static ShapeSuggestion? Archive(TableModel table, DatabaseModel database, ProjectSettings project)
    {
        if (ArchivePlan.Entries(project).Any(e => e.Table.EqualsIgnoreCase(table.TableName)))
            return null;
        var archive = database.Tables.FirstOrDefault(t => ArchiveSuffixes.Any(s => t.TableName.EqualsIgnoreCase(table.TableName + s))
            && table.Columns.Where(c => !c.IsComputed).All(c => t.Columns.Any(a => a.Name.EqualsIgnoreCase(c.Name))));
        return archive is null ? null
            : new ShapeSuggestion("Archive", "ArchiveTables", $"{table.TableName}:90", false,
                $"{archive.TableName} has every column of {table.TableName}; ArchiveTables={table.TableName}:DAYS keeps that many days of rows and moves older ones there (choose the days; the plan expects the name {ArchivePlan.ArchiveName(table.TableName)})");
    }

    private static DatabaseShapeDetection? Tenant(DatabaseModel database, ProjectSettings project)
    {
        if (project.TenantTable is not null)
            return null;
        var owners = database.Tables.SelectMany(t => t.ForeignKeys.Where(f => f.ReferencingColumns.Count == 1 && TenantNames.Any(n => n.EqualsIgnoreCase(f.ReferencingColumns[0]))
                && t.Columns.Any(c => c.Name.EqualsIgnoreCase(f.ReferencingColumns[0]) && !c.IsNullable && c.IsIntegerColumn) && !t.TableName.EqualsIgnoreCase(f.ReferencedTable)).Select(f => f.ReferencedTable))
            .GroupBy(n => n, StringComparer.OrdinalIgnoreCase).OrderByDescending(g => g.Count()).FirstOrDefault();
        if (owners is null || owners.Count() < 2 || owners.Count() * 2 < database.Tables.Count - 1)
            return null;
        return ProjectWide(database, owners.Key, new ShapeSuggestion("Tenant table", "TenantTable", owners.Key, project.ApiAuth,
            $"{owners.Count()} tables have a required tenant column that points at {owners.Key}{(project.ApiAuth ? "" : "; TenantTable also needs ApiAuth")}"));
    }

    private static DatabaseShapeDetection? User(DatabaseModel database, ProjectSettings project)
    {
        if (project.UserTable is not null)
            return null;
        var users = database.Tables.Where(t => t.Columns.Any(c => c.IsStringColumn && UserTable.UserNameNames.Any(n => c.Name.EqualsIgnoreCase(n)))
            && t.Columns.Any(c => c.IsStringColumn && UserTable.PasswordHashNames.Any(n => c.Name.EqualsIgnoreCase(n)))).ToList();
        return users is [var only]
            ? ProjectWide(database, only.TableName, new ShapeSuggestion("User table", "UserTable", only.TableName, project.ApiAuth, $"has a login name and a password hash{(project.ApiAuth ? "" : "; UserTable also needs ApiAuth")}"))
            : null;
    }

    private static DatabaseShapeDetection? Permissions(DatabaseModel database, ProjectSettings project)
    {
        if (project.PermissionTables.Length > 0)
            return null;
        TableModel? Find(params string[] names) => database.Tables.FirstOrDefault(t => names.Any(n => t.TableName.EqualsIgnoreCase(n)));
        var role = Find("Role", "Roles");
        var permission = Find("Permission", "Permissions");
        if (role is null || permission is null)
            return null;
        bool Points(TableModel t, TableModel to) => t.ForeignKeys.Any(f => f.ReferencingColumns.Count == 1 && f.ReferencedTable.EqualsIgnoreCase(to.TableName));
        var rolePermission = database.Tables.FirstOrDefault(t => t.IsJunctionTable && Points(t, role) && Points(t, permission));
        var userRole = database.Tables.FirstOrDefault(t => t.IsJunctionTable && Points(t, role) && !Points(t, permission));
        return rolePermission is null || userRole is null ? null
            : ProjectWide(database, role.TableName, new ShapeSuggestion("Roles and permissions", "PermissionTables", $"{role.TableName},{permission.TableName},{rolePermission.TableName},{userRole.TableName}", project.ApiAuth && project.UserTable is not null,
                $"{role.TableName}, {permission.TableName} and their two junctions are the four tables{(project.ApiAuth && project.UserTable is not null ? "" : "; PermissionTables also needs ApiAuth and UserTable")}"));
    }

    private static DatabaseShapeDetection? Features(DatabaseModel database, ProjectSettings project)
    {
        if (project.FeatureTables.Length > 0)
            return null;
        foreach (var flag in database.Tables.Where(t => t.PrimaryKeyColumns is [{ IsIntegerColumn: true }] && t.Columns.Any(c => c.Name.EqualsIgnoreCase("Name") && c.IsStringColumn)
            && t.Columns.Any(c => c.IsBooleanColumn && !c.IsNullable && FeatureDefaultNames.Any(n => c.Name.EqualsIgnoreCase(n)))))
        {
            var overrides = database.Tables.FirstOrDefault(t => t.ForeignKeys.Count(f => f.ReferencedTable.EqualsIgnoreCase(flag.TableName)) == 1 && t.Columns.Any(c => c.IsBooleanColumn && !c.IsNullable && c.Name.EqualsIgnoreCase("IsEnabled")));
            if (overrides is not null)
                return ProjectWide(database, flag.TableName, new ShapeSuggestion("Feature flags", "FeatureTables", $"{flag.TableName},{overrides.TableName}", project.ApiAuth, $"{flag.TableName} has a name and a default, and {overrides.TableName} refers to it with a yes/no column{(project.ApiAuth ? "" : "; FeatureTables also needs ApiAuth")}"));
        }
        return null;
    }
}

/// <summary> One suggestion of <see cref="DatabaseShapeDetector.Of"/>: the table it is about and the suggestion. <see cref="ProjectWide"/> is true when the setting is a choice for the whole project (the tenant table,
/// the user table, roles and permissions, feature flags, subscriptions); the table is the one that holds it, so the menu of that table offers it, and a run reports it without needing the table to have an API. </summary>
public sealed record DatabaseShapeDetection(string Table, ShapeSuggestion Suggestion, bool ProjectWide = false);
