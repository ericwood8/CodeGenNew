using System.Globalization;
using System.Text;

namespace CodeGenNew.Core;

/// <summary> How a gate or guard compares: the operators of the small rule language. </summary>
public enum GateOperator { Equal, NotEqual, Less, LessOrEqual, Greater, GreaterOrEqual, In }

/// <summary> One comparison of a rule: a column of the gated table (<see cref="Via"/> null) or of a parent reached through one foreign key, and the value or values it is compared with.
/// The values are normalised text: <c>true</c> / <c>false</c>, a number in invariant form, or the characters of a text. </summary>
public sealed record GateTerm(TableModel Table, ForeignKeyModel? Via, ColumnModel Column, GateOperator Operator, IReadOnlyList<string> Values)
{
    /// <summary> The values as a message says them: <c>2</c>, <c>2, 3, 4</c>, <c>anything but 1</c>, <c>a value &gt; 1</c>. </summary>
    public string ValueWords() => Operator switch
    {
        GateOperator.Equal or GateOperator.In => string.Join(", ", Values),
        GateOperator.NotEqual => "anything but " + Values[0],
        GateOperator.Less => "a value < " + Values[0],
        GateOperator.LessOrEqual => "a value <= " + Values[0],
        GateOperator.Greater => "a value > " + Values[0],
        _ => "a value >= " + Values[0]
    };

    /// <summary> The words of the comparison for a message: <c>Customer Status Id = 2</c>. </summary>
    public string Words()
    {
        string column = GateCode.Words(Column);
        string parent = ScreenNames.Words(ScreenNames.BaseName(Table.TableName));
        string name = Via is null || column.StartsWith(parent, StringComparison.OrdinalIgnoreCase) ? column : parent + " " + column;
        string symbol = Operator switch { GateOperator.Equal => "=", GateOperator.NotEqual => "<>", GateOperator.Less => "<", GateOperator.LessOrEqual => "<=", GateOperator.Greater => ">", GateOperator.GreaterOrEqual => ">=", _ => "in" };
        return $"{name} {symbol} {string.Join(", ", Values)}";
    }
}

/// <summary> Terms joined by <c>and</c> and <c>or</c> (<c>and</c> binds tighter): a list of alternatives, each a list of terms that must all hold. </summary>
public sealed record GatePredicate(IReadOnlyList<IReadOnlyList<GateTerm>> AnyOf)
{
    public IEnumerable<GateTerm> Terms => AnyOf.SelectMany(all => all);
    public string Words() => string.Join(" or ", AnyOf.Select(all => string.Join(" and ", all.Select(t => t.Words()))));
}

/// <summary> A named column may not change to certain values while the <see cref="When"/> condition holds, unless <see cref="Unless"/> holds. A new row always starts as <see cref="Start"/> when that is given. </summary>
public sealed record ChangeGate(TableModel Table, ColumnModel Column, GateTerm To, GatePredicate When, GatePredicate? Unless, string? Start, string? Message, string Text);

/// <summary> Only a person whose row in the user table has the yes/no column <see cref="By"/> set (or an administrator) may change the column. The optional stamp columns say who did it and when. </summary>
public sealed record ColumnGuard(TableModel Table, ColumnModel Column, TableModel UserTable, ColumnModel By, ColumnModel? StampBy, ColumnModel? StampOn, string Text);

/// <summary> A guarded column as a screen sees it: the column, its JSON name (the key of the guards answer), the sentence shown instead of the control and the columns the server stamps (shown as text). </summary>
public sealed record GuardHint(string Column, string Json, string Note, IReadOnlyList<string> Stamps);

/// <summary> The gates and guards a project names (<c>ChangeGates</c>, <c>GuardedColumns</c>), checked against the schema. A rule that does not fit is a sentence in <see cref="Problems"/>, never a half-built rule. </summary>
public sealed record GatePlan(IReadOnlyList<ChangeGate> Gates, IReadOnlyList<ColumnGuard> Guards, IReadOnlyList<string> Problems)
{
    /// <summary> Splits a setting into its rules (separated by <c>|</c>) without reading the schema. </summary>
    public static IReadOnlyList<string> RulesOf(string? setting) => setting is null ? [] : SplitOutsideQuotes(setting, '|').Where(r => r.Length > 0).ToList();

