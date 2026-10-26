using CodeGenNew.Connections;
using CodeGenNew.Core;
using CodeGenNew.Generation;
using CodeGenNew.SchemaIntrospection;
using Microsoft.Data.Sqlite;

namespace CodeGenNew.Tests;

/// <summary> ChangeGates and GuardedColumns on WinUI3, which reads the database itself: the gates class, the dialogs that call it and the guarded fields. </summary>
[TestClass]
public class WinUI3GatesTests
{
    private const string Gate = "SalesInvoice.InvoiceStatusId; to 2,3; when Customer.CustomerStatusId=2; unless Customer.IgnoreBadStatus=1; start 1";
    private const string Guard = "Customer.IgnoreBadStatus; by Employee.IsManager; stampBy IgnoreBadStatusBy; stampOn IgnoreBadStatusOn";

    private static string CreateDatabase()
    {
        string path = Path.Combine(Path.GetTempPath(), "codegen_w3g_" + Guid.NewGuid().ToString("N") + ".db");
        using var connection = new SqliteConnection($"Data Source={path}");
        connection.Open();
        foreach (string statement in new[]
        {
            "CREATE TABLE Employee (EmployeeId INTEGER PRIMARY KEY, FullName VARCHAR(40) NOT NULL, Login VARCHAR(30) NOT NULL, PasswordHash VARCHAR(200) NOT NULL, IsManager BOOLEAN NOT NULL, IsAdministrator BOOLEAN NOT NULL)",
            "CREATE TABLE Customer (CustomerId INTEGER PRIMARY KEY, CustomerName VARCHAR(40) NOT NULL, CustomerStatusId INTEGER NOT NULL, IgnoreBadStatus BOOLEAN NOT NULL, IgnoreBadStatusBy VARCHAR(30) NULL, IgnoreBadStatusOn DATETIME NULL)",
            "CREATE TABLE SalesInvoice (SalesInvoiceId INTEGER PRIMARY KEY, CustomerId INTEGER NOT NULL REFERENCES Customer (CustomerId), InvoiceStatusId INTEGER NOT NULL, IsPrinted BOOLEAN NOT NULL, InvoiceDate DATE NOT NULL, Notes VARCHAR(200) NULL)"
        })
        {
            using var command = connection.CreateCommand();
            command.CommandText = statement;
            command.ExecuteNonQuery();
        }
        return path;
    }

    private static async Task GenerateAsync(Dictionary<string, string> extra, Func<Func<string, string>, List<string>, Task> check)
    {
        string database = CreateDatabase();
        string output = Path.Combine(Path.GetTempPath(), "codegen_w3g_out_" + Guid.NewGuid().ToString("N"));
        try
        {
            var values = new Dictionary<string, string>
            {
                ["ProjectName"] = "Shop", ["DatabaseProvider"] = "Sqlite", ["DatabaseName"] = "shop.db", ["EnumTables"] = "none", ["Stacks"] = "WinUI3", ["Dashboard"] = "false",
                ["ApiAuth"] = "true", ["UserTable"] = "Employee", ["UserNameColumn"] = "Login", ["PasswordHashColumn"] = "PasswordHash", ["UserRoleColumn"] = "IsManager"
            };
            foreach (var (key, value) in extra)
                values[key] = value;
            var request = new ConnectionRequest { Provider = DatabaseProvider.Sqlite, ServerName = "", DatabaseName = database };
            var provider = SchemaProviderFactory.Create(request, Path.Combine(Repo.Root, "SpecialLogicColumns.config"));
            var report = await Repo.GenerateAsync(provider, new GenerateOptions
            {
                Project = ProjectSettings.FromValues(values), Stacks = ["WinUI3"], OutputDirectory = output, DatabaseName = database, Schema = "main", Essentials = true
            });
            Assert.IsTrue(report.Success, string.Join(" | ", report.Errors));
            var files = Directory.GetFiles(output, "*", SearchOption.AllDirectories).Select(f => Path.GetRelativePath(output, f).Replace('\\', '/')).ToList();
            await check(relative => File.ReadAllText(Path.Combine(output, relative)).Replace("\r\n", "\n"), files);
        }
        finally
        {
            SqliteConnection.ClearAllPools();
            File.Delete(database);
            if (Directory.Exists(output))
                Directory.Delete(output, recursive: true);
        }
    }

