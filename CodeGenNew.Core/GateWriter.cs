using System.Text;

namespace CodeGenNew.Core;

/// <summary> What differs between the two places a gate or guard runs: the ASP.NET API answers with problem results and knows the caller by a token, the desktop app answers with a sentence and knows the caller
/// by the name it was started with. Everything else is written once by <see cref="GateWriter"/>. </summary>
public abstract class GateFlavor
{
    /// <summary> The generator named in the class comments. </summary>
    public abstract string Generator { get; }
    /// <summary> The return type of a check: null when the row may be saved. </summary>
    public abstract string ResultType { get; }
    /// <summary> The caller as a trailing parameter of the public methods (with its leading comma), or empty. </summary>
    public virtual string CallerParameter => "";
    /// <summary> The same as an argument of a call. </summary>
    public virtual string CallerArgument => "";
    /// <summary> The expression that answers a check when the row to change is gone. </summary>
    public abstract string NotFound { get; }
    public abstract string StartRefused(ChangeGate gate);
    /// <summary> The answer for a held change; <paramref name="text"/> is the C# expression of its sentence. </summary>
    public abstract string Conflict(ChangeGate gate, string text);
    public abstract string Forbidden(ColumnGuard guard);
    /// <summary> An expression of type <c>string?</c>: the name of the person who changes the row. </summary>
    public abstract string CallerName { get; }
    /// <summary> The body of the method that says whether the caller may change a guarded column (eight spaces in, whole lines). </summary>
    public abstract IEnumerable<string> MayBody(ColumnGuard guard, ProjectSettings project, string contextType);
}

/// <summary> The ASP.NET API: a refusal is a problem result and the caller is the token's principal. </summary>
public sealed class ApiGateFlavor : GateFlavor
{
    public override string Generator => "API_Gates";
    public override string ResultType => "IResult?";
    public override string CallerParameter => ", ClaimsPrincipal caller";
    public override string CallerArgument => ", caller";
    public override string NotFound => "Results.NotFound()";
    public override string CallerName => "caller.FindFirstValue(AuthNames.NameClaim)";

    public override string StartRefused(ChangeGate gate) =>
        $"Results.ValidationProblem(new Dictionary<string, string[]> {{ [{CSharpText.Quote(JsonNames.Camel(gate.Column.Name))}] = [{CSharpText.Quote(gate.StartRefusal!)}] }})";

    public override string Conflict(ChangeGate gate, string text) =>
        $"Results.Problem(statusCode: {HttpStatus.Conflict}, type: {CSharpText.Quote(ProblemTypes.ChangeNotAllowed)}, title: \"Change not allowed\", detail: {text})";

    public override string Forbidden(ColumnGuard guard) =>
        $"Results.Problem(statusCode: {HttpStatus.Forbidden}, type: {CSharpText.Quote(ProblemTypes.ColumnNotYours)}, title: \"Not allowed\", detail: {CSharpText.Quote(guard.RefusalText)})";

    public override IEnumerable<string> MayBody(ColumnGuard guard, ProjectSettings project, string contextType)
    {
        string name = guard.UserNameColumn(project).CSharpName();
        return
        [
            "        if (caller.IsInRole(AuthNames.AdminRole))",
            "            return true;",
            "        string? name = caller.FindFirstValue(AuthNames.NameClaim);",
            "        if (string.IsNullOrEmpty(name))",
            "            return false;",
            $"        return await context.Set<{guard.UserTable.TableName}>().AsNoTracking().Where(u => u.{name} == name).Select(u => u.{guard.By.CSharpName()} == true).FirstOrDefaultAsync();"
        ];
    }
}

/// <summary> The WinUI3 app: it reads the database directly, so a refusal is a sentence for the dialog and the caller is <c>DesktopCaller.Name</c> (the Windows sign-in unless the project sets its own). The person is looked
/// up in the user table by name; the user table's administrator column, when there is one, may change any guarded column. </summary>
public sealed class DesktopGateFlavor : GateFlavor
{
    public const string NotFoundText = "This row no longer exists.";

    public override string Generator => "WinUI3_Gates";
    public override string ResultType => "string?";
    public override string NotFound => CSharpText.Quote(NotFoundText);
    public override string CallerName => "DesktopCaller.Name";
    public override string StartRefused(ChangeGate gate) => CSharpText.Quote(gate.StartRefusal!);
    public override string Conflict(ChangeGate gate, string text) => text;
    public override string Forbidden(ColumnGuard guard) => CSharpText.Quote(guard.RefusalText);

