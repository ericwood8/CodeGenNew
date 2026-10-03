using System.Data;

namespace CodeGenNew.Core;

/// <summary> The C# <c>CloneAsync</c> method of a repository: calls <c>&lt;Table&gt;_Clone</c> (SP_Clone) the way each database wants and returns the new row's id.
/// SQL Server hands the key back in an OUTPUT parameter, PostgreSQL as the function's value, MySQL as a one-row result set whose column is called Value.
/// A column in a unique index (an account number) cannot keep the source's value, so each one gets a free value from the repository's own
/// <c>SuggestUnique&lt;Column&gt;</c> ("A100" gives "A1002"), cut short first if the suffix would not fit the column. </summary>
public static class CloneCall
{
    /// <summary> The lines of the method, indented for a class body. The table must satisfy <see cref="CloneShape.CanClone"/>. </summary>
    public static List<string> RepoMethod(TableModel m, string entity)
    {
        var key = m.PrimaryKeyColumns[0];
        var overrides = CloneShape.OverrideColumns(m);
        var createUsers = CloneShape.CreateUserColumns(m);
        var o = new List<string>
        {
            "    // Copies a row into a new one (<Table>_Clone) and returns the new row's id."
                + (overrides.Count > 0 ? " A unique text column gets a free value (SuggestUnique<Column>) so the copy can be saved;" : ""),
            "    // createUser is who is cloning it, for a table that records its creator.",
            "    public async Task<int> CloneAsync(int id, string? createUser = null)",
            "    {",
            $"        var source = await GetByIdAsync(id) ?? throw new KeyNotFoundException($\"No {m.TableName} found to clone (id = {{id}}).\");"
        };

        foreach (var c in overrides)
        {
            int chars = Characters(c);
            string value = $"source.{c.Name}";
            string fitted = chars > 3 ? $"{value}.Length > {chars - 3} ? {value}[..{chars - 3}] : {value}" : value;
            o.Add($"        string? unique{c.Name} = {value} is null ? null : await SuggestUnique{c.Name}({fitted});");
        }

        switch (m.Dialect)
        {
            case SqlDialect.PostgreSql:
                AddPostgres(o, m, key, createUsers, overrides);
                break;
            case SqlDialect.MySql:
                AddMySql(o, m, key, createUsers, overrides);
                break;
            default:
                AddSqlServer(o, m, key, createUsers, overrides);
                break;
        }
        o.Add("    }");
        return o;
    }

    // The column's length in characters (a Unicode column reports bytes); 0 when it has no limit.
    private static int Characters(ColumnModel c) =>
        c.MaxLength is > 0 ? (c.SqlType is SqlDbType.NChar or SqlDbType.NVarChar ? c.MaxLength.Value / 2 : c.MaxLength.Value) : 0;

    private static string CreateUserValue(ColumnModel c) => c.IsStringColumn ? "createUser ?? \"\"" : "DBNull.Value";

    private static void AddSqlServer(List<string> o, TableModel m, ColumnModel key, List<ColumnModel> createUsers, List<ColumnModel> overrides)
    {
        const string P = "Microsoft.Data.SqlClient.SqlParameter";
        string copyFrom = "@CopyFrom" + key.Name, newKey = "@New" + key.Name;
        o.Add($"        var newKey = new {P}(\"{newKey}\", System.Data.SqlDbType.Int) {{ Direction = System.Data.ParameterDirection.Output }};");

        // Named arguments, so the optional parameters can be left out and the order does not matter.
        var named = new List<string> { $"{copyFrom} = {copyFrom}", $"{newKey} = {newKey} OUTPUT" };
        var args = new List<string> { $"new {P}(\"{copyFrom}\", id)", "newKey" };
        foreach (var c in createUsers.Where(c => c.IsStringColumn))
        {
            named.Add($"{c.ParameterName} = {c.ParameterName}");
            args.Add($"new {P}(\"{c.ParameterName}\", {CreateUserValue(c)})");
        }
        foreach (var c in overrides)
        {
            named.Add($"{c.ParameterName} = {c.ParameterName}");
            args.Add($"new {P}(\"{c.ParameterName}\", (object?)unique{c.Name} ?? DBNull.Value)");
        }
        o.Add("        await _context.Database.ExecuteSqlRawAsync(");
        o.Add($"            \"EXEC [{m.SchemaName}].[{m.TableName}_Clone] {string.Join(", ", named)}\",");
        for (int i = 0; i < args.Count; i++)
            o.Add("            " + args[i] + (i < args.Count - 1 ? "," : ");"));
        o.Add("        return (int)newKey.Value!;");
    }

