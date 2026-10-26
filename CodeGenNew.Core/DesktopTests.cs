using System.Text;

namespace CodeGenNew.Core;

/// <summary> The desktop stack a generated view-model test project belongs to. </summary>
public enum DesktopTestStack { WinUI3, Uno }

/// <summary> One check a view-model test makes after a valid row was opened: a statement that spoils one field, and the sentence the save must answer with. </summary>
public sealed record DesktopRefusal(string Name, string Statement, string Sentence);

/// <summary> What the view-model tests of one table check, decided from the same rules the view model is written from (<see cref="DesktopFormCode"/>), so WinUI3 and Uno test the same things: a new row with
/// nothing filled in is refused with the first required field's sentence, a row opened and saved unchanged is saved with every value it had, and each rule the form has (a shape, a date never later than today,
/// an end date before its start, a number that is not whole) refuses a spoiled value. </summary>
public sealed class DesktopTestPlan
{
    private DesktopTestPlan() { }

    /// <summary> The view model class: <c>&lt;Table&gt;DetailViewModel</c>, or <c>&lt;Table&gt;DetailMasterViewModel</c> for a table with child tables. </summary>
    public required string ViewModel { get; init; }

    /// <summary> The entity (WinUI3) or model (Uno) class the view model edits. </summary>
    public required string Entity { get; init; }

    /// <summary> The method that fills the drop-downs, and the child grids of a master, before the dialog shows. </summary>
    public required string LoadMethod { get; init; }

    /// <summary> A row every rule accepts, as the JSON the API takes: a value for every column that cannot be NULL. </summary>
    public required string RowJson { get; init; }

    /// <summary> The API route of the table (Uno), such as <c>/api/customers</c>, and the key text in a request path. </summary>
    public required string Route { get; init; }

    public required string KeyText { get; init; }

    /// <summary> The sentence a new row with nothing filled in is refused with, or null when the table has no required field. </summary>
    public string? RequiredSentence { get; init; }

    public required IReadOnlyList<DesktopRefusal> Refusals { get; init; }

    /// <summary> The plan of a table, or null when the table has no dialog (no single key, or a table the stack writes no screen for). </summary>
    public static DesktopTestPlan? For(TableModel table, ProjectSettings project, DesktopTestStack stack)
    {
        var form = FormModel.For(table, project);
        if (form.KeyColumn is null || form.KeyType is null || (stack == DesktopTestStack.Uno ? UnoNames.Refusal(table, project) : null) is not null)
            return null;
        bool master = DatabaseModel.IsDetailMaster(table, project);
        var refusals = new List<DesktopRefusal>();
        string? required = null;
        var wholeDone = false;
        foreach (var c in form.Columns)
        {
            var kind = form.ValueOf(c);
            string label = Labels.Column(c.Name);
            if (required is null && !c.IsNullable && kind != ValueKind.Boolean)
                required = label + " is required.";
            if (kind == ValueKind.Text && c.RuleOf(project, table.TableName) is { Shape: not TextShape.None } rule)
                refusals.Add(new($"Refuses_a_{Name(c)}_that_is_not_{ColumnRules.NounOf(rule).Replace(' ', '_')}", $"model.{c.Name} = \"!?!\";", $"{label} is not {ColumnRules.NounOf(rule)}."));
            if (kind == ValueKind.Date)
            {
                if (c.RuleOf(project, table.TableName) is { NotFuture: true })
                    refusals.Add(new($"Refuses_a_{Name(c)}_later_than_today", $"model.{c.Name} = DateTimeOffset.Now.AddYears(5);", $"{label} must not be later than today."));
                if (DatePairs.EndingAt(table, project, c) is { } pair && form.Columns.Contains(pair.Start) && form.ValueOf(pair.Start) == ValueKind.Date)
                    refusals.Add(new($"Refuses_a_{Name(c)}_before_the_{Name(pair.Start)}", $"model.{c.Name} = model.{pair.Start.Name}!.Value.AddDays(-1);", pair.Message));
            }
            if (kind == ValueKind.WholeNumber && !wholeDone && project.RangeFor(c) is not null)
            {
                wholeDone = true;
                refusals.Add(new($"Refuses_a_{Name(c)}_that_is_not_whole", $"model.{c.Name} = 1.5;", $"{label} must be a whole number."));
            }
        }
        string key = ApiSampleValues.SampleId(table);
        var members = ApiSampleValues.BodyColumns(table).Where(c => !c.IsRowVersionColumn && !c.IsCredentialColumn).Select(c => ApiSampleValues.Json(JsonNames.Camel(c.Name)) + ": " + (c.IsPrimaryKey ? (c.IsIntegerColumn ? key : ApiSampleValues.Json(key)) : ApiSampleValues.Value(c, project, table.TableName)));
        // a row version is filled by the database, and SQLite is given the same starting value for it (TestDb), so an update finds the row it expects
        members = members.Concat(table.Columns.Where(c => c.IsRowVersionColumn).Select(c => ApiSampleValues.Json(JsonNames.Camel(c.Name)) + ": " + ApiSampleValues.Json(DesktopTestCode.RowVersionBase64)));
        string typeName = BlazorNames.TypeName(table.TableName);
        return new DesktopTestPlan
        {
            ViewModel = table.TableName + (master ? "DetailMasterViewModel" : "DetailViewModel"),
            Entity = stack == DesktopTestStack.Uno ? typeName : table.TableName,
            LoadMethod = master ? "LoadAsync" : "LoadLookupsAsync",
            RowJson = "{ " + string.Join(", ", members) + " }",
            Route = BlazorNames.ApiRoute(table.TableName),
            KeyText = Uri.EscapeDataString(key),
            RequiredSentence = required,
            Refusals = refusals
        };

        static string Name(ColumnModel c) => new(Labels.Column(c.Name).Select(ch => char.IsLetterOrDigit(ch) ? ch : '_').ToArray());
    }
}