    public override IEnumerable<string> MayBody(ColumnGuard guard, ProjectSettings project, string contextType)
    {
        var plan = UserTable.ResolveTable(guard.UserTable, project).Plan!;
        string name = plan.UserName.CSharpName();
        string allowed = plan.Admin is { } admin ? $"u.{admin.CSharpName()} == true || u.{guard.By.CSharpName()} == true" : $"u.{guard.By.CSharpName()} == true";
        return
        [
            "        string? name = DesktopCaller.Name;",
            "        if (string.IsNullOrEmpty(name))",
            "            return false;",
            $"        return await context.Set<{guard.UserTable.TableName}>().AsNoTracking().Where(u => u.{name} == name).Select(u => {allowed}).FirstOrDefaultAsync();"
        ];
    }
}

/// <summary> The C# of a table's change gates and column guards (<c>&lt;Table&gt;Gates</c>), shared by the API and the WinUI3 app so the two refuse the same changes. </summary>
public static class GateWriter
{
    /// <summary> One static class per gated table, whole lines ending in a line feed. </summary>
    public static string Tables(GatePlan plan, DatabaseModel database, string contextType, ProjectSettings project, GateFlavor flavor)
    {
        var sb = new StringBuilder();
        foreach (var gated in plan.Tables)
            Table(sb, gated, database, contextType, project, flavor);
        return sb.ToString();
    }

    private static string NameKey(ForeignKeyModel fk) => fk.ReferencedTable;

    /// <summary> The referenced table's entity, key, display column and key type; null when the table has no column worth showing (then the id stays in the sentence). </summary>
    private static (string Entity, string Key, string Display, string KeyType)? LookupOf(ForeignKeyModel fk, DatabaseModel database)
    {
        var table = database.Tables.FirstOrDefault(t => t.TableName.EqualsIgnoreCase(fk.ReferencedTable));
        var key = table?.Columns.FirstOrDefault(c => c.Name.EqualsIgnoreCase(fk.ReferencedColumns[0]));
        if (table is null || key is null)
            return null;
        var display = table.Columns.ToList().SelectDisplayColumns(table.ForeignKeys.SelectMany(f => f.ReferencingColumns).ToList()).FirstOrDefault();
        return display is null ? null : (table.TableName, key.CSharpName(), display.CSharpName(), table.PrimaryKeyType?.CSharp ?? "int");
    }

    /// <summary> The C# expression of a held change's sentence: the project's own message as it is, else the default sentence with each id of a lookup (a status) said as its name, read when the change is refused. </summary>
    private static string RefusalExpression(ChangeGate gate, DatabaseModel database)
    {
        if (gate.Message is not null || !gate.NamedTerms.Any(t => LookupOf(t.NamedBy!, database) is not null))
            return CSharpText.Quote(gate.RefusalText);
        const char open = '', close = '';
        string text = gate.RefusalWith((term, value) =>
        {
            if (term.NamedBy is not { } fk || LookupOf(fk, database) is not { } lookup)
                return value;
            return $"{open}await {lookup.Entity}NameAsync(context, {value}){close}";
        });
        var o = new StringBuilder("$\"");
        foreach (char c in text)
            o.Append(c switch { open => "{", close => "}", '"' => "\\\"", '\\' => "\\\\", '{' => "{{", '}' => "}}", _ => c.ToString() });
        return o.Append('"').ToString();
    }

    private static void Table(StringBuilder sb, GatedTable gated, DatabaseModel database, string contextType, ProjectSettings project, GateFlavor flavor)
    {
        void Line(string text = "") => sb.Append(text).Append('\n');

        var table = gated.Table;
        string entity = table.TableName;
        var key = table.PrimaryKeyColumns[0];
        var gates = gated.Gates;
        var guards = gated.Guards;
        string result = flavor.ResultType;

        Line($"/// <summary> The change gates and column guards of {entity} (the project's ChangeGates and GuardedColumns), checked before a row is saved. Generated by {flavor.Generator}. </summary>");
        Line($"public static class {entity}Gates");
        Line("{");
        Line($"    public static async Task<{result}> CheckAsync({contextType} context{flavor.CallerParameter}, {entity} row, bool isNew)");
        Line("    {");
        int block = 0;
        foreach (var check in gated.Checks)
        {
            if (check.Gate is { } checkedGate)
            {
                Line($"        if (await Gate{checkedGate.Column.CSharpName()}Async(context, row, isNew) is {{ }} refused{checkedGate.Column.CSharpName()})");
                Line($"            return refused{checkedGate.Column.CSharpName()};");
                continue;
            }
            var guard = check.Guard!;
            string column = guard.Column.CSharpName();
            var members = new List<string> { column };
            if (guard.StampBy is { } by) members.Add(by.CSharpName());
            if (guard.StampOn is { } on) members.Add(on.CSharpName());
            string stored = "stored" + (++block);
            string defaultTest = GateCode.CSharpNotDefault(guard.Column, "row." + column);
            Line($"        // guard {entity}.{guard.Column.Name}: {guard.Description}");
            Line("        {");
            Line($"            var {stored} = isNew ? null : await context.Set<{entity}>().AsNoTracking().Where(r => r.{key.CSharpName()} == row.{key.CSharpName()})");
            Line($"                .Select(r => new {{ {string.Join(", ", members.Select(m => "r." + m))} }}).FirstOrDefaultAsync();");
            Line($"            if (!isNew && {stored} is null)");
            Line($"                return {flavor.NotFound};");
            Line($"            bool changed = isNew ? {defaultTest} : !Equals(row.{column}, {stored}!.{column});");
            Line($"            if (changed && !await May{column}Async(context{flavor.CallerArgument}))");
            Line($"                return {flavor.Forbidden(guard)};");
            if (guard.StampBy is not null || guard.StampOn is not null)
            {
                Line("            if (changed)");
                Line("            {");
                if (guard.StampBy is { } stampBy)
                    Line($"                row.{stampBy.CSharpName()} = {flavor.CallerName}{(stampBy.IsNullable ? "" : " ?? \"\"")};");
                if (guard.StampOn is { } stampOn)
                    Line($"                row.{stampOn.CSharpName()} = DateTime.Now;");
                Line("            }");
                Line("            else");
                Line("            {");
                if (guard.StampBy is { } keepBy)
                    Line($"                row.{keepBy.CSharpName()} = isNew ? default! : {stored}!.{keepBy.CSharpName()};");
                if (guard.StampOn is { } keepOn)
                    Line($"                row.{keepOn.CSharpName()} = isNew ? default! : {stored}!.{keepOn.CSharpName()};");
                Line("            }");
            }
            Line("        }");
            Line();
        }
        Line();
        Line("        return null;");
        Line("    }");

