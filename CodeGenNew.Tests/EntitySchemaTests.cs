using System.Data;
using CodeGenNew.Core;

namespace CodeGenNew.Tests;

/// <summary> A table outside the database's default schema must say its schema in the entity, or EF queries dbo.Product instead of Production.Product. </summary>
[TestClass]
public class EntitySchemaTests
{
    private static TableModel Product(SqlDialect dialect) => Sample.Table("Product",
    [
        Sample.Column("ProductId", SqlDbType.Int, primaryKey: true, identity: true, ordinal: 1),
        Sample.Column("Name", SqlDbType.NVarChar, characters: 50, ordinal: 2)
    ], dialect: dialect);

    private static async Task<string> Render(TableModel table)
    {
        var result = await Repo.Cache.RunAsync(Repo.Template("CS_Entity_v1.tt"), table, ProjectSettings.FromValues([new("ProjectName", "Acme")]));
        Assert.IsTrue(result.Success, string.Join(" | ", result.Errors));
        return result.GeneratedText!;
    }

    [TestMethod]
    public async Task The_default_schema_needs_no_table_attribute()
    {
        Expect.DoesNotContain(await Render(Product(SqlDialect.SqlServer)), "[Table(");
    }

    [TestMethod]
    public async Task A_postgres_table_outside_public_names_its_schema()
    {
        // the sample table's schema is dbo, which is not PostgreSQL's default
        Expect.Contains(await Render(Product(SqlDialect.PostgreSql)), "[Table(\"Product\", Schema = \"dbo\")]");
    }
}
