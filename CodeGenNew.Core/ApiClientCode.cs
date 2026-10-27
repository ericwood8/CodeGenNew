using System.Text;

namespace CodeGenNew.Core;

/// <summary> The C# a .NET front end (the Blazor and Uno apps) reads a table's API through: the model class the JSON becomes and the client over an <c>HttpClient</c> that calls the table's routes. Both
/// stacks write the same text from here and differ only in their namespaces. The client needs <c>ApiException</c>, <c>PagedResult&lt;T&gt;</c> and the response helpers of the Support essentials group. </summary>
public static class ApiClientCode
{
    /// <summary> The model class of the table, one property per column. A non-null text property starts as <c>""</c>, a binary one as an empty array. With <c>ApiAnnotations</c> the class carries the
    /// attributes of the API's entity. <paramref name="tinyIntAsShort"/>: a tinyint column is a <c>short</c> (the Blazor <c>InputNumber</c> has no byte), which the API still reads and writes as the same JSON number. </summary>
    public static string Model(TableModel table, ProjectSettings project, string modelNamespace, bool tinyIntAsShort)
    {
        string type = BlazorNames.TypeName(table.TableName);
        var o = new StringBuilder();
        void L(string s = "") => o.Append(s).Append('\n');
        if (project.ApiAnnotations)
        {
            L("using System.ComponentModel.DataAnnotations;");
            L();
        }
        L($"namespace {modelNamespace};");
        L();
        L($"public class {type}");
        L("{");
        foreach (var c in table.Columns)
        {
            string initial = c.IsNullable ? "" : c.CSharpBase() switch
            {
                "string" => " = \"\";",
                "byte[]" => " = [];",
                "object" => " = new();",
                _ => ""
            };
            string propertyType = tinyIntAsShort && c.SqlType == System.Data.SqlDbType.TinyInt ? (c.IsNullable ? "short?" : "short") : c.CSharp();
            // the columns a person fills in (not an audit column, not an identity key, not computed): the attributes CS_Validation writes on the entity's metadata class
            if (project.ApiAnnotations && !c.IsComputed && !c.IsAuditColumn && !(c.IsPrimaryKey && c.IsIdentity))
                foreach (string attribute in ValidationAttributes.Of(c, project, table.TableName))
                    L("    " + attribute);
            L($"    public {propertyType} {c.CSharpName()} {{ get; set; }}{initial}");
        }
        L("}");
        return o.ToString();
    }

