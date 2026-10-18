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
    public static string Client(TableModel table, ProjectSettings project, string modelNamespace, string clientNamespace)
    {
        string type = BlazorNames.TypeName(table.TableName);
        var key = KeyType.Of(table)!;
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
        L($"public class {type}Client(HttpClient http)");
        L("{");
        L($"    private const string Url = \"{BlazorNames.ApiRoute(table.TableName)}\";");
        L();
        L($"    public async Task<List<{type}>> GetAllAsync() => await (await http.GetAsync(Url)).ReadAsync<List<{type}>>();");
        L();
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
        L();
        L($"    public async Task DeleteAsync({keyType} id) => await (await http.DeleteAsync($\"{{Url}}/{{{id}}}\")).EnsureSuccessAsync();");
        if (CloneShape.CanClone(table, project))
        {
            L();
            L("    // A copy of the row, made by the API; the copy comes back.");
            L($"    public async Task<{type}> CloneAsync({keyType} id) => await (await http.PostAsync($\"{{Url}}/{{{id}}}/clone\", null)).ReadAsync<{type}>();");
        }
        if (GatePlan.GuardsOf(project, table).Count > 0)
        {
            L();
            L("    // Which guarded columns the signed-in person may change (the form shows the others as text), by the column's JSON name.");
            L("    public async Task<Dictionary<string, bool>> GetGuardsAsync() => await (await http.GetAsync($\"{Url}/guards\")).ReadAsync<Dictionary<string, bool>>();");
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
}