/// <summary> The text of the view-model test projects of the WinUI3 and Uno apps. The project is a plain <c>net10.0</c> one that compiles the app's own view models, models (or entities, repositories and context)
/// and API clients as linked files, so it runs without Windows, WinUI or a device. The two platform types a view model touches (<c>Visibility</c> and the WinRT currency formatter) are small stand-ins. </summary>
public static class DesktopTestCode
{
    /// <summary> The value every row version column starts with in the test database, as base64 (eight bytes, the last one 1). </summary>
    public const string RowVersionBase64 = "AAAAAAAAAAE=";

    /// <summary> The stand-ins for the two platform types the view models use. </summary>
    public const string PlatformShims = """
namespace Microsoft.UI.Xaml
{
    public enum Visibility { Visible, Collapsed }
}

namespace Windows.Globalization.NumberFormatting
{
    public sealed class CurrencyFormatter(string currency)
    {
        public string Currency => currency;
        public bool IsGrouped { get; set; }
        public int FractionDigits { get; set; }
        public string FormatDouble(double value) => value.ToString("N" + FractionDigits, System.Globalization.CultureInfo.InvariantCulture);
    }
}
""";

    /// <summary> The JSON options, and the comparison, every test uses. </summary>
    public static string JsonSupport(string testNamespace) => JsonSupportText.Replace("TESTNAMESPACE", testNamespace);

    private const string JsonSupportText = """
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;

namespace TESTNAMESPACE;

/// <summary> The JSON the API speaks, and a comparison that looks only at the properties the expected row has. </summary>
public static class TestJson
{
    public static readonly JsonSerializerOptions Options = new(JsonSerializerDefaults.Web) { ReferenceHandler = ReferenceHandler.IgnoreCycles };

    public static string Write<T>(T value) => JsonSerializer.Serialize(value, Options);

    /// <summary> The first property of <paramref name="expected"/> that <paramref name="actual"/> lacks or holds another value for, or null when every one is there. </summary>
    public static string? FirstDifference(string actual, string expected)
    {
        var have = JsonNode.Parse(actual)!.AsObject();
        foreach (var (name, value) in JsonNode.Parse(expected)!.AsObject())
        {
            var found = have.FirstOrDefault(p => string.Equals(p.Key, name, StringComparison.OrdinalIgnoreCase)).Value;
            if (!JsonNode.DeepEquals(found, value))
                return $"{name}: expected {value?.ToJsonString() ?? "null"}, found {found?.ToJsonString() ?? "null"}";
        }
        return null;
    }
}
""";