    /// <summary> The client class of the table (<c>&lt;Type&gt;Client</c>) with the lookup row record: GetAll, GetLookup (with a search or keys for a type-ahead), Get, Create, Update, Delete, Clone (a table
    /// <see cref="CloneShape"/> allows), GetGuards (a table with guarded columns) and GetPage (search, paging and sort). The table must have a client (<see cref="BlazorNames.Refusal"/> is null). </summary>
    public static string Client(TableModel table, ProjectSettings project, string modelNamespace, string clientNamespace, bool childRows = false, bool pagedHere = false)
    {
        string type = BlazorNames.TypeName(table.TableName);
        var key = KeyType.Of(table)!;
        bool nameActive = table.IsNameActiveTable;   // its API (NameActiveCrudApi) has no search, no paging, no clone and no child rows
        string keyType = key.CSharp;   // int, long, Guid or string
        // a text key can hold characters a route treats as its own (a space, a slash, a question mark): it is escaped into the path
        string id = key.Kind == KeyKind.Text ? "Uri.EscapeDataString(id)" : "id";
        var o = new StringBuilder();
        void L(string s = "") => o.Append(s).Append('\n');

        L("using System.Net.Http.Json;");
        L($"using {modelNamespace};");
        L();
        L($"namespace {clientNamespace};");
        L();
        L("// One row of GET " + BlazorNames.ApiRoute(table.TableName) + "/lookup: the key, the text a person recognises the row by and whether the row is active (absent for a table with no IsActive column).");
        L($"public record {BlazorNames.LookupType(table.TableName)}({keyType} Id, string Name, bool? IsActive = null);");
        L();
        var queue = nameActive ? null : QueuePlan.For(table, project);
        if (queue is not null)
        {
            L("// GET " + BlazorNames.ApiRoute(table.TableName) + "/stats: the rows in each status and the oldest pending row (QueueTables).");
            L($"public record {type}QueueStats(int Pending, int Claimed, int Done, int Failed, {keyType}? OldestPendingId, DateTime? OldestPendingOn);");
            L();
        }
        if (!nameActive && TreePlan.For(table, project) is not null)
        {
            L("// One row of GET " + BlazorNames.ApiRoute(table.TableName) + "/tree: the rows depth first, level 1 is a root (TreeTables).");
            L($"public record {type}TreeNode(long Id, long? ParentId, int Depth, bool HasChildren, string Text);");
            L();
        }
        L($"public class {type}Client(HttpClient http)");
        L("{");
        L($"    private const string Url = \"{BlazorNames.ApiRoute(table.TableName)}\";");
        L();
        L($"    public async Task<List<{type}>> GetAllAsync() => await (await http.GetAsync(Url)).ReadAsync<List<{type}>>();");
        L();
        if (nameActive)
        {
            L("    // The rows whose IsActive is true. A name / active table's API has no search and no paging: GetAllAsync reads the whole list.");
            L($"    public async Task<List<{type}>> GetActiveAsync() => await (await http.GetAsync($\"{{Url}}/active\")).ReadAsync<List<{type}>>();");
            L();
        }
        L("    // With a search only the first few rows whose text contains it, with ids only the rows with those keys (a type-ahead for a parent with too many rows for a drop-down).");
        L($"    public async Task<List<{BlazorNames.LookupType(table.TableName)}>> GetLookupAsync(string? search = null, IEnumerable<{keyType}>? ids = null, int? take = null)");
        L("    {");
        L("        var query = new List<string>();");
        L("        if (!string.IsNullOrWhiteSpace(search)) query.Add($\"search={Uri.EscapeDataString(search)}\");");
        L("        if (ids is not null) query.Add($\"ids={Uri.EscapeDataString(string.Join(\",\", ids))}\");");
        L("        if (take is not null) query.Add($\"take={take}\");");
        L($"        return await (await http.GetAsync($\"{{Url}}/lookup{{(query.Count == 0 ? \"\" : \"?\" + string.Join(\"&\", query))}}\")).ReadAsync<List<{BlazorNames.LookupType(table.TableName)}>>();");
        L("    }");
        L();
        L($"    public async Task<{type}> GetAsync({keyType} id) => await (await http.GetAsync($\"{{Url}}/{{{id}}}\")).ReadAsync<{type}>();");
        L();
        L($"    public async Task<{type}> CreateAsync({type} row) => await (await http.PostAsJsonAsync(Url, row)).ReadAsync<{type}>();");
        L();
        L($"    public async Task UpdateAsync({keyType} id, {type} row) => await (await http.PutAsJsonAsync($\"{{Url}}/{{{id}}}\", row)).EnsureSuccessAsync();");
        if (childRows && !nameActive)
        {
            L();
            L("    // The row with the rows of its editable child tables (EditableChildren), saved in one request: each key of children is the name of a list in the body (departmentTeams), each value the rows of that child.");
            L($"    public async Task<{type}> CreateAsync({type} row, IReadOnlyDictionary<string, object> children) => await (await http.PostAsJsonAsync(Url, WithChildren(row, children))).ReadAsync<{type}>();");
            L();
            L($"    public async Task UpdateAsync({keyType} id, {type} row, IReadOnlyDictionary<string, object> children) => await (await http.PutAsJsonAsync($\"{{Url}}/{{{id}}}\", WithChildren(row, children))).EnsureSuccessAsync();");
            L();
            L($"    private static System.Text.Json.Nodes.JsonObject WithChildren({type} row, IReadOnlyDictionary<string, object> children)");
            L("    {");
            L("        var body = System.Text.Json.JsonSerializer.SerializeToNode(row, System.Text.Json.JsonSerializerOptions.Web)!.AsObject();");
            L("        foreach (var (name, rows) in children)");
            L("            body[name] = System.Text.Json.JsonSerializer.SerializeToNode(rows, rows.GetType(), System.Text.Json.JsonSerializerOptions.Web);");
            L("        return body;");
            L("    }");
        }
        L();
        L($"    public async Task DeleteAsync({keyType} id) => await (await http.DeleteAsync($\"{{Url}}/{{{id}}}\")).EnsureSuccessAsync();");
        if (!nameActive && CloneShape.CanClone(table, project))
        {
            L();
            L("    // A copy of the row, made by the API; the copy comes back.");
            L($"    public async Task<{type}> CloneAsync({keyType} id) => await (await http.PostAsync($\"{{Url}}/{{{id}}}/clone\", null)).ReadAsync<{type}>();");
        }
        if (queue is not null)
        {
            L();
            L("    // A work queue: the counts by status, and a failed row put back to pending; the row comes back.");
            L($"    public async Task<{type}QueueStats> GetQueueStatsAsync() => await (await http.GetAsync($\"{{Url}}/stats\")).ReadAsync<{type}QueueStats>();");
            L();
            L($"    public async Task<{type}> RetryAsync({keyType} id) => await (await http.PostAsync($\"{{Url}}/{{{id}}}/retry\", null)).ReadAsync<{type}>();");
        }
        if (!nameActive && TreePlan.For(table, project) is not null)
        {
            L();
            L("    // A tree (TreeTables): every row depth first with its level, and the children of a row handed to the row's parent so it can be deleted.");
            L($"    public async Task<List<{type}TreeNode>> GetTreeAsync() => await (await http.GetAsync($\"{{Url}}/tree\")).ReadAsync<List<{type}TreeNode>>();");
            L();
            L($"    public async Task PromoteChildrenAsync({keyType} id) => (await http.PostAsync($\"{{Url}}/{{{id}}}/promote-children\", null)).EnsureSuccessStatusCode();");
        }
        if (!nameActive && SoftDeletePlan.For(table, project) is not null)
        {
            L();
            L("    // Soft delete (SoftDeleteTables): the deleted rows, and a deleted row brought back (an administrator); the row comes back.");
            L($"    public async Task<List<{type}>> GetDeletedAsync() => await (await http.GetAsync($\"{{Url}}/deleted\")).ReadAsync<List<{type}>>();");
            L();
            L($"    public async Task<{type}> RestoreAsync({keyType} id) => await (await http.PostAsync($\"{{Url}}/{{{id}}}/restore\", null)).ReadAsync<{type}>();");
        }
        if (!nameActive && EffectiveDatePlan.For(table, project) is not null)
        {
            L();
            L("    // Effective dating (EffectiveDatedTables): the rows in effect on a day (today when left out), by the date text the API reads (2026-10-27).");
            L($"    public async Task<List<{type}>> GetCurrentAsync(string? on = null) => await (await http.GetAsync(on is null ? $\"{{Url}}/current\" : $\"{{Url}}/current?on={{Uri.EscapeDataString(on)}}\")).ReadAsync<List<{type}>>();");
        }
        if (!nameActive && GatePlan.GuardsOf(project, table).Count > 0)
        {
            L();
            L("    // Which guarded columns the signed-in person may change (the form shows the others as text), by the column's JSON name.");
            L("    public async Task<Dictionary<string, bool>> GetGuardsAsync() => await (await http.GetAsync($\"{Url}/guards\")).ReadAsync<Dictionary<string, bool>>();");
        }
        if (nameActive)
        {
            if (pagedHere)
                NameActivePage(o, table, type);
            L("}");
            return o.ToString();
        }
        L();
        L("    // The filters are the search boxes of the screen, by the names the API's search endpoint reads; a blank filter is left out. sortBy is a column name.");
        L($"    public async Task<PagedResult<{type}>> GetPageAsync(int page, int pageSize, IReadOnlyDictionary<string, string?>? filters = null, string? sortBy = null, bool descending = false)");
        L("    {");
        L("        var query = new List<string> { $\"pageNumber={page}\", $\"pageSize={pageSize}\" };");
        L("        foreach (var (name, value) in filters ?? new Dictionary<string, string?>())");
        L("            if (!string.IsNullOrWhiteSpace(value))");
        L("                query.Add($\"{name}={Uri.EscapeDataString(value)}\");");
        L("        if (sortBy is not null)");
        L("        {");
        L("            query.Add($\"sortBy={Uri.EscapeDataString(sortBy)}\");");
        L("            query.Add($\"sortDir={(descending ? \"desc\" : \"asc\")}\");");
        L("        }");
        L($"        return await (await http.GetAsync($\"{{Url}}/search?{{string.Join('&', query)}}\")).ReadAsync<PagedResult<{type}>>();");
        L("    }");
        L("}");
        return o.ToString();
    }

