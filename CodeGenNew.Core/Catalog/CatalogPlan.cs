using CodeGenNew.Core.Settings;

namespace CodeGenNew.Core.Catalog;

public static class CatalogWords
{
    public const string BadFilterType = ProblemTypes.Prefix + "facet-filter-invalid";
    public const string BadFilterTitle = "Not a valid facet filter";
    public const string BadFilter = "A filter is written Name:Value (filter=Voltage:120 V), with a name and a value; a range is written Name:Min:Max (range=Voltage:100:300), with numbers and at least one end.";
    public const string BadPageType = ProblemTypes.Prefix + "facet-page-invalid";
    public const string BadPageTitle = "Not a valid page";
    public const string BadPage = "The page and the page size must be whole numbers above zero.";
}

/// <summary> <c>FacetTables=ProductAttribute[:ProductColumn[:NameColumn[:ValueColumn]]]</c>: an attribute table (one row for each attribute a product carries: the product, the attribute's name and its value) read back as facets.
/// The product table is the parent of the product column; it gets two routes, <c>facets</c> (the values of each attribute with the number of products that carry them, for the current filters) and <c>narrow</c> (the page of products that match). </summary>
public sealed record CatalogShape(TableModel Product, ColumnModel Key, TableModel Attributes, ColumnModel AttributeKey, ColumnModel AttributeProduct, ColumnModel AttributeName, ColumnModel AttributeValue,
    IReadOnlyList<ColumnModel> SearchColumns, ColumnModel? Title, ColumnModel? Code, ColumnModel? Detail, ColumnModel? AttributeNumber, bool FullText);

public static class CatalogPlan
{
    public static readonly string[] NameColumns = ["Name", "AttributeName", "Attribute", "Key", "Property"];
    public static readonly string[] ValueColumns = ["Value", "AttributeValue", "PropertyValue"];
    public static readonly string[] NumberColumns = ["NumberValue", "NumericValue", "ValueNumber", "AttributeNumber"];
    public static readonly string[] TitleColumns = ["Name", "Title", "Description", "ShortDescription", "ProductName"];
    public static readonly string[] CodeColumns = ["CatalogNumber", "CatalogNo", "PartNumber", "Sku", "Code", "ItemNumber", "ProductCode"];

    public const string RangeParameter = "range";
    public const string FacetsRoute = "facets";
    public const string NarrowRoute = "narrow";
    public const string DefaultPageSize = "25";
    public const int MaxPageSize = 200;
    public const int MaxFacets = 12;
    public const int MaxValues = 12;

    /// <summary> One entry of the setting: the attribute table and, when it is named, the columns. </summary>
    public sealed record Entry(string Table, string? Product, string? Name, string? Value, string? Number = null);

    public static IReadOnlyList<Entry> Entries(ProjectSettings project) =>
        SettingParts.Items(project.FacetTables).Select(p => new Entry(p[0], SettingParts.At(p, 1), SettingParts.At(p, 2), SettingParts.At(p, 3), SettingParts.At(p, 4))).ToList();

    public static bool IsAttributeTable(string table, ProjectSettings project) => Entries(project).Any(e => e.Table.EqualsIgnoreCase(table));

    /// <summary> The shape of a product table: the attribute table whose product column points at it. Null when the table has none or the entry has a problem. </summary>
    public static CatalogShape? ForProduct(TableModel table, ProjectSettings project, DatabaseModel database)
    {
        foreach (var entry in Entries(project))
            if (Resolve(entry, project, database, out _) is { } shape && shape.Product.TableName.EqualsIgnoreCase(table.TableName))
                return shape;
        return null;
    }

    public static CatalogShape? ForProduct(TableModel table, ProjectSettings project) =>
        Rate.RatePlan.Remembered(project) is { } d ? ForProduct(table, project, d) : null;

    /// <summary> Whether the table is a product table of the setting that resolves. </summary>
    public static bool IsProduct(TableModel table, ProjectSettings project) => ForProduct(table, project) is not null;

    public static List<string> Problems(DatabaseModel database, ProjectSettings project)
    {
        var problems = new List<string>();
        foreach (var entry in Entries(project))
        {
            Resolve(entry, project, database, out string? problem);
            if (problem is not null)
                problems.Add(problem);
        }
        return problems;
    }

    /// <summary> One sentence for each attribute table without an index that starts with the product and the name: each facet reads the rows of one attribute name for a set of products. </summary>
    public static List<string> MissingIndexes(DatabaseModel database, ProjectSettings project)
    {
        var missing = new List<string>();
        foreach (var entry in Entries(project))
            if (Resolve(entry, project, database, out _) is { } shape && !shape.Attributes.Indexes.Any(i => i.Columns.Count >= 2 && i.Columns[0].EqualsIgnoreCase(shape.AttributeProduct.DbName) && i.Columns[1].EqualsIgnoreCase(shape.AttributeName.DbName)
                || i.Columns.Count >= 2 && i.Columns[0].EqualsIgnoreCase(shape.AttributeName.DbName) && i.Columns[1].EqualsIgnoreCase(shape.AttributeValue.DbName)))
                missing.Add($"{shape.Attributes.DbTableName} is a facet table without an index on ({shape.AttributeProduct.DbName}, {shape.AttributeName.DbName}) or ({shape.AttributeName.DbName}, {shape.AttributeValue.DbName}): the facet counts read it once for each attribute. Add one.");
        return missing;
    }

