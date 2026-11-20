using CodeGenNew.Connections;
using CodeGenNew.Core;
using CodeGenNew.Generation;
using CodeGenNew.SchemaIntrospection;
using Microsoft.Data.Sqlite;

namespace CodeGenNew.Tests.Shapes;

/// <summary> The screens of a default-row table (DefaultRowTables): a Make default button on every row that is not the default of its group, with the client calls behind it, in every front end. </summary>
[TestClass]
public class DefaultRowScreenTests
{
    private static async Task<(GenerateReport Report, Dictionary<string, string> Files)> GenerateAsync(string stacks)
    {
        string database = Path.Combine(Path.GetTempPath(), "codegen_default_" + Guid.NewGuid().ToString("N") + ".db");
        string output = Path.Combine(Path.GetTempPath(), "codegen_default_out_" + Guid.NewGuid().ToString("N"));
        try
        {
            using (var connection = new SqliteConnection($"Data Source={database}"))
            {
                connection.Open();
                using var command = connection.CreateCommand();
                command.CommandText = File.ReadAllText(Path.Combine(Repo.Root, "Docs", "Verification", "Tools", "Schemas", "default_rows.sql"));
                command.ExecuteNonQuery();
            }
            var values = new Dictionary<string, string>
            {
                ["ProjectName"] = "Shop", ["DatabaseProvider"] = "Sqlite", ["DatabaseName"] = "shop.db", ["EnumTables"] = "none", ["Stacks"] = stacks, ["Dashboard"] = "false", ["AccessMode"] = "Ef",
                ["DefaultRowTables"] = "CustomerAddress:IsDefault:CustomerId+AddressType", ["Screens"] = "Customer,CustomerAddress", ["AngularVersion"] = "22"
            };
            var request = new ConnectionRequest { Provider = DatabaseProvider.Sqlite, ServerName = "", DatabaseName = database };
            var provider = SchemaProviderFactory.Create(request, Path.Combine(Repo.Root, "SpecialLogicColumns.config"));
            var report = await Repo.GenerateAsync(provider, new GenerateOptions
            {
                Project = ProjectSettings.FromValues(values), Stacks = stacks.Split(","), OutputDirectory = output, DatabaseName = database, Schema = "main"
            });
            var files = Directory.Exists(output)
                ? Directory.GetFiles(output, "*", SearchOption.AllDirectories).ToDictionary(f => Path.GetRelativePath(output, f).Replace('\\', '/'), f => File.ReadAllText(f).Replace("\r\n", "\n"))
                : [];
            return (report, files);
        }
        finally
        {
            SqliteConnection.ClearAllPools();
            File.Delete(database);
            if (Directory.Exists(output))
                Directory.Delete(output, true);
        }
    }

    private static string FileEnding(Dictionary<string, string> files, string ending) => files.Single(f => f.Key.EndsWith(ending)).Value;

    [TestMethod]
    public async Task The_web_front_ends_have_the_button_and_the_call_behind_it()
    {
        var (report, files) = await GenerateAsync("Api,React,Angular,Blazor");
        Assert.IsTrue(report.Success, string.Join("\n", report.Errors));

        Expect.Contains(FileEnding(files, "customeraddressApi.ts"), "makeDefault: (id: number) => request<CustomerAddress>(`${apiUrl}/${id}/make-default`, { method: 'POST' }),");
        string page = FileEnding(files, "CustomerAddressPage.tsx");
        Expect.Contains(page, "await customerAddressApi.makeDefault(id);");
        Expect.Contains(page, "{!customerAddress.isDefault && <button type=\"button\" onClick={() => void makeDefault(customerAddress.customerAddressId!)}>Make default</button>}");

        Expect.Contains(FileEnding(files, "customeraddress.service.ts"), "makeDefault(id: number): Observable<CustomerAddress>");
        Expect.Contains(FileEnding(files, "customeraddress.component.ts"), "this.service.makeDefault(id).subscribe({ next: () => { if (this.selectedRow?.customerAddressId === id)");
        string html = FileEnding(files, "customeraddress.component.html");
        Expect.Contains(html, "@if (!customerAddress.isDefault) {");
        Expect.Contains(html, "<button (click)=\"makeDefault(customerAddress.customerAddressId!)\" class=\"btn-action\">Make default</button>");

        Expect.Contains(FileEnding(files, "CustomerAddressClient.cs"), "public async Task<CustomerAddress> MakeDefaultAsync(int id)");
        string razor = FileEnding(files, "CustomerAddressPage.razor");
        Expect.Contains(razor, "<button type=\"button\" @onclick=\"() => MakeDefaultAsync(item)\">Make default</button>");
        Expect.Contains(razor, "await Client.MakeDefaultAsync(item.CustomerAddressId);");

        Assert.IsFalse(files.Single(f => f.Key.EndsWith("customer.service.ts")).Value.Contains("makeDefault"), "a table that is not a default-row table has no such call");
    }

