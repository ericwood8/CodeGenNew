using CodeGenNew.Core.Api;
using CodeGenNew.Core.Naming;

namespace CodeGenNew.Core.Catalog;

/// <summary> What the generated facet tests of the Python and Rust APIs send (PY_TestCatalog, RS_TestCatalog): a product row and an attribute row with an example value for every column that must have one, the routes and the JSON names the tests
/// override. A test adds products of its own whose title starts with a token of its own and asks for that keyword, so the rows other tests left in the scratch database do not count. </summary>
public sealed record CatalogTestPlan(CatalogShape Shape, string ProductBody, string AttributeBody, string ProductRoute, string AttributeRoute, string KeyJson, string TitleJson, IReadOnlyList<string> UniqueJson,
    string ProductColumnJson, string NameJson, string ValueJson, string? NumberJson)
{
    /// <summary> The plan of the first product table of <c>FacetTables</c> the tests can fill, or null: a column that must have a value is a foreign key (other than the product column), has no example, is unique and not text,
    /// or the title is too short to hold a token. </summary>
    public static CatalogTestPlan? For(DatabaseModel database, ProjectSettings project)
    {
        foreach (var table in database.Tables.OrderBy(t => t.TableName, StringComparer.OrdinalIgnoreCase))
        {
            if (CatalogPlan.ForProduct(table, project, database) is not { Title: { } title } shape || title.CharacterLength is > 0 and < 24)
                continue;
            var unique = new List<string>();
            if (BodyOf(shape.Product, project, null, unique, skip: [shape.Key.Name], overridden: [title.Name]) is not { } product)
                continue;
            if (BodyOf(shape.Attributes, project, shape.AttributeProduct, [], skip: [shape.AttributeKey.Name], overridden: [shape.AttributeProduct.Name, shape.AttributeName.Name, shape.AttributeValue.Name, shape.AttributeNumber?.Name ?? ""]) is not { } attribute)
                continue;
            if (unique.Any(name => shape.Product.FindColumn(name) is { CharacterLength: > 0 and < 24 }))
                continue;
            return new CatalogTestPlan(shape, product, attribute, ApiRequests.Route(shape.Product), ApiRequests.Route(shape.Attributes), JsonNames.Camel(shape.Key.Name), JsonNames.Camel(title.Name),
                unique.Select(JsonNames.Camel).ToList(), JsonNames.Camel(shape.AttributeProduct.Name), JsonNames.Camel(shape.AttributeName.Name), JsonNames.Camel(shape.AttributeValue.Name),
                shape.AttributeNumber is null ? null : JsonNames.Camel(shape.AttributeNumber.Name));
        }
        return null;
    }

    private static string? BodyOf(TableModel table, ProjectSettings project, ColumnModel? allowedForeignKey, List<string> unique, string[] skip, string[] overridden)
    {
        var parts = new List<string>();
        foreach (var c in ApiSampleValues.BodyColumns(table))
        {
            string value;
            if (skip.Any(s => s.EqualsIgnoreCase(c.Name)))
                value = "0";
            else if (table.ForeignKeys.Any(f => f.ReferencingColumns.Any(r => r.EqualsIgnoreCase(c.Name))) && !(allowedForeignKey is not null && allowedForeignKey.Name.EqualsIgnoreCase(c.Name)))
                return null;
            else if (c.IsInUniqueIndex)
            {
                if (!c.IsStringColumn)
                    return null;
                unique.Add(c.Name);
                value = "\"\"";
            }
            else
                value = overridden.Any(o => o.EqualsIgnoreCase(c.Name)) ? (c.IsStringColumn ? "\"\"" : "0") : c.ValidExample(project, table.TableName);
            if (value == "null" && !c.IsNullable)
                return null;
            parts.Add(ApiSampleValues.Json(JsonNames.Camel(c.Name)) + ": " + value);
        }
        return "{ " + string.Join(", ", parts) + " }";
    }
}