    public static CatalogShape? Resolve(Entry entry, ProjectSettings project, DatabaseModel database, out string? problem)
    {
        problem = null;
        string syntax = $"FacetTables={entry.Table}[:ProductColumn[:NameColumn[:ValueColumn[:NumberColumn]]]]";
        if (database.Tables.FirstOrDefault(t => t.TableName.EqualsIgnoreCase(entry.Table)) is not { } attributes)
        {
            problem = $"FacetTables: {entry.Table} is not a table of the database.";
            return null;
        }
        if (attributes.PrimaryKeyColumns is not [{ IsIntegerColumn: true } attributeKey])
        {
            problem = $"FacetTables: {attributes.TableName} needs a single whole-number key.";
            return null;
        }
        var fks = attributes.ForeignKeys.Where(f => f.ReferencingColumns.Count == 1).ToList();
        ForeignKeyModel? fk = entry.Product is null
            ? (fks.Count == 1 ? fks[0] : null)
            : fks.FirstOrDefault(f => f.ReferencingColumns[0].EqualsIgnoreCase(entry.Product));
        if (fk is null)
        {
            problem = entry.Product is null
                ? $"FacetTables: {attributes.TableName} needs exactly one foreign key to the product table, or name the product column ({syntax})."
                : $"FacetTables: {attributes.TableName}.{entry.Product} is not a foreign key ({syntax}).";
            return null;
        }
        var productColumn = attributes.FindColumn(fk.ReferencingColumns[0]);
        var name = entry.Name is null ? FindText(attributes, NameColumns, 1) : attributes.FindColumn(entry.Name);
        var value = entry.Value is null ? FindText(attributes, ValueColumns, 2) : attributes.FindColumn(entry.Value);
        if (database.Tables.FirstOrDefault(t => t.TableName.EqualsIgnoreCase(fk.ReferencedTable)) is not { } product || product.PrimaryKeyColumns is not [{ } key] || productColumn is null || key.CSharpBase() != productColumn.CSharpBase())
        {
            problem = $"FacetTables: {attributes.TableName}'s product column must point at a product table with a single key of the same type ({syntax}).";
            return null;
        }
        if (name is not { IsStringColumn: true, IsNullable: false } || value is not { IsStringColumn: true } || name.Name.EqualsIgnoreCase(value.Name) || productColumn.IsNullable)
        {
            problem = $"FacetTables: {attributes.TableName} needs a product column that cannot be NULL and two text columns, the attribute's name (cannot be NULL) and its value ({syntax}).";
            return null;
        }
        var number = entry.Number is null ? NumberColumns.Select(n => attributes.Columns.FirstOrDefault(c => c.Name.EqualsIgnoreCase(n))).FirstOrDefault(c => c is { IsNumericColumn: true }) : attributes.FindColumn(entry.Number);
        if (entry.Number is not null && number is not { IsNumericColumn: true })
        {
            problem = $"FacetTables: {attributes.TableName}.{entry.Number} is not a numeric column ({syntax}).";
            return null;
        }
        if (project.FacetFullText && attributes.Dialect != SqlDialect.SqlServer)
        {
            problem = "FacetFullText: the keyword is read by a full-text index on SQL Server only.";
            return null;
        }
        if (product.TableName.EqualsIgnoreCase(attributes.TableName))
        {
            problem = $"FacetTables: {attributes.TableName} cannot be its own product table.";
            return null;
        }
        if (OwnedTable.OwnerColumn(product, project) is not null || OwnedTable.Through(product, project) is not null || Tenant.TenantPlan.OwnedBy(product, project) is not null)
        {
            problem = $"FacetTables: {product.TableName} is limited to an owner or a tenant, which the facet routes do not read yet.";
            return null;
        }
        var text = product.Columns.Where(c => c.IsStringColumn && !c.IsPrimaryKey && c.CharacterLength is > 0 and <= 400 && !c.IsCredentialColumn).ToList();
        var title = FindText(product, TitleColumns, 0, text) ?? text.FirstOrDefault();
        var code = FindText(product, CodeColumns, 0, text);
        var detail = text.FirstOrDefault(c => c != title && c != code && c.CharacterLength >= 20);
        return new CatalogShape(product, key, attributes, attributeKey, productColumn, name, value, text.Take(8).ToList(), title, code, detail, number, project.FacetFullText);
    }

    private static ColumnModel? FindText(TableModel table, string[] names, int ordinalFallback, IReadOnlyList<ColumnModel>? within = null)
    {
        var pool = within ?? table.Columns.Where(c => c.IsStringColumn && !c.IsPrimaryKey).ToList();
        foreach (string n in names)
            if (pool.FirstOrDefault(c => c.Name.EqualsIgnoreCase(n)) is { } hit)
                return hit;
        return within is null && ordinalFallback > 0 ? pool.Skip(ordinalFallback - 1).FirstOrDefault() : null;
    }
}