    /// <summary> The project file: the app's own files linked in, the stand-ins, and the packages those files need. </summary>
    public static string Csproj(DesktopTestStack stack, ProjectSettings project, string appFolder, string testNamespace)
    {
        string app = appFolder.Replace('\\', '/');
        var o = new StringBuilder();
        void L(string s = "") => o.Append(s).Append('\n');
        L("<Project Sdk=\"Microsoft.NET.Sdk\">");
        L();
        L("  <PropertyGroup>");
        L("    <TargetFramework>net10.0</TargetFramework>");
        L("    <Nullable>enable</Nullable>");
        L("    <ImplicitUsings>enable</ImplicitUsings>");
        L("    <IsPackable>false</IsPackable>");
        L($"    <RootNamespace>{testNamespace}</RootNamespace>");
        L("    <NoWarn>$(NoWarn);MVVMTK0045;MSTEST0001</NoWarn>");
        L("  </PropertyGroup>");
        L();
        L("  <!-- The app's own view models and what they read, compiled here; no window, no WinUI. -->");
        L("  <ItemGroup>");
        foreach (string folder in stack == DesktopTestStack.Uno ? new[] { "ViewModels", "Models", "Services" } : new[] { "ViewModels", "Data", "Entities", "Repositories" })
            L($"    <Compile Include=\"{app}/{folder}/**/*.cs\" LinkBase=\"App/{folder}\" />");
        L($"    <Compile Include=\"{app}/GlobalUsings.cs\" LinkBase=\"App\" />");
        L("  </ItemGroup>");
        L();
        L("  <ItemGroup>");
        L("    <PackageReference Include=\"MSTest\" Version=\"4.0.2\" />");
        L("    <PackageReference Include=\"CommunityToolkit.Mvvm\" Version=\"8.4.0\" />");
        if (stack == DesktopTestStack.WinUI3)
        {
            var (package, version) = EfProviderInfo.PackageVersion(project.DatabaseDialect);
            L($"    <PackageReference Include=\"{package}\" Version=\"{version}\" />");
            if (project.DatabaseDialect != SqlDialect.Sqlite)
            {
                var (sqlite, sqliteVersion) = EfProviderInfo.PackageVersion(SqlDialect.Sqlite);
                L($"    <PackageReference Include=\"{sqlite}\" Version=\"{sqliteVersion}\" />");
            }
            L("    <PackageReference Include=\"Microsoft.Extensions.Configuration.Json\" Version=\"10.0.12\" />");
        }
        L("  </ItemGroup>");
        L();
        L("</Project>");
        return o.ToString();
    }

    /// <summary> The Uno stand-in for the API: it answers a list, a lookup and the guards with nothing, echoes a create, accepts an update and a delete, remembers every write, and can refuse writes. </summary>
    public static string FakeApi(string testNamespace, string servicesNamespace, bool auth) => $$"""
using System.Net;
using System.Text;
using {{servicesNamespace}};

namespace {{testNamespace}};

/// <summary> One request the view model made: its method, path with query and body. </summary>
public sealed record SentRequest(HttpMethod Method, string Path, string Body);

/// <summary> Stands in for the API: no server, no database. </summary>
public sealed class FakeApi
{
    private readonly Handler _handler = new();

    public FakeApi() => Clients = new ApiClients(new HttpClient(_handler) { BaseAddress = new Uri("http://localhost") }{{(auth ? ", new AuthSession()" : "")}});

    public ApiClients Clients { get; }

    /// <summary> Every create, update and delete the view model sent. </summary>
    public List<SentRequest> Writes => _handler.Requests.Where(r => r.Method != HttpMethod.Get).ToList();

    /// <summary> Makes every later create, update and delete fail with this status. </summary>
    public void FailWrites(HttpStatusCode status) => _handler.FailWith = status;

    /// <summary> Makes every later request fail like a server that cannot be reached. </summary>
    public void Unreachable() => _handler.Down = true;

    private sealed class Handler : HttpMessageHandler
    {
        public List<SentRequest> Requests { get; } = [];
        public HttpStatusCode? FailWith { get; set; }
        public bool Down { get; set; }

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            if (Down)
                throw new HttpRequestException("The server could not be reached.");
            string body = request.Content is null ? "" : await request.Content.ReadAsStringAsync(cancellationToken);
            string path = request.RequestUri!.PathAndQuery;
            Requests.Add(new SentRequest(request.Method, path, body));
            if (request.Method != HttpMethod.Get && FailWith is { } status)
                return new HttpResponseMessage(status);
            if (request.Method == HttpMethod.Get)
                return Json(path.Contains("/search") ? "{\"items\":[],\"page\":1,\"pageSize\":20,\"totalCount\":0,\"totalPages\":1}" : path.Split('?')[0].EndsWith("/guards") ? "{}" : "[]");
            return request.Method == HttpMethod.Post ? Json(body.Length == 0 ? "{}" : body) : new HttpResponseMessage(HttpStatusCode.NoContent);
        }

        private static HttpResponseMessage Json(string text) => new(HttpStatusCode.OK) { Content = new StringContent(text, Encoding.UTF8, "application/json") };
    }
}
""";