    /// <summary> The table a rule names first (<c>SalesInvoice.InvoiceStatusId; to ...</c> is SalesInvoice), found without reading the schema; null for a rule with no table. </summary>
    public static string? TableOf(string rule)
    {
        string first = SplitOutsideQuotes(rule, ';')[0];
        int dot = first.IndexOf('.');
        return dot > 0 ? first[..dot].Trim() : null;
    }

    /// <summary> True when the project names a gate or a guard for <paramref name="table"/>. Answered from the settings alone, so a per-table template can ask it. </summary>
    public static bool HasRules(ProjectSettings project, TableModel table) => HasRules(project, table.TableName);

    /// <summary> The same, for a table known only by name. </summary>
    public static bool HasRules(ProjectSettings project, string table) =>
        project.ApiAuth && RulesOf(project.ChangeGates).Concat(RulesOf(project.GuardedColumns)).Any(rule => TableOf(rule) is { } name && name.EqualsIgnoreCase(table));

    /// <summary> The guarded columns of <paramref name="table"/> as a screen needs them, read from the settings alone: the column, the sentence shown in place of the control, and the columns the server stamps.
    /// A rule that does not name a column of the table is left out (API_Gates reports it). </summary>
    public static IReadOnlyList<GuardHint> GuardsOf(ProjectSettings project, TableModel table)
    {
        var hints = new List<GuardHint>();
        if (!project.ApiAuth)
            return hints;
        foreach (string rule in RulesOf(project.GuardedColumns))
        {
            var clauses = SplitOutsideQuotes(rule, ';');
            string[] first = clauses[0].Split('.', 2, StringSplitOptions.TrimEntries);
            if (first.Length != 2 || !first[0].EqualsIgnoreCase(table.TableName) || table.Columns.FirstOrDefault(c => c.Name.EqualsIgnoreCase(first[1])) is not { } column)
                continue;
            string by = "", stampBy = "", stampOn = "";
            foreach (string clause in clauses.Skip(1))
            {
                var (keyword, text) = Keyword(clause);
                if (keyword == "by") by = text.Contains('.') ? text[(text.IndexOf('.') + 1)..].Trim() : text;
                else if (keyword == "stampby") stampBy = text;
                else if (keyword == "stampon") stampOn = text;
            }
            var stamps = new[] { stampBy, stampOn }.Where(n => n.Length > 0 && table.Columns.Any(c => c.Name.EqualsIgnoreCase(n))).ToList();
            hints.Add(new GuardHint(column.Name, JsonNames.Camel(column.Name), GuardNote(by), stamps));
        }
        return hints;
    }

    /// <summary> The sentence a screen shows in place of a guarded control: <c>Only a manager can change this.</c> for <c>IsManager</c>, else <c>Only people with Can Approve can change this.</c>. </summary>
    public static string GuardNote(string byColumn)
    {
        string words = Labels.Words(byColumn);
        return words.StartsWith("Is ", StringComparison.OrdinalIgnoreCase) && words.Length > 3
            ? $"Only a {words[3..].ToLowerInvariant()} can change this."
            : $"Only people with {words} can change this.";
    }

    public bool ForTable(TableModel table) => Gates.Any(g => g.Table == table) || Guards.Any(g => g.Table == table);

    /// <summary> The rules that fit the schema, and a sentence for each one that does not. </summary>
    public static GatePlan Resolve(DatabaseModel database, ProjectSettings project)
    {
        var gates = new List<ChangeGate>();
        var guards = new List<ColumnGuard>();
        var problems = new List<string>();
        var rules = RulesOf(project.ChangeGates).Select(r => (Setting: "ChangeGates", Rule: r)).Concat(RulesOf(project.GuardedColumns).Select(r => (Setting: "GuardedColumns", Rule: r))).ToList();
        if (rules.Count == 0)
            return new GatePlan(gates, guards, problems);
        if (!project.ApiAuth)
            return new GatePlan(gates, guards, ["ChangeGates and GuardedColumns need ApiAuth=true: a rule about who may change a value needs to know who is asking."]);

        var apiTables = database.AspNetApiTables(project);
        foreach (var (setting, rule) in rules)
        {
            if (setting == "ChangeGates")
            {
                var (gate, problem) = ParseGate(rule, database, apiTables);
                if (gate is not null) gates.Add(gate); else problems.Add($"{setting}: \"{rule}\": {problem}");
            }
            else
            {
                var (guard, problem) = ParseGuard(rule, database, project, apiTables);
                if (guard is not null) guards.Add(guard); else problems.Add($"{setting}: \"{rule}\": {problem}");
            }
        }

        foreach (var table in gates.Select(g => g.Table).Concat(guards.Select(g => g.Table)).Distinct())
            if (UserTable.IsUserTable(table, project))
                problems.Add($"{table.TableName} is the user table; its own rules (credentials, administrators) cannot be combined with a gate or a guard.");
        foreach (var duplicate in gates.GroupBy(g => (g.Table, g.Column)).Where(g => g.Count() > 1))
            problems.Add($"ChangeGates names {duplicate.Key.Table.TableName}.{duplicate.Key.Column.Name} more than once.");
        foreach (var duplicate in guards.GroupBy(g => (g.Table, g.Column)).Where(g => g.Count() > 1))
            problems.Add($"GuardedColumns names {duplicate.Key.Table.TableName}.{duplicate.Key.Column.Name} more than once.");
        return new GatePlan(gates, guards, problems);
    }