    [TestMethod]
    public void The_gates_template_is_in_the_plan_only_with_a_rule()
    {
        var with = ProjectSettings.FromValues(new Dictionary<string, string>() { ["ProjectName"] = "Shop", ["ApiAuth"] = "true", ["ChangeGates"] = Gate });
        var without = ProjectSettings.FromValues(new Dictionary<string, string>() { ["ProjectName"] = "Shop", ["ApiAuth"] = "true" });

        CollectionAssert.Contains(with.ImpliedPlanTemplates.ToList(), "WinUI3_Gates");
        CollectionAssert.DoesNotContain(without.ImpliedPlanTemplates.ToList(), "WinUI3_Gates");
    }

    [TestMethod]
    public async Task The_app_checks_a_row_with_the_gates_class_before_it_saves_and_names_the_person_by_the_windows_sign_in()
    {
        await GenerateAsync(new() { ["ChangeGates"] = Gate, ["GuardedColumns"] = Guard }, (read, files) =>
        {
            string gates = files.Single(f => f.EndsWith("Repositories/Gates.cs")) is var path ? read(path) : "";
            Expect.Contains(gates, "public static string? Name { get; set; } = Environment.UserName;");
            Expect.Contains(gates, "public static async Task<string?> CheckAsync(ShopContext context, Customer row, bool isNew)");
            Expect.Contains(gates, "return \"A new Sales Invoice always starts with Invoice Status = 1.\";");
            Expect.Contains(gates, "context.Set<Employee>().AsNoTracking().Where(u => u.Login == name)");
            Expect.DoesNotContain(gates, "ClaimsPrincipal");
            Expect.DoesNotContain(gates, "Results.");

            string model = read("Shop.App/ViewModels/CustomerDetailMasterViewModel.cs");
            Expect.Contains(model, "IgnoreBadStatusAllowed = await CustomerGates.MayIgnoreBadStatusAsync(_context);");
            Expect.Contains(model, "await CustomerGates.CheckAsync(_context, entity, _editing is null) is { } refusal");
            Expect.Contains(read("Shop.App/ViewModels/SalesInvoiceDetailViewModel.cs"), "await SalesInvoiceGates.CheckAsync(_context, entity, _editing is null)");
            Expect.Contains(read("Shop.App/ViewModels/CustomerListViewModel.cs"), "await CustomerGates.AfterCloneAsync(_context, copyId)");

            string dialog = read("Shop.App/Views/CustomerDetailMasterDialog.xaml");
            Expect.Contains(dialog, "<ContentControl HorizontalContentAlignment=\"Stretch\" IsEnabled=\"{x:Bind ViewModel.IgnoreBadStatusAllowed, Mode=OneWay}\">");
            return Task.CompletedTask;
        });
    }

    [TestMethod]
    public async Task Without_a_rule_the_app_has_no_gates_class_and_no_disabled_field()
    {
        await GenerateAsync([], (read, files) =>
        {
            Assert.IsFalse(files.Any(f => f.EndsWith("Repositories/Gates.cs")));
            Expect.DoesNotContain(read("Shop.App/ViewModels/CustomerDetailMasterViewModel.cs"), "Gates");
            Expect.DoesNotContain(read("Shop.App/Views/CustomerDetailMasterDialog.xaml"), "ContentControl");
            return Task.CompletedTask;
        });
    }

    [TestMethod]
    public void The_API_and_the_app_write_the_same_checks_and_differ_only_in_how_a_refusal_and_the_caller_are_written()
    {
        var api = new ApiGateFlavor();
        var desktop = new DesktopGateFlavor();

        Assert.AreEqual("IResult?", api.ResultType);
        Assert.AreEqual("string?", desktop.ResultType);
        Assert.AreEqual("", desktop.CallerParameter);
        Assert.AreEqual(", ClaimsPrincipal caller", api.CallerParameter);
        Assert.AreEqual("DesktopCaller.Name", desktop.CallerName);
    }
}