    [TestMethod]
    public async Task The_desktop_front_ends_have_the_button_and_the_move_of_the_mark()
    {
        var (report, files) = await GenerateAsync("Api,WinUI3,Uno");
        Assert.IsTrue(report.Success, string.Join("\n", report.Errors));

        foreach (string stack in new[] { "App", "Uno" })
        {
            string xaml = files.Single(f => f.Key.EndsWith(stack + "/Views/CustomerAddressListPage.xaml", StringComparison.Ordinal) || f.Key.EndsWith("." + stack + "/Views/CustomerAddressListPage.xaml", StringComparison.Ordinal)).Value;
            Expect.Contains(xaml, "Content=\"Make default\"");
            Expect.Contains(xaml, "Click=\"OnMakeDefaultClick\" Visibility=\"{x:Bind MakeDefaultVisibility}\"");
            string model = files.Single(f => f.Key.EndsWith("." + stack + "/ViewModels/CustomerAddressListViewModel.cs", StringComparison.Ordinal)).Value;
            Expect.Contains(model, "MakeDefaultVisibility = !e.IsDefault ? Visibility.Visible : Visibility.Collapsed");
            Expect.Contains(model, "public async Task MakeDefaultAsync(int id)");
            Assert.IsTrue(files.Single(f => f.Key.EndsWith("." + stack + "/Views/CustomerAddressListPage.xaml.cs", StringComparison.Ordinal)).Value
                .Contains("await ViewModel.MakeDefaultAsync((int)((Button)sender).Tag);"));
        }
        string winUi = files.Single(f => f.Key.EndsWith(".App/ViewModels/CustomerAddressListViewModel.cs", StringComparison.Ordinal)).Value;
        Expect.Contains(winUi, "await using var transaction = await _context.Database.BeginTransactionAsync();");
        Expect.Contains(winUi, "x.CustomerAddressId != id && x.CustomerId == row.CustomerId && x.AddressType == row.AddressType && x.IsDefault");
        Expect.Contains(files.Single(f => f.Key.EndsWith(".Uno/ViewModels/CustomerAddressListViewModel.cs", StringComparison.Ordinal)).Value, "await Client.MakeDefaultAsync(id);");
    }