    // ChangeGates: SalesInvoice.InvoiceStatusId; to 2,3,4,5,6; when Customer.CustomerStatusId=2; unless Customer.IgnoreBadStatus=1; start 1; message "Customer is in bad standing."
    private static (ChangeGate? Gate, string? Problem) ParseGate(string rule, DatabaseModel database, List<TableModel> apiTables)
    {
        var clauses = SplitOutsideQuotes(rule, ';');
        if (FindColumn(clauses[0], database, apiTables, out var table, out var column) is { } where)
            return (null, where);
        if (column!.IsComputed || table!.PrimaryKeyColumns.Any(k => k.Name == column.Name))
            return (null, $"{table!.TableName}.{column.Name} is a key or a computed column; a gate watches an ordinary column.");

        GateTerm? to = null;
        GatePredicate? when = null, unless = null;
        string? start = null, message = null;
        foreach (string clause in clauses.Skip(1))
        {
            var (keyword, rest) = Keyword(clause);
            switch (keyword)
            {
                case "to":
                    if (ParseTerm("it " + (rest.StartsWith('<') || rest.StartsWith('>') || rest.StartsWith('=') ? "" : "in ") + rest, table, column, out to, forOwnColumn: true) is { } toProblem)
                        return (null, "to: " + toProblem);
                    break;
                case "when" or "unless":
                    var parsed = ParsePredicate(rest, database, table, out var predicate);
                    if (parsed is not null)
                        return (null, keyword + ": " + parsed);
                    if (keyword == "when") when = predicate; else unless = predicate;
                    break;
                case "start":
                    if (NormalizeValue(column, Unquote(rest), out start) is { } startProblem)
                        return (null, "start: " + startProblem);
                    break;
                case "message":
                    message = Unquote(rest);
                    break;
                default:
                    return (null, $"'{clause.Trim()}' is not to, when, unless, start or message.");
            }
        }
        if (to is null)
            return (null, "it needs 'to', the values the column may not change to (to 2,3,4 or to > 1).");
        if (when is null)
            return (null, "it needs 'when', the condition that holds the change (when Customer.CustomerStatusId=2).");
        return (new ChangeGate(table!, column, to, when, unless, start, message, rule), null);
    }