        foreach (var gate in gates)
        {
            string column = gate.Column.CSharpName();
            string rowColumn = "row." + column;
            var parents = gate.ParentReads;
            string TermTest(GateTerm term) => term.Via is null
                ? GateCode.Test("row." + term.Column.CSharpName(), term)
                : $"(parent_{term.Via.ReferencingColumns[0]} is not null && {GateCode.Test($"parent_{term.Via.ReferencingColumns[0]}.{term.Column.CSharpName()}", term)})";
            string to = GateCode.Test(rowColumn, gate.To);
            string when = GateCode.Predicate(gate.When, TermTest);
            string unless = gate.Unless is null ? "" : GateCode.Predicate(gate.Unless, TermTest);

            Line();
            Line($"    // gate {entity}.{gate.Column.Name}: {gate.Description}");
            Line($"    private static async Task<{result}> Gate{column}Async({contextType} context, {entity} row, bool isNew)");
            Line("    {");
            Line("        bool moves;");
            Line("        if (isNew)");
            Line("        {");
            if (gate.Start is { } start)
            {
                Line($"            if ({rowColumn} != {GateCode.Literal(gate.Column, start)})");
                Line($"                return {flavor.StartRefused(gate)};");
                Line("            moves = false;");
            }
            else
                Line("            moves = true;");
            Line("        }");
            Line("        else");
            Line("        {");
            Line($"            var before = await context.Set<{entity}>().AsNoTracking().Where(r => r.{key.CSharpName()} == row.{key.CSharpName()}).Select(r => new {{ r.{column} }}).FirstOrDefaultAsync();");
            Line($"            moves = before is not null && !Equals(before.{column}, {rowColumn});");
            Line("        }");
            Line($"        if (moves && {to})");
            Line("        {");
            foreach (var parent in parents)
            {
                var via = parent.Via;
                var holder = parent.Holder;
                var members = parent.Columns.Select(c => c.CSharpName()).Distinct().Select(m => "p." + m);
                string fk = via.ReferencingColumns[0];
                var fkColumn = table.Columns.First(c => c.Name.EqualsIgnoreCase(fk));
                string referenced = holder.Columns.First(c => c.Name.EqualsIgnoreCase(via.ReferencedColumns[0])).CSharpName();
                Line($"            var parent_{fk} = await context.Set<{holder.TableName}>().AsNoTracking().Where(p => p.{referenced} == row.{fkColumn.CSharpName()})");
                Line($"                .Select(p => new {{ {string.Join(", ", members)} }}).FirstOrDefaultAsync();");
            }
            if (gate.RequiresApproval)
                Line($"            bool approved = !isNew && await RowApprovedAsync(context, row.{key.CSharpName()});");
            Line($"            if (({when}){(unless.Length == 0 ? "" : $" && !({unless})")}{(gate.RequiresApproval ? " && !approved" : "")})");
            Line($"                return {flavor.Conflict(gate, RefusalExpression(gate, database))};");
            Line("        }");
            Line("        return null;");
            Line("    }");
        }

