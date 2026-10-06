using System.Text;

namespace CodeGenNew.Core;

/// <summary> One request of a table's API: its name, the HTTP method, the path under the host (with <c>{{id}}</c> where the key goes) and the JSON body, if any. </summary>
public sealed record ApiRequest(string Name, string Method, string Path, string? Body);

/// <summary> The requests a table's API answers, worked out once for the files that list them (API_Http, the Postman and the Bruno collections). The routes are the ones the API templates write. </summary>
public static class ApiRequests
{
    /// <summary> The route of a table's API: <c>/api/customers</c>. </summary>
    public static string Route(TableModel table) => "/api/" + table.TableName.Pluralize().ToLowerInvariant();

    /// <summary> The requests of a table that has an API of its own (<see cref="TableModel.HasCrudApi"/>): all, one, search (a table that has one), add, change, remove and copy (a table that can be cloned). </summary>
    public static List<ApiRequest> For(TableModel model, ProjectSettings project)
    {
        string route = Route(model);
        string plural = model.TableName.Pluralize();
        string one = Labels.Words(model.TableName).ToLowerInvariant();
        string a = "aeiou".Contains(one[0]) ? "an" : "a";
        var requests = new List<ApiRequest> { new($"All {plural}", "GET", route, null), new($"One {one} by its id", "GET", route + "/{{id}}", null) };
        if (model.HasSearchApi(project))
        {
            var search = model.SearchableColumns;
            string filters = string.Concat(search.Take(2).Select(c => JsonNames.Camel(c.Name) + "=&"));
            string sort = search.Count > 0 ? "&sortBy=" + JsonNames.Camel(search[0].Name) + "&sortDir=asc" : "";
            requests.Add(new($"Search {plural}", "GET", route + "/search?" + filters + "pageNumber=1&pageSize=20" + sort, null));
        }
        requests.Add(new($"Add {a} {one}", "POST", route, Body(model, project, update: false)));
        requests.Add(new($"Change {a} {one}", "PUT", route + "/{{id}}", Body(model, project, update: true)));
        requests.Add(new($"Remove {a} {one}", "DELETE", route + "/{{id}}", null));
        if (CloneShape.CanClone(model, project))
            requests.Add(new($"Copy {a} {one}", "POST", route + "/{{id}}/clone", null));
        return requests;
    }

    /// <summary> The create or update body: a value for every column that cannot be NULL, by its JSON name; the key is <c>{{id}}</c> for an update. </summary>
    public static string Body(TableModel model, ProjectSettings project, bool update)
    {
        var lines = ApiSampleValues.BodyColumns(model).Select(c => "  " + ApiSampleValues.Json(JsonNames.Camel(c.Name)) + ": " + (update && c.IsPrimaryKey ? "{{id}}" : ApiSampleValues.Value(c, project)));
        return "{\n" + string.Join(",\n", lines) + "\n}";
    }

    /// <summary> Text as a JSON string, line breaks included. </summary>
    public static string JsonString(string text) => "\"" + text.Replace("\\", "\\\\").Replace("\"", "\\\"").Replace("\r", "").Replace("\n", "\\n") + "\"";

    /// <summary> A file name from a request name: the characters a file system refuses become a space. </summary>
    public static string FileName(string name)
    {
        var builder = new StringBuilder();
        foreach (char c in name)
            builder.Append(Path.GetInvalidFileNameChars().Contains(c) ? ' ' : c);
        return builder.ToString().Trim();
    }
}