    /// <summary> The WinUI3 stand-in for the database: an in-memory SQLite database made from the app's own context, with foreign keys left unchecked so a row can be saved without its parents. </summary>
    public static string TestDb(string testNamespace, string contextNamespace, string contextType) => $$"""
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata;
using {{contextNamespace}};

namespace {{testNamespace}};

/// <summary> The app's own context over an in-memory SQLite database that lives as long as this object. </summary>
public sealed class TestDb : IAsyncDisposable
{
    private readonly SqliteConnection _connection;

    private TestDb(SqliteConnection connection, {{contextType}} context)
    {
        _connection = connection;
        Context = context;
    }

    public {{contextType}} Context { get; }

    public static async Task<TestDb> CreateAsync()
    {
        var connection = new SqliteConnection("Data Source=:memory:;Foreign Keys=False");
        await connection.OpenAsync();
        var context = new {{contextType}}(new DbContextOptionsBuilder<{{contextType}}>().UseSqlite(connection).Options);
        // SQLite does not fill a row version the way SQL Server does: the column the database would fill gets a starting value (the one the sample rows carry)
        string script = context.Database.GenerateCreateScript();
        foreach (var property in context.Model.GetEntityTypes().SelectMany(t => t.GetProperties()).Where(p => p.ClrType == typeof(byte[]) && p.IsConcurrencyToken && p.ValueGenerated != ValueGenerated.Never))
            script = script.Replace($"\"{property.GetColumnName()}\" BLOB NOT NULL", $"\"{property.GetColumnName()}\" BLOB NOT NULL DEFAULT x'0000000000000001'");
        await using (var command = connection.CreateCommand())
        {
            command.CommandText = script;
            await command.ExecuteNonQueryAsync();
        }
        return new TestDb(connection, context);
    }

    public async ValueTask DisposeAsync()
    {
        await Context.DisposeAsync();
        await _connection.DisposeAsync();
    }
}
""";