    // GuardedColumns: Customer.IgnoreBadStatus; by Employee.IsManager; stampBy IgnoreBadStatusBy; stampOn IgnoreBadStatusOn
    private static (ColumnGuard? Guard, string? Problem) ParseGuard(string rule, DatabaseModel database, ProjectSettings project, List<TableModel> apiTables)
    {
        var clauses = SplitOutsideQuotes(rule, ';');
        if (FindColumn(clauses[0], database, apiTables, out var table, out var column) is { } where)
            return (null, where);
        if (column!.IsComputed || table!.PrimaryKeyColumns.Any(k => k.Name == column.Name))
            return (null, $"{table!.TableName}.{column.Name} is a key or a computed column; a guard protects an ordinary column.");
        if (!(column.IsBooleanColumn || column.IsIntegerColumn || column.IsNumericColumn || column.IsMoneyColumn || column.IsStringColumn))
            return (null, $"{table.TableName}.{column.Name} is not a yes/no, number or text column.");

        string? byText = null, stampByText = null, stampOnText = null;
        foreach (string clause in clauses.Skip(1))
        {
            var (keyword, rest) = Keyword(clause);
            switch (keyword)
            {
                case "by": byText = rest; break;
                case "stampby": stampByText = rest; break;
                case "stampon": stampOnText = rest; break;
                default: return (null, $"'{clause.Trim()}' is not by, stampBy or stampOn.");
            }
        }
        if (byText is null)
            return (null, "it needs 'by', the yes/no column of the user table that lets a person change it (by Employee.IsManager).");
        if (project.UserTable is not { } userTableName)
            return (null, "a guard reads the user table: set UserTable.");
        var userTable = database.Tables.FirstOrDefault(t => t.TableName.EqualsIgnoreCase(userTableName));
        if (userTable is null)
            return (null, $"UserTable names {userTableName}, which is not a table of the database.");
        string[] parts = byText.Split('.', 2, StringSplitOptions.TrimEntries);
        if (parts.Length != 2 || !parts[0].EqualsIgnoreCase(userTable.TableName))
            return (null, $"'by {byText}' must name a column of the user table ({userTable.TableName}.Column).");
        var by = userTable.Columns.FirstOrDefault(c => c.Name.EqualsIgnoreCase(parts[1]));
        if (by is null)
            return (null, $"{userTable.TableName} has no column {parts[1]}.");
        if (!by.IsBooleanColumn)
            return (null, $"{userTable.TableName}.{by.Name} must be a yes/no column.");

        ColumnModel? stampBy = null, stampOn = null;
        if (stampByText is not null)
        {
            stampBy = table.Columns.FirstOrDefault(c => c.Name.EqualsIgnoreCase(stampByText));
            if (stampBy is null || !stampBy.IsStringColumn)
                return (null, $"stampBy {stampByText} must be a text column of {table.TableName}.");
        }
        if (stampOnText is not null)
        {
            stampOn = table.Columns.FirstOrDefault(c => c.Name.EqualsIgnoreCase(stampOnText));
            if (stampOn is null || !stampOn.IsDateColumn)
                return (null, $"stampOn {stampOnText} must be a date column of {table.TableName}.");
        }
        return (new ColumnGuard(table, column, userTable, by, stampBy, stampOn, rule), null);
    }

    private static string? FindColumn(string text, DatabaseModel database, List<TableModel> apiTables, out TableModel? table, out ColumnModel? column)
    {
        table = null;
        column = null;
        string[] parts = text.Split('.', 2, StringSplitOptions.TrimEntries);
        if (parts.Length != 2 || parts[0].Length == 0 || parts[1].Length == 0)
            return $"'{text.Trim()}' is not Table.Column.";
        table = database.Tables.FirstOrDefault(t => t.TableName.EqualsIgnoreCase(parts[0]));
        if (table is null)
            return $"{parts[0]} is not a table of the database.";
        if (!apiTables.Contains(table))
            return $"{table.TableName} has no generated API (a gate or guard is checked in the API).";
        column = table.Columns.FirstOrDefault(c => c.Name.EqualsIgnoreCase(parts[1]));
        return column is null ? $"{table.TableName} has no column {parts[1]}." : null;
    }

    private static (string Keyword, string Text) Keyword(string clause)
    {
        string text = clause.Trim();
        int space = text.IndexOf(' ');
        return space < 0 ? (text.ToLowerInvariant(), "") : (text[..space].ToLowerInvariant(), text[(space + 1)..].Trim());
    }

    // ----- predicates

    private static string? ParsePredicate(string text, DatabaseModel database, TableModel table, out GatePredicate? predicate)
    {
        predicate = null;
        var alternatives = new List<IReadOnlyList<GateTerm>>();
        foreach (string orPart in SplitWord(text, "or"))
        {
            var all = new List<GateTerm>();
            foreach (string andPart in SplitWord(orPart, "and"))
            {
                if (ParseTermWithPath(andPart, database, table, out var term) is { } problem)
                    return problem;
                all.Add(term!);
            }
            alternatives.Add(all);
        }
        predicate = new GatePredicate(alternatives);
        return null;
    }

