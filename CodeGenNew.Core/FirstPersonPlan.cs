namespace CodeGenNew.Core;

/// <summary> What a value of the first person's row is, so each language writes it its own way. </summary>
public enum FirstPersonKind
{
    /// <summary> The sign-in name the person typed, cut to the column's length. </summary>
    Name,
    /// <summary> The hash of the password the person typed. </summary>
    Hash,
    /// <summary> A flag set to true. </summary>
    True,
    /// <summary> A flag set to false. </summary>
    False,
    /// <summary> The administrator role as text. </summary>
    AdminRole,
    /// <summary> A number, 0. </summary>
    Zero,
    /// <summary> A date and time, now. </summary>
    Now
}

/// <summary> The row the sign-in creates when the user table is empty: the first person, who is the administrator. The sign-in name and the password hash come from what was typed, the administrator flag
/// (and the manager flag or the administrator role in the role column) is set, and every other column the database makes the row hold is given a value that fits: a text column the user name, a flag false,
/// a number 0, a date now. A column that cannot be filled that way (a required foreign key, binary data) means the table has no first person offer: <see cref="For"/> answers the sentence. </summary>
public sealed record FirstPersonPlan(IReadOnlyList<FirstPersonValue> Values)
{
    /// <summary> The plan for <paramref name="user"/>, or the reason there is none. </summary>
    public static (FirstPersonPlan? Plan, string? Problem) For(UserTablePlan user, ProjectSettings project)
    {
        var table = user.Table;
        var values = new List<FirstPersonValue>
        {
            Value(user.UserName, FirstPersonKind.Name),
            Value(user.PasswordHash, FirstPersonKind.Hash)
        };
        if (user.Admin is { } admin)
            values.Add(Value(admin, FirstPersonKind.True));
        if (user.Role is { } role)
            values.Add(Value(role, role.IsBooleanColumn ? FirstPersonKind.True : FirstPersonKind.AdminRole, project.AuthAdminRole));
        if (user.Owner is { IsNullable: false } owner && !owner.IsPrimaryKey)
            return (null, $"{table.TableName}.{owner.Name} is required and names the owner, which the first person does not have.");

        var given = values.Select(v => v.Column.Name).ToHashSet(StringComparer.OrdinalIgnoreCase);
        foreach (var c in table.Columns.Where(c => !c.IsNullable && !c.IsPrimaryKey && !c.IsIdentity && !c.IsComputed && c.DatabaseDefaultSql is null && !given.Contains(c.Name)))
        {
            if (table.ForeignKeyByColumn.ContainsKey(c.Name))
                return (null, $"{table.TableName}.{c.Name} is a required foreign key; the first person cannot be given a value for it.");
            if (c.IsStringColumn)
                values.Add(Value(c, FirstPersonKind.Name));
            else if (c.IsBooleanColumn)
                values.Add(Value(c, FirstPersonKind.False));
            else if (c.IsNumericColumn || c.IsIntegerColumn)
                values.Add(Value(c, FirstPersonKind.Zero));
            else if (c.CSharpBase() is "DateTime" or "DateTimeOffset")
                values.Add(Value(c, FirstPersonKind.Now));
            else
                return (null, $"{table.TableName}.{c.Name} is required; the first person cannot be given a value for a {c.CSharpBase()} column.");
        }
        return (new FirstPersonPlan(values), null);
    }

    private static FirstPersonValue Value(ColumnModel column, FirstPersonKind kind, string? text = null)
    {
        int? length = kind == FirstPersonKind.Name && column.CharacterLength is > 0 and var l ? l : null;
        string expression = kind switch
        {
            FirstPersonKind.Name => length is { } n ? $"name.Length > {n} ? name[..{n}] : name" : "name",
            FirstPersonKind.Hash => "Hash(password)",
            FirstPersonKind.True => "true",
            FirstPersonKind.False => "false",
            FirstPersonKind.AdminRole => CSharpText.Quote(text!),
            FirstPersonKind.Zero => "default",
            _ => column.CSharpBase() == "DateTimeOffset" ? "DateTimeOffset.Now" : "DateTime.Now"
        };
        return new FirstPersonValue(column.CSharpName(), expression, column, kind, length, text);
    }

    /// <summary> The members of the new row's object initializer. </summary>
    public string Initializer(string indent) => string.Join(",\n", Values.Select(v => $"{indent}{v.Property} = {v.Expression}"));
}

/// <summary> One property of the first person's row: its C# name and expression, and what kind of value it is for the other languages (<paramref name="Length"/> cuts a name to the column). </summary>
public sealed record FirstPersonValue(string Property, string Expression, ColumnModel Column, FirstPersonKind Kind, int? Length, string? Text);