        if (gates.Any(g => g.RequiresApproval))
        {
            var approval = ApprovalPlan.Resolve(database, project).Tables.First(a => a.Table == table);
            Line();
            Line($"    /// <summary> True when the latest request for the row was approved (RowApprovals). </summary>");
            Line($"    private static async Task<bool> RowApprovedAsync({contextType} context, {table.PrimaryKeyType?.CSharp ?? "int"} id) =>");
            Line($"        await context.Set<{approval.Side.TableName}>().AsNoTracking().Where(a => a.{approval.Link.CSharpName()} == id).OrderByDescending(a => a.{approval.Revision.CSharpName()})");
            Line($"            .Select(a => a.{approval.Status.CSharpName()}).FirstOrDefaultAsync() == {ApprovalStatus.Approved};");
        }

        foreach (var fk in gates.Where(g => g.Message is null).SelectMany(g => g.NamedTerms).Select(t => t.NamedBy!).Where(fk => LookupOf(fk, database) is not null).DistinctBy(NameKey))
        {
            var (entityName, keyName, displayName, keyType) = LookupOf(fk, database)!.Value;
            Line();
            Line($"    /// <summary> The name of a {entityName} row for a message, or its id when the row is gone. </summary>");
            Line($"    private static async Task<string> {entityName}NameAsync({contextType} context, {keyType} id) =>");
            Line($"        (await context.Set<{entityName}>().AsNoTracking().Where(r => r.{keyName} == id).Select(r => r.{displayName}).FirstOrDefaultAsync())?.ToString() ?? id.ToString();");
        }

        // a clone is a new row made without the request's body: it starts the way a new row does and the guarded columns do not carry the source's value over
        string cloneKey = table.PrimaryKeyType?.CSharp ?? "int";
        Line();
        Line($"    /// <summary> Runs after {entity} was cloned: the copy starts as a new row does (the gated columns at their start, the guarded columns reset unless the caller may change them) and a gate that holds a new row removes the copy. </summary>");
        Line($"    public static async Task<{result}> AfterCloneAsync({contextType} context{flavor.CallerParameter}, {cloneKey} newId)");
        Line("    {");
        Line($"        var copy = await context.Set<{entity}>().FirstOrDefaultAsync(r => r.{key.CSharpName()} == newId);");
        Line("        if (copy is null)");
        Line("            return null;");
        foreach (var gate in gates.Where(g => g.Start is not null))
            Line($"        copy.{gate.Column.CSharpName()} = {GateCode.Literal(gate.Column, gate.Start!)};");
        foreach (var guard in guards)
        {
            string column = guard.Column.CSharpName();
            string notDefault = GateCode.CSharpNotDefault(guard.Column, "copy." + column);
            Line($"        if ({notDefault})");
            Line("        {");
            Line($"            if (await May{column}Async(context{flavor.CallerArgument}))");
            Line("            {");
            if (guard.StampBy is { } stampBy)
                Line($"                copy.{stampBy.CSharpName()} = {flavor.CallerName}{(stampBy.IsNullable ? "" : " ?? \"\"")};");
            if (guard.StampOn is { } stampOn)
                Line($"                copy.{stampOn.CSharpName()} = DateTime.Now;");
            Line("            }");
            Line("            else");
            Line("            {");
            Line($"                copy.{column} = default!;");
            if (guard.StampBy is { } clearBy)
                Line($"                copy.{clearBy.CSharpName()} = default!;");
            if (guard.StampOn is { } clearOn)
                Line($"                copy.{clearOn.CSharpName()} = default!;");
            Line("            }");
            Line("        }");
            if (guard.StampBy is not null || guard.StampOn is not null)
            {
                Line("        else");
                Line("        {");
                if (guard.StampBy is { } emptyBy)
                    Line($"            copy.{emptyBy.CSharpName()} = default!;");
                if (guard.StampOn is { } emptyOn)
                    Line($"            copy.{emptyOn.CSharpName()} = default!;");
                Line("        }");
            }
        }
        Line("        await context.SaveChangesAsync();");
        foreach (var gate in gates)
        {
            Line($"        if (await Gate{gate.Column.CSharpName()}Async(context, copy, true) is {{ }} refused{gate.Column.CSharpName()})");
            Line("        {");
            Line("            context.Remove(copy);");
            Line("            await context.SaveChangesAsync();");
            Line($"            return refused{gate.Column.CSharpName()};");
            Line("        }");
        }
        Line("        return null;");
        Line("    }");
        foreach (var guard in guards)
        {
            string column = guard.Column.CSharpName();
            Line();
            Line($"    /// <summary> True when the caller is an administrator or has {guard.UserTable.TableName}.{guard.By.Name} set. </summary>");
            Line($"    public static async Task<bool> May{column}Async({contextType} context{flavor.CallerParameter})");
            Line("    {");
            foreach (string body in flavor.MayBody(guard, project, contextType))
                Line(body);
            Line("    }");
        }
        Line("}");
        Line();
    }
}