    /// <summary> The test class of one table. </summary>
    public static string TestClass(DesktopTestStack stack, DesktopTestPlan plan, string testNamespace, IEnumerable<string> usings)
    {
        var o = new StringBuilder();
        void L(string s = "") => o.Append(s).Append('\n');
        bool uno = stack == DesktopTestStack.Uno;
        string vm = plan.ViewModel;
        foreach (string u in usings)
            L($"using {u};");
        L();
        L($"namespace {testNamespace};");
        L();
        L($"/// <summary> The rules of {vm}: what it refuses and what it saves, checked on the app's own view model over {(uno ? "a stand-in for the API" : "an in-memory SQLite database")}. </summary>");
        L("[TestClass]");
        L($"public class {vm}Tests");
        L("{");
        L($"    private const string RowJson = \"\"\"{plan.RowJson}\"\"\";");
        L();
        L($"    private static {plan.Entity} Row() => System.Text.Json.JsonSerializer.Deserialize<{plan.Entity}>(RowJson, TestJson.Options)!;");
        L();
        if (uno)
        {
            L($"    private static async Task<({vm} Model, FakeApi Api)> OpenAsync(bool edit)");
            L("    {");
            L("        var api = new FakeApi();");
            L($"        var model = new {vm}(api.Clients, edit ? Row() : null);");
            L($"        await model.{plan.LoadMethod}();");
            L("        return (model, api);");
            L("    }");
        }
        else
        {
            L($"    private static async Task<({vm} Model, TestDb Db)> OpenAsync(bool edit)");
            L("    {");
            L("        var db = await TestDb.CreateAsync();");
            L("        if (edit)");
            L("        {");
            L("            db.Context.Add(Row());");
            L("            await db.Context.SaveChangesAsync();");
            L("            db.Context.ChangeTracker.Clear();");
            L("        }");
            L($"        var model = new {vm}(db.Context, edit ? Row() : null);");
            L($"        await model.{plan.LoadMethod}();");
            L("        return (model, db);");
            L("    }");
        }
        string open(bool edit) => uno
            ? $"        var (model, api) = await OpenAsync(edit: {(edit ? "true" : "false")});"
            : $"        var (model, db) = await OpenAsync(edit: {(edit ? "true" : "false")});\n        await using var scope = db;";
        string nothingWritten = uno ? "        Assert.IsEmpty(api.Writes);" : "";

        if (plan.RequiredSentence is { } required)
        {
            L();
            L("    [TestMethod]");
            L("    public async Task Refuses_a_new_row_with_nothing_filled_in()");
            L("    {");
            L(open(false));
            L("        Assert.IsFalse(await model.SaveAsync());");
            L($"        Assert.AreEqual(\"{required}\", model.ErrorMessage);");
            if (uno)
                L(nothingWritten);
            else
                L($"        Assert.AreEqual(0, await db.Context.Set<{plan.Entity}>().CountAsync());");
            L("    }");
        }

        L();
        L("    [TestMethod]");
        L("    public async Task Saves_a_row_it_opened_with_every_value_it_had()");
        L("    {");
        L(open(true));
        L("        Assert.IsTrue(await model.SaveAsync(), model.ErrorMessage);");
        if (uno)
        {
            L("        var write = api.Writes.Single();");
            L("        Assert.AreEqual(HttpMethod.Put, write.Method);");
            L($"        Assert.AreEqual(\"{plan.Route}/{plan.KeyText}\", write.Path);");
            L("        Assert.IsNull(TestJson.FirstDifference(write.Body, RowJson));");
        }
        else
        {
            L("        db.Context.ChangeTracker.Clear();");
            L($"        var saved = await db.Context.Set<{plan.Entity}>().SingleAsync();");
            L("        Assert.IsNull(TestJson.FirstDifference(TestJson.Write(saved), RowJson));");
        }
        L("    }");

        foreach (var refusal in plan.Refusals)
        {
            L();
            L("    [TestMethod]");
            L($"    public async Task {refusal.Name}()");
            L("    {");
            L(open(true));
            L($"        {refusal.Statement}");
            L("        Assert.IsFalse(await model.SaveAsync());");
            L($"        Assert.AreEqual(\"{refusal.Sentence.Replace("\\", "\\\\").Replace("\"", "\\\"")}\", model.ErrorMessage);");
            if (uno)
                L(nothingWritten);
            L("    }");
        }

        if (uno)
        {
            L();
            L("    [TestMethod]");
            L("    public async Task Shows_the_sentence_when_the_API_refuses_the_save()");
            L("    {");
            L(open(true));
            L("        api.FailWrites(System.Net.HttpStatusCode.BadRequest);");
            L("        Assert.IsFalse(await model.SaveAsync());");
            L("        Assert.AreEqual(\"Some of the values are not acceptable!\", model.ErrorMessage);");
            L("    }");
            L();
            L("    [TestMethod]");
            L("    public async Task Says_so_when_the_server_cannot_be_reached()");
            L("    {");
            L(open(true));
            L("        api.Unreachable();");
            L("        Assert.IsFalse(await model.SaveAsync());");
            L("        Assert.AreEqual(\"The server could not be reached.\", model.ErrorMessage);");
            L("    }");
        }
        L("}");
        return o.ToString();
    }
}