    private static string? ParseTermWithPath(string text, DatabaseModel database, TableModel table, out GateTerm? term)
    {
        term = null;
        var match = System.Text.RegularExpressions.Regex.Match(text.Trim(), @"^(?:(?<parent>\w+)\.)?(?<column>\w+)\s*(?<op><>|<=|>=|=|<|>|\s+in\s+)\s*(?<value>.+)$", System.Text.RegularExpressions.RegexOptions.IgnoreCase);
        if (!match.Success)
            return $"'{text.Trim()}' is not Column = value, Parent.Column = value, or a comparison with <>, <, >, <=, >= or in.";
        TableModel holder = table;
        ForeignKeyModel? via = null;
        if (match.Groups["parent"].Success)
        {
            string parentName = match.Groups["parent"].Value;
            var candidates = table.ForeignKeys.Where(fk => fk.ReferencedTable.EqualsIgnoreCase(parentName) && fk.ReferencingColumns.Count == 1 && fk.ReferencedColumns.Count == 1).ToList();
            if (candidates.Count == 0)
                return $"{table.TableName} has no single-column foreign key to {parentName}; a rule reads a column of the row or of a parent one foreign key away.";
            if (candidates.Count > 1)
                return $"{table.TableName} has {candidates.Count} foreign keys to {parentName} ({string.Join(", ", candidates.Select(c => c.ReferencingColumns[0]))}); a rule cannot tell which one is meant.";
            via = candidates[0];
            holder = database.Tables.FirstOrDefault(t => t.TableName.EqualsIgnoreCase(via.ReferencedTable) && t.SchemaName.EqualsIgnoreCase(via.ReferencedSchema)) ?? database.Tables.FirstOrDefault(t => t.TableName.EqualsIgnoreCase(via.ReferencedTable))!;
            if (holder is null)
                return $"{parentName} is not a table of the database.";
        }
        var column = holder.Columns.FirstOrDefault(c => c.Name.EqualsIgnoreCase(match.Groups["column"].Value));
        if (column is null)
            return $"{holder.TableName} has no column {match.Groups["column"].Value}.";
        string op = match.Groups["op"].Value.Trim().ToLowerInvariant();
        return ParseTerm($"{column.Name} {op} {match.Groups["value"].Value.Trim()}", holder, column, out term, forOwnColumn: false, via);
    }

    // "it in 2,3" / "it > 1" / "Status = 2": the operator and the values, checked against the column's type
    private static string? ParseTerm(string text, TableModel holder, ColumnModel column, out GateTerm? term, bool forOwnColumn, ForeignKeyModel? via = null)
    {
        term = null;
        var match = System.Text.RegularExpressions.Regex.Match(text.Trim(), @"^\S+\s*(?<op><>|<=|>=|=|<|>|in)\s*(?<value>.+)$", System.Text.RegularExpressions.RegexOptions.IgnoreCase);
        if (!match.Success)
            return $"'{text.Trim()}' is not an operator and a value.";
        var op = match.Groups["op"].Value.ToLowerInvariant() switch
        {
            "=" => GateOperator.Equal, "<>" => GateOperator.NotEqual, "<" => GateOperator.Less, "<=" => GateOperator.LessOrEqual,
            ">" => GateOperator.Greater, ">=" => GateOperator.GreaterOrEqual, _ => GateOperator.In
        };
        var values = new List<string>();
        foreach (string raw in op == GateOperator.In ? SplitOutsideQuotes(match.Groups["value"].Value, ',') : [match.Groups["value"].Value.Trim()])
        {
            if (NormalizeValue(column, Unquote(raw), out string? value) is { } problem)
                return problem;
            values.Add(value!);
        }
        if (op is GateOperator.Less or GateOperator.LessOrEqual or GateOperator.Greater or GateOperator.GreaterOrEqual && !(column.IsIntegerColumn || column.IsNumericColumn || column.IsMoneyColumn))
            return $"{column.Name} is not a number; use = <> or in.";
        if (op is GateOperator.Equal or GateOperator.NotEqual or GateOperator.Less or GateOperator.LessOrEqual or GateOperator.Greater or GateOperator.GreaterOrEqual && values.Count != 1)
            return "that operator takes one value.";
        term = new GateTerm(holder, via, column, op, values);
        return null;
    }

    /// <summary> The value as the rules keep it, or why the column cannot hold it. </summary>
    private static string? NormalizeValue(ColumnModel column, string text, out string? value)
    {
        value = null;
        if (column.IsBooleanColumn)
        {
            value = text.ToLowerInvariant() switch { "1" or "true" or "yes" => "true", "0" or "false" or "no" => "false", _ => null };
            return value is null ? $"{column.Name} is a yes/no column: use 1 or 0 (or true or false), not '{text}'." : null;
        }
        if (column.IsIntegerColumn || column.IsNumericColumn || column.IsMoneyColumn)
        {
            if (!decimal.TryParse(text, NumberStyles.Number | NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out decimal number))
                return $"{column.Name} is a number column: '{text}' is not a number.";
            if (column.IsIntegerColumn && number != decimal.Truncate(number))
                return $"{column.Name} is a whole-number column: '{text}' has a fraction.";
            value = number.ToString(CultureInfo.InvariantCulture);
            return null;
        }
        if (column.IsStringColumn)
        {
            value = text;
            return null;
        }
        return $"{column.Name} is not a yes/no, number or text column; a rule compares only those.";
    }

