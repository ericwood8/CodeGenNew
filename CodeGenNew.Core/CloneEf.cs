namespace CodeGenNew.Core;

/// <summary> The C# <c>CloneAsync</c> method of a repository when the project reaches the database through EF Core (<see cref="AccessMode.Ef"/>): the same copy SP_Clone writes as one INSERT ... SELECT, done in code.
/// The rules are the stored procedure's: the key is new, the create date is now, the modify and inactivation columns stay empty, the active flag is "active", a delete flag is "not deleted", the create user
/// is whoever clones, and a unique text column gets a free value from the repository's own <c>SuggestUnique&lt;Column&gt;</c>. Everything else is copied. </summary>
public static class CloneEf
{
    /// <summary> The lines of the method, indented for a class body. The table must satisfy <see cref="CloneShape.CanClone"/>. </summary>
    public static List<string> RepoMethod(TableModel m, string entity)
    {
        var key = m.PrimaryKeyColumns[0];
        var overrides = CloneShape.OverrideColumns(m);
        var active = m.HasActiveInactivePair ? m.ActiveColumn : null;
        var inactiveDate = m.HasActiveInactivePair ? m.InactiveDateColumn : null;
        bool activeNegative = active is not null && active.Name.Contains("Inactive", StringComparison.OrdinalIgnoreCase);

        var o = new List<string>
        {
            "    // Copies a row into a new one and returns the new row's id."
                + (overrides.Count > 0 ? " A unique text column gets a free value (SuggestUnique<Column>) so the copy can be saved;" : ""),
            "    // createUser is who is cloning it, for a table that records its creator.",
            "    public async Task<int> CloneAsync(int id, string? createUser = null)",
            "    {",
            $"        var source = await GetByIdAsync(id) ?? throw new KeyNotFoundException($\"No {m.TableName} found to clone (id = {{id}}).\");"
        };

        foreach (var c in overrides)
        {
            int chars = c.CharacterLength();
            string value = $"source.{c.CSharpName()}";
            string fitted = chars > 3 ? $"{value}.Length > {chars - 3} ? {value}[..{chars - 3}] : {value}" : value;
            o.Add($"        string? unique{c.Name} = {value} is null ? null : await SuggestUnique{c.Name}({fitted});");
        }

        o.Add($"        var copy = new {entity}");
        o.Add("        {");
        var assignments = new List<string>();
        foreach (var c in CloneShape.Copyable(m))
        {
            string name = c.CSharpName();
            string? value = Value(c);
            if (value is not null)
                assignments.Add($"            {name} = {value}");
        }
        for (int i = 0; i < assignments.Count; i++)
            o.Add(assignments[i] + (i < assignments.Count - 1 ? "," : ""));
        o.Add("        };");
        o.Add("        _dbSet.Add(copy);");
        o.Add("        await _context.SaveChangesAsync();");
        o.Add($"        return copy.{key.CSharpName()};");
        o.Add("    }");
        return o;

        string? Value(ColumnModel c)
        {
            if (c.IsPrimaryKey) return null;   // the database assigns it
            if (c.IsCreateDateColumn || c.IsLastChangedDateColumn)
                return c.CSharpBase() switch { "DateTime" => "DateTime.Now", "DateTimeOffset" => "DateTimeOffset.Now", _ => null };
            if (c.IsModifiedDateColumn || c.IsModifiedUserColumn || c.IsInactiveReasonColumn || c == inactiveDate || (m.HasSoftDelete && c == m.DeletedDateColumn))
                return null;   // a new row has nothing to put here
            if (c == active) return Flag(c, !activeNegative, "\"1\"", "\"0\"");
            if (c.IsAdminFlagColumn || (m.HasSoftDelete && c == m.IsDeletedColumn)) return Flag(c, false, "\"False\"", "\"False\"");
            if (c.IsCreateUserColumn) return c.IsStringColumn ? "createUser ?? \"\"" : null;
            if (overrides.Contains(c)) return $"unique{c.Name} ?? source.{c.CSharpName()}";
            return $"source.{c.CSharpName()}";
        }

        static string Flag(ColumnModel c, bool value, string trueText, string falseText) =>
            c.IsStringColumn ? (value ? trueText : falseText) : (value ? "true" : "false");
    }
}
