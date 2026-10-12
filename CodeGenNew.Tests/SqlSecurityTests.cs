using System.Data;
using System.Text.RegularExpressions;
using CodeGenNew.Core;

namespace CodeGenNew.Tests;

/// <summary> What the generated SQL does with text that came from a person (Docs/Security.md): a value is always a parameter, a LIKE pattern is escaped, a sort column is
/// compared with a fixed list, and no template builds a statement out of request text. The routines themselves are run with hostile text by
/// Docs/Verification/Test-SearchHostileText.py. </summary>
[TestClass]
public class SqlSecurityTests
{
    private static TableModel Customer(SqlDialect dialect) => Sample.Table("Customer",
    [
        Sample.Column("CustomerId", SqlDbType.Int, primaryKey: true, identity: true, ordinal: 1),
        Sample.Column("Name", SqlDbType.NVarChar, characters: 50, ordinal: 2),
        Sample.Column("Email", SqlDbType.VarChar, nullable: true, characters: 100, ordinal: 3)
    ], [], dialect: dialect);

    private static async Task<string> Search(SqlDialect dialect)
    {
        var result = await Repo.Cache.RunAsync(Repo.Template("SP_Search_v1.tt"), Customer(dialect), ProjectSettings.FromValues([new KeyValuePair<string, string>("ProjectName", "Acme")]));
        if (!result.Success)
            Assert.Fail(string.Join(" | ", result.Errors).ReplaceLineEndings(" "));
        return result.GeneratedText!.ReplaceLineEndings("\n");
    }

    [TestMethod]
    [DataRow(SqlDialect.SqlServer)]
    [DataRow(SqlDialect.PostgreSql)]
    [DataRow(SqlDialect.MySql)]
    public async Task Every_search_filter_escapes_the_wildcards_of_the_value_and_says_which_character_escapes(SqlDialect dialect)
    {
        string sql = await Search(dialect);

        int filters = Regex.Matches(sql, @"IS NULL OR ").Count;
        Assert.IsGreaterThan(0, filters);
        Assert.HasCount(filters, Regex.Matches(sql, "REPLACE\\(REPLACE\\(REPLACE\\("), "every filter escapes its value");
        Assert.HasCount(filters, Regex.Matches(sql, "ESCAPE '"), "every filter names the escape character");
    }

    [TestMethod]
    [DataRow(SqlDialect.SqlServer)]
    [DataRow(SqlDialect.PostgreSql)]
    [DataRow(SqlDialect.MySql)]
    public async Task The_sort_column_is_compared_with_a_fixed_list_and_never_becomes_text_in_the_statement(SqlDialect dialect)
    {
        string sql = await Search(dialect);

        Expect.Contains(sql, "CASE WHEN");
        foreach (string dynamic in new[] { "EXEC(", "EXECUTE(", "sp_executesql", "PREPARE ", "EXECUTE IMMEDIATE", "ORDER BY @SortColumn", "ORDER BY \"SortColumn\"", "ORDER BY `SortColumn`" })
            Expect.DoesNotContain(sql, dynamic);
        Assert.IsFalse(Regex.IsMatch(sql, @"(\+|\|\||CONCAT\()\s*(@SortColumn|""SortColumn""|`SortColumn`)"), "the sort name is never joined into text");
    }

    [TestMethod]
    public async Task An_unknown_sort_name_appears_nowhere_but_in_the_comparisons()
    {
        string sql = await Search(SqlDialect.SqlServer);

        foreach (Match use in Regex.Matches(sql, @"@SortColumn"))
        {
            string around = sql.Substring(use.Index, Math.Min(40, sql.Length - use.Index));
            Assert.IsTrue(around.StartsWith("@SortColumn NVARCHAR") || around.StartsWith("@SortColumn = N'"), "@SortColumn is a declaration or a comparison: " + around);
        }
    }

    // a raw-SQL call that is given an interpolated or joined string builds a statement from its values
    private static readonly Regex InterpolatedRawSql = new(@"(FromSqlRaw|SqlQueryRaw|ExecuteSqlRaw|ExecuteSqlRawAsync)\(\s*(\$@?""|@\$""|[A-Za-z_]+\s*\+)");

    [TestMethod]
    public void No_template_builds_a_raw_sql_statement_from_an_interpolated_or_joined_string()
    {
        var found = new List<string>();
        foreach (string path in Directory.GetFiles(Repo.TemplatesDirectory, "*.tt").Concat(Directory.GetFiles(Path.Combine(Repo.Root, "CodeGenNew.Core"), "*.cs")))
            foreach (string line in File.ReadLines(path))
                if (InterpolatedRawSql.IsMatch(line))
                    found.Add(Path.GetFileName(path) + ": " + line.Trim());

        Assert.IsEmpty(found, string.Join("\n", found));
    }

    [TestMethod]
    public void The_raw_sql_check_can_fail()
    {
        Assert.IsTrue(InterpolatedRawSql.IsMatch("await context.Database.ExecuteSqlRawAsync($\"DELETE FROM t WHERE Name = '{name}'\");"));
        Assert.IsTrue(InterpolatedRawSql.IsMatch("context.Set<T>().FromSqlRaw(text + name)"));
        Assert.IsFalse(InterpolatedRawSql.IsMatch("context.Set<T>().FromSqlRaw(\"EXEC p @pName = {0}\", parameters)"));
    }

    [TestMethod]
    public void Rust_and_Python_search_escape_like_wildcards_and_choose_a_sort_from_a_map()
    {
        string rust = File.ReadAllText(Repo.Template("RS_Repo_v1.tt"));
        string python = File.ReadAllText(Repo.Template("PY_Routes_v1.tt"));

        Expect.Contains(rust, "escape_like(value)");
        Expect.Contains(rust, "sort_expression");
        Expect.Contains(python, "SORTS.get(");
        Expect.Contains(File.ReadAllText(Repo.Template("PY_EssentialApp_v1.tt")), "escape_like(text)");
    }
}