    // ----- text helpers

    private static string Unquote(string text)
    {
        text = text.Trim();
        return text.Length >= 2 && (text[0] == '"' || text[0] == '\'') && text[^1] == text[0] ? text[1..^1] : text;
    }

    /// <summary> Splits on a separator character that is not inside a double or single quote. Each piece is trimmed. </summary>
    internal static List<string> SplitOutsideQuotes(string text, char separator)
    {
        var parts = new List<string>();
        var current = new StringBuilder();
        char quote = '\0';
        foreach (char c in text)
        {
            if (quote != '\0')
            {
                current.Append(c);
                if (c == quote) quote = '\0';
            }
            else if (c == '"' || c == '\'')
            {
                quote = c;
                current.Append(c);
            }
            else if (c == separator)
            {
                parts.Add(current.ToString().Trim());
                current.Clear();
            }
            else
                current.Append(c);
        }
        parts.Add(current.ToString().Trim());
        return parts;
    }

    // splits on the word between spaces (and / or), outside quotes
    private static List<string> SplitWord(string text, string word)
    {
        var parts = new List<string>();
        var current = new StringBuilder();
        char quote = '\0';
        for (int i = 0; i < text.Length; i++)
        {
            char c = text[i];
            if (quote != '\0')
            {
                current.Append(c);
                if (c == quote) quote = '\0';
            }
            else if (c == '"' || c == '\'')
            {
                quote = c;
                current.Append(c);
            }
            else if (char.IsWhiteSpace(c) && i + word.Length + 1 < text.Length && string.Compare(text, i + 1, word, 0, word.Length, StringComparison.OrdinalIgnoreCase) == 0 && char.IsWhiteSpace(text[i + 1 + word.Length]))
            {
                parts.Add(current.ToString().Trim());
                current.Clear();
                i += word.Length + 1;
            }
            else
                current.Append(c);
        }
        parts.Add(current.ToString().Trim());
        return parts;
    }
}

/// <summary> A gate's conditions as C# for the API template (API_Gates). </summary>
public static class GateCode
{
    /// <summary> The column as a message says it: <c>Invoice Status</c> for <c>InvoiceStatusId</c> (a key reads as the thing it points at). </summary>
    public static string Words(ColumnModel column)
    {
        string words = Labels.Words(column.Name);
        return words.EndsWith(" Id", StringComparison.Ordinal) && words.Length > 3 ? words[..^3] : words;
    }

    /// <summary> The value as a C# literal of the column's type: <c>2</c>, <c>2m</c>, <c>true</c>, <c>"text"</c>. </summary>
    public static string Literal(ColumnModel column, string value)
    {
        if (column.IsBooleanColumn || column.IsStringColumn)
            return column.IsBooleanColumn ? value : CSharpText.Quote(value);
        return column.CSharpBase() switch { "decimal" => value + "m", "double" => value + "d", "float" => value + "f", _ => value };
    }

    /// <summary> <paramref name="access"/> compared as the term says: <c>row.Status == 2</c>, <c>row.Status is 2 or 3</c>. </summary>
    public static string Test(string access, GateTerm term)
    {
        string[] literals = term.Values.Select(v => Literal(term.Column, v)).ToArray();
        return term.Operator switch
        {
            GateOperator.Equal => $"{access} == {literals[0]}",
            GateOperator.NotEqual => $"{access} != {literals[0]}",
            GateOperator.Less => $"{access} < {literals[0]}",
            GateOperator.LessOrEqual => $"{access} <= {literals[0]}",
            GateOperator.Greater => $"{access} > {literals[0]}",
            GateOperator.GreaterOrEqual => $"{access} >= {literals[0]}",
            _ => $"{access} is {string.Join(" or ", literals)}"
        };
    }

    /// <summary> The whole condition; <paramref name="test"/> writes one term (it knows where the term's column is read from). </summary>
    public static string Predicate(GatePredicate predicate, Func<GateTerm, string> test) =>
        string.Join(" || ", predicate.AnyOf.Select(all => all.Count == 1 ? test(all[0]) : "(" + string.Join(" && ", all.Select(test)) + ")"));
}

/// <summary> The words a screen shows for a guard's stamp column (who changed the guarded column, and when) on a row that was saved before the stamp existed. </summary>
public static class GuardStamp
{
    public const string NotRecorded = "not recorded";
}