    [TestMethod]
    public async Task The_edit_form_shows_the_flag_as_text_and_has_the_button()
    {
        var (report, files) = await GenerateAsync("Api,React,Angular,Blazor,WinUI3,Uno");
        Assert.IsTrue(report.Success, string.Join("\n", report.Errors));

        string page = FileEnding(files, "CustomerAddressPage.tsx");
        Expect.Contains(page, "<span id=\"customerAddressIsDefault\">{selectedRow.isDefault ? 'Yes' : 'No'}</span>");
        Assert.IsFalse(page.Contains("type=\"checkbox\""), "no check box for the flag");
        Expect.Contains(page, "{selectedRow.customerAddressId && !selectedRow.isDefault && <button type=\"button\" onClick={() => void makeDefault(selectedRow.customerAddressId!)}>Make default</button>}");
        Expect.Contains(page, "setSelectedRow((current) => current && current.customerAddressId === id ? { ...current, isDefault: true } : current);");

        string html = FileEnding(files, "customeraddress.component.html");
        Expect.Contains(html, "{{ selectedRow.isDefault ? 'Yes' : 'No' }}");
        Expect.Contains(html, "(click)=\"makeDefault(selectedRow.customerAddressId!)\">Make default</button>");

        string razor = FileEnding(files, "CustomerAddressPage.razor");
        Expect.Contains(razor, "@(row.IsDefault ? \"Yes\" : \"No\")");
        Expect.Contains(razor, "@if (!isNew && !row!.IsDefault)");
        Expect.Contains(razor, "if (row is not null && !isNew && row.CustomerAddressId == item.CustomerAddressId) row.IsDefault = true;");

        foreach (string stack in new[] { "App", "Uno" })
        {
            Expect.Contains(files.Single(f => f.Key.EndsWith("." + stack + "/Views/CustomerAddressDetailDialog.xaml", StringComparison.Ordinal)).Value,
                "<Button Content=\"Make default\" Click=\"OnMakeDefaultClick\" Visibility=\"{x:Bind ViewModel.MakeDefaultVisibility, Mode=OneWay}\"");
            string model = files.Single(f => f.Key.EndsWith("." + stack + "/ViewModels/CustomerAddressDetailViewModel.cs", StringComparison.Ordinal)).Value;
            Expect.Contains(model, "private void RefreshDefaultButton()");
            Expect.Contains(model, "public string YesNo(bool? value) => value == true ? \"Yes\" : \"No\";");
            Expect.Contains(files.Single(f => f.Key.EndsWith("." + stack + "/Views/CustomerAddressDetailDialog.xaml", StringComparison.Ordinal)).Value, "Text=\"{x:Bind ViewModel.YesNo(ViewModel.IsDefault), Mode=OneWay}\"");
            Expect.Contains(model, "public async Task MakeDefaultAsync()");
            Assert.IsFalse(model.Contains("entity.IsDefault = entity.IsDefault"), "the flag is not saved from the form");
        }
        Expect.Contains(files.Single(f => f.Key.EndsWith(".App/ViewModels/CustomerAddressDetailViewModel.cs", StringComparison.Ordinal)).Value,
            "entity.IsDefault = !await _context.Set<CustomerAddress>().AnyAsync(x => x.IsDefault && x.CustomerId == entity.CustomerId && x.AddressType == entity.AddressType);");

        // the default row of a group moved to another group is refused in the dialog (it would leave its group without a default, or break the unique index of the flag and end the app);
        // the Uno dialog and the web stacks get the same two sentences from the API
        string winui = files.Single(f => f.Key.EndsWith(".App/ViewModels/CustomerAddressDetailViewModel.cs", StringComparison.Ordinal)).Value;
        Expect.Contains(winui, "else if (entity.IsDefault)");
        Expect.Contains(winui, "var defaultWas = await _context.Set<CustomerAddress>().AsNoTracking().Where(x => x.CustomerAddressId == entity.CustomerAddressId).Select(x => new { x.CustomerId, x.AddressType }).FirstAsync();");
        Expect.Contains(winui, "if (defaultWas.CustomerId != entity.CustomerId || defaultWas.AddressType != entity.AddressType)");
        Expect.Contains(winui, "ErrorMessage = \"" + DefaultRowScreens.LeavesGroupText + "\";");
        Expect.Contains(winui, "ErrorMessage = \"" + DefaultRowScreens.GroupTakenText + "\";");
        Assert.DoesNotContain(DefaultRowScreens.GroupTakenText, files.Single(f => f.Key.EndsWith(".Uno/ViewModels/CustomerAddressDetailViewModel.cs", StringComparison.Ordinal)).Value);
    }