    private static void AddPostgres(List<string> o, TableModel m, ColumnModel key, List<ColumnModel> createUsers, List<ColumnModel> overrides)
    {
        const string P = "Npgsql.NpgsqlParameter";
        static string Q(string name) => "\\\"" + name + "\\\"";            // a quoted identifier inside a C# string literal
        string copyFrom = "CopyFrom" + key.Name;
        var named = new List<string> { $"{Q(copyFrom)} => @{copyFrom}" };
        var args = new List<string> { $"new {P}(\"@{copyFrom}\", id)" };
        foreach (var c in createUsers.Where(c => c.IsStringColumn))
        {
            string name = c.ParameterName.TrimStart('@');
            named.Add($"{Q(name)} => @{name}");
            args.Add($"new {P}(\"@{name}\", NpgsqlTypes.NpgsqlDbType.Text) {{ Value = {CreateUserValue(c)} }}");
        }
        foreach (var c in overrides)
        {
            string name = c.ParameterName.TrimStart('@');
            named.Add($"{Q(name)} => @{name}");
            args.Add($"new {P}(\"@{name}\", NpgsqlTypes.NpgsqlDbType.Text) {{ Value = (object?)unique{c.Name} ?? DBNull.Value }}");
        }
        o.Add("        // The function returns the new key; SqlQueryRaw reads a scalar from a column called Value. A CALL / function call is not composable, so ToListAsync, then Single.");
        o.Add("        var rows = await _context.Database.SqlQueryRaw<int>(");
        o.Add($"            \"SELECT {Q(m.SchemaName)}.{Q(m.TableName + "_Clone")}({string.Join(", ", named)}) AS {Q("Value")}\",");
        for (int i = 0; i < args.Count; i++)
            o.Add("            " + args[i] + (i < args.Count - 1 ? "," : ")"));
        o.Add("            .ToListAsync();");
        o.Add("        return rows.Single();");
    }

    private static void AddMySql(List<string> o, TableModel m, ColumnModel key, List<ColumnModel> createUsers, List<ColumnModel> overrides)
    {
        const string P = "MySql.Data.MySqlClient.MySqlParameter";
        // A MySQL procedure has no default parameter: every argument is passed, in the order SP_Clone declares them.
        var names = new List<string> { "@CopyFrom" + key.Name };
        var args = new List<string> { $"new {P}(\"@CopyFrom{key.Name}\", id)" };
        int n = 0;
        foreach (var c in createUsers)
        {
            string name = "@CreateUser" + n++;
            names.Add(name);
            args.Add($"new {P}(\"{name}\", {CreateUserValue(c)})");
        }
        foreach (var c in overrides)
        {
            string name = "@Unique" + c.Name;
            names.Add(name);
            args.Add($"new {P}(\"{name}\", (object?)unique{c.Name} ?? DBNull.Value)");
        }
        o.Add("        // The procedure returns the new key as a one-row result set whose column is called Value. A CALL is not composable, so ToListAsync, then Single.");
        o.Add("        var rows = await _context.Database.SqlQueryRaw<int>(");
        o.Add($"            \"CALL `{m.TableName}_Clone`({string.Join(", ", names)})\",");
        for (int i = 0; i < args.Count; i++)
            o.Add("            " + args[i] + (i < args.Count - 1 ? "," : ")"));
        o.Add("            .ToListAsync();");
        o.Add("        return rows.Single();");
    }
}
