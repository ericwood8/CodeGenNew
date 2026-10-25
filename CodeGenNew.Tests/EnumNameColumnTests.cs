using System.Data;
using CodeGenNew.Core;

namespace CodeGenNew.Tests;

/// <summary> A lookup table whose text column is called like the table (CompanyType.CompanyType) or anything else still names its enum members. </summary>
[TestClass]
public class EnumNameColumnTests
{
    private static async Task<string> Enum(string table, string textColumn, List<object?[]> rows)
    {
        var model = Sample.Table(table,
        [
            Sample.Column(table + "Id", SqlDbType.Int, primaryKey: true, ordinal: 1),
            Sample.Column(textColumn, SqlDbType.NVarChar, characters: 50, ordinal: 2)
        ], rows: rows);
        var result = await Repo.Cache.RunAsync(Repo.Template("CS_Enum_v1.tt"), model, ProjectSettings.FromValues([new("ProjectName", "Acme")]));
        Assert.IsTrue(result.Success, string.Join(" | ", result.Errors));
        return result.GeneratedText!;
    }

    [TestMethod]
    public async Task A_column_called_like_the_table_names_the_members()
    {
        Expect.Contains(await Enum("CompanyType", "CompanyType", [[1, "Carrier"]]), "Carrier = 1");
    }

    [TestMethod]
    public async Task The_first_text_column_names_the_members_when_nothing_else_fits()
    {
        Expect.Contains(await Enum("Priority", "Description", [[1, "Urgent"]]), "Urgent = 1");
    }

    [TestMethod]
    public void A_desktop_strategy_project_uses_pascal_names()
    {
        var settings = StrategyRules.ToSettings(new StrategyChoice(SqlDialect.SqlServer, StrategyUi.WinUI3, null, null, null), "Acme", "Db", "srv");
        Assert.AreEqual("Pascal", settings.Values["NamingStyle"]);
    }
}
