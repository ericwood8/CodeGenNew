using CodeGenNew.Core;
using CodeGenNew.TemplateEngine;

namespace CodeGenNew.Tests;

/// <summary> API_Junction and WinUI3_JunctionEditor call the junction routines the way each database wants (SQL Server EXEC, a PostgreSQL
/// function, a MySQL CALL). No database needed. </summary>
[TestClass]
public class JunctionDialectTests
{
    private static TableModel In(TableModel t, SqlDialect dialect) => new()
    {
        SchemaName = dialect == SqlDialect.PostgreSql ? "public" : t.SchemaName, TableName = t.TableName, QuotedName = t.QuotedName, Dialect = dialect,
        Columns = t.Columns, PrimaryKeyColumns = t.PrimaryKeyColumns, ForeignKeys = t.ForeignKeys, ChildForeignKeys = t.ChildForeignKeys,
        DisplayColumns = t.DisplayColumns, HasReferencedDisplayColumns = t.HasReferencedDisplayColumns, HasRowData = t.HasRowData, Rows = t.Rows,
        LookupShape = t.LookupShape
    };

    private static async Task<string> Api(SqlDialect dialect)
    {
        var result = await Repo.Cache.RunAsync(Repo.Template("API_Junction_v1.tt"), In(Sample.JunctionWithSurrogateKey(), dialect));
        Assert.IsTrue(result.Success, string.Join(" | ", result.Errors));
        return result.GeneratedText!.Replace("\r\n", "\n");
    }

    private static async Task<string> ViewModel(SqlDialect dialect)
    {
        var result = await Repo.Cache.RunAsync(Repo.Template("WinUI3_JunctionEditor_v1.tt"), In(Sample.JunctionWithSurrogateKey(), dialect));
        Assert.IsTrue(result.Success, string.Join(" | ", result.Errors));
        return GeneratedFiles.Split(result.GeneratedText!).Single(f => f.RelativePath.EndsWith("JunctionEditorViewModel.cs")).Content.Replace("\r\n", "\n");
    }

    [TestMethod]
    public async Task The_sql_server_api_still_execs_the_procedures()
    {
        string cs = await Api(SqlDialect.SqlServer);

        Expect.Contains(cs, "EXEC [dbo].[NameBaseGroupXref_List] @AnchorNameBaseID");
        Expect.Contains(cs, "new Microsoft.Data.SqlClient.SqlParameter(\"@AnchorNameBaseID\", anchorId)");
        Expect.Contains(cs, "EXEC [dbo].[NameBaseGroupXref_Link] {request.AnchorId}, {request.TargetId}");
        Expect.Contains(cs, "EXEC [dbo].[NameBaseGroupXref_Unlink] {request.AnchorId}, {request.TargetId}");
    }

    [TestMethod]
    public async Task The_postgres_api_calls_the_functions()
    {
        string cs = await Api(SqlDialect.PostgreSql);

        Expect.Contains(cs, "SELECT * FROM \\\"public\\\".\\\"NameBaseGroupXref_List\\\"(@AnchorNameBaseID)");
        Expect.Contains(cs, "new Npgsql.NpgsqlParameter(\"@AnchorNameBaseID\", anchorId)");
        Expect.Contains(cs, "SELECT \\\"public\\\".\\\"NameBaseGroupXref_Link\\\"({request.AnchorId}, {request.TargetId})");
        Expect.DoesNotContain(cs, "EXEC");
    }

    [TestMethod]
    public async Task The_mysql_api_calls_the_procedures()
    {
        string cs = await Api(SqlDialect.MySql);

        Expect.Contains(cs, "CALL `NameBaseGroupXref_List`(@AnchorNameBaseID)");
        Expect.Contains(cs, "new MySql.Data.MySqlClient.MySqlParameter(\"@AnchorNameBaseID\", anchorId)");
        Expect.Contains(cs, "CALL `NameBaseGroupXref_Unlink`({request.AnchorId}, {request.TargetId})");
        Expect.DoesNotContain(cs, "EXEC");
    }

    [TestMethod]
    public async Task The_desktop_editor_calls_the_same_routines()
    {
        Expect.Contains(await ViewModel(SqlDialect.SqlServer), "EXEC [dbo].[NameBaseGroupXref_Link] {_anchorId}, {item.TargetId}");
        Expect.Contains(await ViewModel(SqlDialect.PostgreSql), "SELECT \\\"public\\\".\\\"NameBaseGroupXref_Link\\\"({_anchorId}, {item.TargetId})");
        Expect.Contains(await ViewModel(SqlDialect.MySql), "CALL `NameBaseGroupXref_List`(@AnchorNameBaseID)");
    }

    [TestMethod]
    public async Task The_desktop_editors_xaml_is_well_formed_xml()
    {
        // A "--" inside an XML comment (an easy slip in a template's explanatory text) breaks the XAML compiler, not the template run.
        var result = await Repo.Cache.RunAsync(Repo.Template("WinUI3_JunctionEditor_v1.tt"), In(Sample.JunctionWithSurrogateKey(), SqlDialect.PostgreSql));
        var xaml = GeneratedFiles.Split(result.GeneratedText!).Single(f => f.RelativePath.EndsWith(".xaml")).Content;

        System.Xml.Linq.XDocument.Parse(xaml);
    }
}

/// <summary> Template text must not use "--" as a dash: inside a generated XML / XAML comment it is illegal and breaks the compiler of the generated
/// file, not the template. Allowed: the comment delimiters, an SQL comment in a stored-procedure template and C# decrement. </summary>
[TestClass]
public class TemplateDashTests
{
    [TestMethod]
    public void No_template_uses_a_double_hyphen_as_punctuation()
    {
        var offenders = new List<string>();
        foreach (string path in Directory.GetFiles(Repo.TemplatesDirectory, "*.tt"))
        {
            bool sp = Path.GetFileName(path).StartsWith("SP_");
            int number = 0;
            foreach (string line in File.ReadAllLines(path))
            {
                number++;
                string rest = line.Replace("<!--", "").Replace("-->", "").Replace("PageNumber--", "");
                if (sp && (rest.TrimStart().StartsWith("-- ") || rest.Contains("; -- ") || rest.TrimStart() == "--"))
                    continue;
                if (rest.Contains("--"))
                    offenders.Add($"{Path.GetFileName(path)}:{number}: {line.Trim()}");
            }
        }
        Assert.IsEmpty(offenders, string.Join("\n", offenders.Take(10)));
    }
}