    [TestMethod]
    public async Task Every_api_refuses_to_move_a_default_row_and_the_clients_show_the_sentence()
    {
        var (report, files) = await GenerateAsync("Api,Python,Rust,Blazor,Uno,React");
        Assert.IsTrue(report.Success, string.Join("\n", report.Errors));

        // the React page shows the detail of the 409 of a refused save (it has no other 409 to tell apart)
        string page = FileEnding(files, "CustomerAddressPage.tsx");
        Expect.Contains(page, "const problemDetail = (error: unknown): string | undefined => {");
        Expect.Contains(page, "err.status === 409) { setError(problemDetail(err) ?? 'A row with this value already exists!'); }");

        string dotnet = files.Single(f => f.Value.Contains("public static async Task<IResult?> CheckDefaultAsync(")).Value;
        Expect.Contains(dotnet, "if (stored is not null && stored.IsDefault && (stored.CustomerId != row.CustomerId || stored.AddressType != row.AddressType))");
        Expect.Contains(dotnet, "RowShapeProblems.Problem(409, \"" + DefaultRowScreens.MovedType + "\", \"" + DefaultRowScreens.MovedTitle + "\", \"" + DefaultRowScreens.LeavesGroupText + "\")");
        Expect.Contains(dotnet, "RowShapeProblems.Problem(409, \"" + DefaultRowScreens.MovedType + "\", \"" + DefaultRowScreens.MovedTitle + "\", \"" + DefaultRowScreens.GroupTakenText + "\")");

        string python = FileEnding(files, "app/routes/customer_address.py");
        Expect.Contains(python, "if stored is not None and stored[0] and any(stored[position + 1] != value for position, value in enumerate(groups.values())):");
        Expect.Contains(python, "raise ApiError(409, \"" + DefaultRowScreens.MovedTitle + "\", problem_type=\"" + DefaultRowScreens.MovedType + "\", detail=\"" + DefaultRowScreens.LeavesGroupText + "\")");
        Expect.Contains(python, "detail=\"" + DefaultRowScreens.GroupTakenText + "\")");

        string rust = FileEnding(files, "src/routes/customer_address.rs");
        Expect.Contains(rust, "if stored.is_default && (stored.customer_id != row.customer_id || stored.address_type != row.address_type) {");
        Expect.Contains(rust, "ApiError::Rejected(409, \"" + DefaultRowScreens.MovedType + "\", \"" + DefaultRowScreens.MovedTitle + "\", \"" + DefaultRowScreens.LeavesGroupText + "\".into())");
        Expect.Contains(rust, "\"" + DefaultRowScreens.GroupTakenText + "\".into())");

        // the Blazor and Uno clients read the sentence (Angular shows the detail of any 409, React above); a project without default rows has no such line
        foreach (string template in new[] { "BLZ_EssentialSupport_v1.tt", "UNO_EssentialSupport_v1.tt" })
        {
            foreach (bool listed in new[] { true, false })
            {
                var project = ProjectSettings.FromValues(new Dictionary<string, string> { ["ProjectName"] = "Shop", ["DefaultRowTables"] = listed ? "CustomerAddress:IsDefault:CustomerId+AddressType" : "" });
                var result = await Repo.Cache.RunAsync(Repo.Template(template), project);
                Assert.IsTrue(result.Success, $"{template}: {string.Join(" | ", result.Errors)}");
                string support = result.GeneratedText!.ReplaceLineEndings("\n");
                string line = "Message is \"" + DefaultRowScreens.GroupTakenText + "\" or \"" + DefaultRowScreens.LeavesGroupText + "\" => Message";
                if (listed)
                {
                    Expect.Contains(support, line);
                    Expect.Contains(support, "await ProblemDetailAsync(response) is { } refusal");
                }
                else
                {
                    Assert.DoesNotContain(line, support);
                }
            }
        }
    }

    [TestMethod]
    public async Task The_python_and_rust_apis_keep_one_default_per_group()
    {
        var (report, files) = await GenerateAsync("Api,Python,Rust");
        Assert.IsTrue(report.Success, string.Join("\n", report.Errors));

        string python = FileEnding(files, "app/routes/customer_address.py");
        Expect.Contains(python, "def settle_default(session: Session, row_id, groups: dict, wants_default: bool) -> bool:");
        Expect.Contains(python, "row.is_default = settle_default(session, None, {\"customer_id\": body.customer_id, \"address_type\": body.address_type}, body.is_default)");
        Expect.Contains(python, "promote_default(session, row, {\"customer_id\": row.customer_id, \"address_type\": row.address_type})");
        Expect.Contains(python, "@router.post(\"/api/customeraddresses/{id}/make-default\", response_model=CustomerAddressSchema)");

        string rust = FileEnding(files, "src/routes/customer_address.rs");
        Expect.Contains(rust, "async fn settle_default(pool: &Pool, row_id: Option<i32>, row: &mut CustomerAddress) -> ApiResult<bool> {");
        Expect.Contains(rust, "let moves_default = settle_default(&pool, None, &mut row).await?;");
        Expect.Contains(rust, "promote_default(&pool, id).await?;");
        Expect.Contains(rust, ".route(\"/api/customeraddresses/{id}/make-default\", post(make_default))");
    }
}
