namespace CodeGenNew.Core;

/// <summary> The words of the sessions and tokens page, the same in every front end. </summary>
public static class TokenScreenWords
{
    public const string ActiveColumn = "Active";
    public const string ActionsColumn = "Actions";
    public const string Revoke = "Revoke";
    public const string SignOut = "Sign out everywhere";
    public const string Yes = "Yes";
    public const string No = "No";
    public const string NoRows = "There is nothing to show.";
    public const string LoadFailed = "The list could not be read.";
    public const string ChangeFailed = "The change could not be saved.";
    public const string Hint = "The tokens themselves are never shown. Revoking ends a row at once; signing a person out everywhere ends all of their active rows.";
    public const string EndedSuffix = " ended.";

    /// <summary> The answer to a sign out everywhere: how many rows ended. </summary>
    public static string SignedOut(int count) => count + EndedSuffix;
}

/// <summary> One column of the page: the name in the answer, the caption and what kind of value it holds (<c>text</c>, <c>flag</c> or <c>date</c>). </summary>
public sealed record TokenColumn(string Json, string Label, string Kind);

/// <summary> The page of one token table: the list of its rows with a Revoke button and, when the table points at a person, a Sign out everywhere button. </summary>
public sealed record TokenPage(TokenShape Shape, string ApiRoute, string Path, string Label, string Name, string KeyJson, string? PersonJson, IReadOnlyList<TokenColumn> Columns)
{
    public string Camel => char.ToLowerInvariant(Name[0]) + Name[1..];
}

/// <summary> Which front ends show the sessions and tokens of <c>TokenTables</c>: for the administrator, a list of each table's rows without the hash (the API's <c>GET</c>), a Revoke button on an active row and a Sign out
/// everywhere button on a row that has a person. A front end that signs in needs the routes, so it needs <c>Auth</c> and <c>ApiAuth</c>. </summary>
public static class TokenScreens
{
    public static IReadOnlyList<TokenPage> Pages(DatabaseModel database, ProjectSettings project)
    {
        if (!project.Auth || !project.ApiAuth)
            return [];
        var pages = new List<TokenPage>();
        foreach (var shape in database.Tables.Select(t => TableShapes.For(t, project).Token).OfType<TokenShape>().OrderBy(t => t.Table.TableName, StringComparer.OrdinalIgnoreCase))
        {
            var columns = shape.Shown.Where(c => c.Name != shape.Key.Name).Select(c => new TokenColumn(JsonNames.Camel(c.Name), c.FlagWords, c.IsBooleanColumn ? "flag" : LogTablePlan.HasTimeOfDay(c) || c.IsDateColumn ? "date" : "text")).ToList();
            string name = ScreenNames.BaseName(shape.Table.TableName);
            pages.Add(new TokenPage(shape, BlazorNames.ApiRoute(shape.Table.TableName), ScreenNames.Route(shape.Table.TableName), ScreenNames.Label(shape.Table.TableName).Pluralize(), name + "Tokens",
                JsonNames.Camel(shape.Key.Name), shape.Person is null ? null : JsonNames.Camel(shape.Person.Name), columns));
        }
        return pages;
    }
}