    /// <summary> The paged read of a name / active table, whose API has no search and no paging: the whole list is read and narrowed, sorted and cut into pages in the client, so a screen that pages
    /// every table the same way (the Uno list) needs no case of its own. A filter named after a search box keeps the rows whose text contains the value, a key filter the rows with that key, and
    /// <c>isActive</c> set to <c>true</c> the active rows. Switches over the columns, not reflection, so a trimmed app keeps working. </summary>
    private static void NameActivePage(StringBuilder o, TableModel table, string type)
    {
        void L(string s = "") => o.Append(s).Append('\n');
        var filterColumns = table.SearchableColumns.Concat(table.SearchKeyColumns).Distinct().ToList();
        var sortColumns = table.Columns.Where(SearchSort.IsSortable).ToList();
        string Quote(string s) => "\"" + s + "\"";
        L();
        L("    // This table's API has no search and no paging: the list is read whole and narrowed, sorted and cut into pages here. A filter named after a search box keeps the rows whose text contains the value,");
        L("    // a key filter the rows with that key, and isActive set to true the active rows. sortBy is a column name.");
        L($"    public async Task<PagedResult<{type}>> GetPageAsync(int page, int pageSize, IReadOnlyDictionary<string, string?>? filters = null, string? sortBy = null, bool descending = false)");
        L("    {");
        L("        var rows = (await GetAllAsync()).AsEnumerable();");
        L("        foreach (var (name, value) in filters ?? new Dictionary<string, string?>())");
        L("        {");
        L("            if (string.IsNullOrWhiteSpace(value))");
        L("                continue;");
        L("            string wanted = value.Trim();");
        L("            rows = name.ToLowerInvariant() switch");
        L("            {");
        L("                \"isactive\" => wanted == \"true\" ? rows.Where(r => r.IsActive) : rows,");
        foreach (var c in filterColumns)
        {
            string test = c.IsStringColumn
                ? $"(r.{c.CSharpName()} ?? \"\").Contains(wanted, StringComparison.OrdinalIgnoreCase)"
                : $"Convert.ToString(r.{c.CSharpName()}, System.Globalization.CultureInfo.InvariantCulture) == wanted";
            L($"                {Quote(c.Name.ToLowerInvariant())} => rows.Where(r => {test}),");
        }
        L("                _ => rows");
        L("            };");
        L("        }");
        L("        if (sortBy is not null)");
        L("        {");
        L("            object? Key(" + type + " r) => sortBy switch");
        L("            {");
        foreach (var c in sortColumns)
            L($"                {Quote(c.Name)} => r.{c.CSharpName()},");
        L("                _ => null");
        L("            };");
        L("            rows = descending ? rows.OrderByDescending(Key) : rows.OrderBy(Key);");
        L("        }");
        L("        var all = rows.ToList();");
        L("        int pages = (all.Count + pageSize - 1) / pageSize;");
        L($"        return new PagedResult<{type}> {{ Items = all.Skip((page - 1) * pageSize).Take(pageSize).ToList(), Page = page, PageSize = pageSize, TotalCount = all.Count, TotalPages = pages }};");
        L("    }");
    }
}
