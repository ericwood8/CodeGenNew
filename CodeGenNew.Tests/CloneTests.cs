using System.Data;
using CodeGenNew.Core;
using CodeGenNew.TemplateEngine;

namespace CodeGenNew.Tests;

/// <summary> The Clone button on every grid row: which tables get one, the repository method that calls the clone routine on each database, and the endpoint, services and screens. </summary>
[TestClass]
public class CloneTests
{
    // An account: an identity key, a unique text code, a name and who created it.
    private static TableModel Account(SqlDialect dialect = SqlDialect.SqlServer, bool uniqueNumber = false)
    {
        var columns = new List<ColumnModel>
        {
            Sample.Column("AccountId", SqlDbType.Int, primaryKey: true, identity: true, ordinal: 1),
            Sample.Column("Code", SqlDbType.NVarChar, characters: 20, inUniqueIndex: !uniqueNumber, ordinal: 2),
            Sample.Column("Name", SqlDbType.NVarChar, characters: 50, ordinal: 3),
            Sample.Column("CreatedBy", SqlDbType.NVarChar, characters: 30, createUserColumn: true, ordinal: 4)
        };
        if (uniqueNumber)
            columns.Add(Sample.Column("Number", SqlDbType.Int, inUniqueIndex: true, ordinal: 5));
        var t = Sample.Table("Account", columns);
        return new TableModel
        {
            SchemaName = dialect == SqlDialect.PostgreSql ? "public" : t.SchemaName, TableName = t.TableName, QuotedName = t.QuotedName, Dialect = dialect,
            Columns = t.Columns, PrimaryKeyColumns = t.PrimaryKeyColumns, ForeignKeys = t.ForeignKeys, ChildForeignKeys = t.ChildForeignKeys,
            DisplayColumns = t.DisplayColumns, HasReferencedDisplayColumns = t.HasReferencedDisplayColumns, LookupShape = t.LookupShape
        };
    }

    private static ProjectSettings Project(params (string Key, string Value)[] values) =>
        ProjectSettings.FromValues(values.Select(v => new KeyValuePair<string, string>(v.Key, v.Value)).Append(new("ProjectName", "Acme")));

    private static async Task<string> Render(string template, TableModel table, ProjectSettings? project = null)
    {
        var result = await TemplateRunner.RunAsync(Repo.Template(template), table, project ?? Project());
        Assert.IsTrue(result.Success, $"{template}: {string.Join(" | ", result.Errors)}");
        return result.GeneratedText!.Replace("\r\n", "\n");
    }

    // ------------------------------------------------------------------ which tables

    [TestMethod]
    public void A_table_with_a_generated_int_key_can_be_cloned_and_its_unique_text_column_needs_a_new_value()
    {
        var table = Account();

        Assert.IsTrue(CloneShape.CanClone(table, Project()));
        CollectionAssert.AreEqual(new[] { "Code" }, CloneShape.OverrideColumns(table).Select(c => c.Name).ToArray());
        CollectionAssert.AreEqual(new[] { "CreatedBy" }, CloneShape.CreateUserColumns(table).Select(c => c.Name).ToArray());
    }

    [TestMethod]
    public void A_table_the_clone_routine_cannot_give_a_new_key_or_a_new_unique_value_is_not_cloned()
    {
        Assert.IsFalse(CloneShape.CanClone(Sample.CompositeKey(), Project()), "composite key");
        Assert.IsFalse(CloneShape.CanClone(Sample.NaturalKey(), Project()), "a key the person types");
        Assert.IsFalse(CloneShape.CanClone(Account(uniqueNumber: true), Project()), "a unique column that is not text has no suggested value");
        StringAssert.Contains(CloneShape.WhyNot(Account(uniqueNumber: true), Project())!, "not text");
    }

    [TestMethod]
    public void The_project_can_list_tables_that_get_no_clone_button()
    {
        Assert.IsTrue(CloneShape.CanClone(Account(), Project()));
        Assert.IsFalse(CloneShape.CanClone(Account(), Project(("NoCloneTables", "Customer, account"))));
        StringAssert.Contains(CloneShape.WhyNot(Account(), Project(("NoCloneTables", "Account")))!, "NoCloneTables");
    }

    // ------------------------------------------------------------------ the repository method

    [TestMethod]
    public void SQL_Server_reads_the_new_key_from_an_output_parameter()
    {
        string cs = string.Join("\n", CloneCall.RepoMethod(Account(), "Account"));

        Expect.Contains(cs, "public async Task<int> CloneAsync(int id, string? createUser = null)");
        Expect.Contains(cs, "string? uniqueCode = source.Code is null ? null : await SuggestUniqueCode(source.Code.Length > 17 ? source.Code[..17] : source.Code);");
        Expect.Contains(cs, "{ Direction = System.Data.ParameterDirection.Output }");
        Expect.Contains(cs, "\"EXEC [dbo].[Account_Clone] @CopyFromAccountId = @CopyFromAccountId, @NewAccountId = @NewAccountId OUTPUT, @pCreatedBy = @pCreatedBy, @pCode = @pCode\"");
        Expect.Contains(cs, "(\"@pCreatedBy\", createUser ?? \"\")");
        Expect.Contains(cs, "return (int)newKey.Value!;");
    }

    [TestMethod]
    public void PostgreSQL_calls_the_function_with_named_arguments_and_reads_its_value()
    {
        string cs = string.Join("\n", CloneCall.RepoMethod(Account(SqlDialect.PostgreSql), "Account"));

        Expect.Contains(cs, "SELECT \\\"public\\\".\\\"Account_Clone\\\"(\\\"CopyFromAccountId\\\" => @CopyFromAccountId, \\\"pCreatedBy\\\" => @pCreatedBy, \\\"pCode\\\" => @pCode) AS \\\"Value\\\"");
        Expect.Contains(cs, "SqlQueryRaw<int>(");
        Expect.Contains(cs, "new Npgsql.NpgsqlParameter(\"@pCode\", NpgsqlTypes.NpgsqlDbType.Text)");
        Expect.Contains(cs, "return rows.Single();");
    }

    [TestMethod]
    public void MySQL_passes_every_argument_in_the_order_the_procedure_declares_them()
    {
        string cs = string.Join("\n", CloneCall.RepoMethod(Account(SqlDialect.MySql), "Account"));

        Expect.Contains(cs, "\"CALL `Account_Clone`(@CopyFromAccountId, @CreateUser0, @UniqueCode)\"");
        Expect.Contains(cs, "new MySql.Data.MySqlClient.MySqlParameter(\"@CreateUser0\", createUser ?? \"\")");
        Expect.Contains(cs, "return rows.Single();");
    }

    [TestMethod]
    public async Task The_mysql_clone_routine_returns_the_new_key_in_a_column_called_Value()
    {
        string sql = await Render("SP_Clone_v1.tt", Account(SqlDialect.MySql));

        Expect.Contains(sql, "SELECT v_new_key AS `Value`");
    }

    // ------------------------------------------------------------------ the generated files

    [TestMethod]
    public async Task The_repository_and_the_api_offer_clone_only_for_a_table_that_can_be_cloned()
    {
        string repo = await Render("CS_Repo_v1.tt", Account());
        string api = await Render("API_Crud_v1.tt", Account());
        Expect.Contains(repo, "public async Task<int> CloneAsync(int id");
        Expect.Contains(repo, "public async Task<string> SuggestUniqueCode(string desired)");   // the unique text column's suggestion CloneAsync uses
        Expect.Contains(api, "app.MapPost(_apiSubDir + \"/{id:int}/clone\", CloneRow)");
        Expect.Contains(api, "int newId = await repo.CloneAsync(id);");
        Expect.Contains(api, "return Results.Created($\"/api{_apiSubDir}/{newId}\", copy);");

        string noRepo = await Render("CS_Repo_v1.tt", Account(), Project(("NoCloneTables", "Account")));
        string noApi = await Render("API_Crud_v1.tt", Account(), Project(("NoCloneTables", "Account")));
        Expect.DoesNotContain(noRepo, "CloneAsync");
        Expect.DoesNotContain(noApi, "clone");

        Expect.DoesNotContain(await Render("API_Crud_v1.tt", Account(uniqueNumber: true)), "/clone");
    }

    [TestMethod]
    public async Task The_web_services_have_clone_for_a_table_that_can_be_cloned()
    {
        var react = GeneratedFiles.Split(await Render("TSX_Api_v1.tt", Account())).Single().Content;
        var angular = GeneratedFiles.Split(await Render("TS_Service_v1.tt", Account())).Single().Content;

        Expect.Contains(react, "clone: (id: number) => request<Account>(`${apiUrl}/${id}/clone`, { method: 'POST' }),");
        Expect.Contains(angular, "clone(id: number): Observable<Account> {");
        Expect.Contains(angular, "this.http.post<Account>(`${this.apiUrl}/${id}/clone`, null)");
        Expect.DoesNotContain(GeneratedFiles.Split(await Render("TSX_Api_v1.tt", Account(), Project(("NoCloneTables", "Account")))).Single().Content, "clone");
    }

    [TestMethod]
    [DataRow("TSX_Page_v1.tt")]
    [DataRow("TSX_DetailMasterPage_v1.tt")]
    public async Task A_React_grid_has_a_clone_button_that_opens_the_copy_in_the_form(string template)
    {
        string tsx = await Render(template, template.Contains("DetailMaster") ? WithChild(Account()) : Account());

        Expect.Contains(tsx, "<button type=\"button\" onClick={() => void clone(account.accountId!)}>Clone</button>");
        Expect.Contains(tsx, "const copy = await accountApi.clone(id);");
        Expect.Contains(tsx, "edit(copy);");
        Expect.DoesNotContain(await Render(template, template.Contains("DetailMaster") ? WithChild(Account()) : Account(), Project(("NoCloneTables", "Account"))), "clone(");
    }

    [TestMethod]
    [DataRow("TS_Component_v1.tt")]
    [DataRow("TS_DetailMasterComponent_v1.tt")]
    public async Task An_Angular_grid_has_a_clone_button_that_opens_the_copy_in_the_form(string template)
    {
        string files = await Render(template, template.Contains("DetailMaster") ? WithChild(Account()) : Account());

        Expect.Contains(files, "<button (click)=\"clone(account.accountId!)\" class=\"btn-action\">Clone</button>");
        Expect.Contains(files, "this.accountService.clone(id).subscribe({");
        Expect.Contains(files, "this.edit(copy);");
    }

    [TestMethod]
    public async Task The_WinUI3_grid_has_a_clone_button_that_opens_the_copy_in_the_edit_dialog()
    {
        var files = GeneratedFiles.Split(await Render("WinUI3_MasterScreen_v1.tt", Account()));
        string xaml = files.Single(f => f.RelativePath.EndsWith("ListPage.xaml")).Content;
        string page = files.Single(f => f.RelativePath.EndsWith("ListPage.xaml.cs")).Content;
        string vm = files.Single(f => f.RelativePath.EndsWith("ListViewModel.cs")).Content;

        Expect.Contains(xaml, "<Button Content=\"Clone\" Tag=\"{x:Bind Id}\" Click=\"OnCloneClick\" />");
        Expect.Contains(xaml, "Width=\"230\"");                  // room for three buttons in the actions column
        Expect.Contains(page, "int? newId = await ViewModel.CloneAsync(id);");
        Expect.Contains(page, "FindAsync(newId.Value)");
        Expect.Contains(vm, "return await _repo.CloneAsync(id);");

        var plain = GeneratedFiles.Split(await Render("WinUI3_MasterScreen_v1.tt", Account(), Project(("NoCloneTables", "Account"))));
        Expect.DoesNotContain(plain.Single(f => f.RelativePath.EndsWith("ListPage.xaml")).Content, "OnCloneClick");
    }

    private static TableModel WithChild(TableModel t) => new()
    {
        SchemaName = t.SchemaName, TableName = t.TableName, QuotedName = t.QuotedName, Dialect = t.Dialect,
        Columns = t.Columns, PrimaryKeyColumns = t.PrimaryKeyColumns, ForeignKeys = t.ForeignKeys,
        ChildForeignKeys = [Sample.ChildForeignKey("AccountNote", "AccountId", "AccountId", childOwnPrimaryKey: ["AccountNoteId"])],
        DisplayColumns = t.DisplayColumns, HasReferencedDisplayColumns = t.HasReferencedDisplayColumns, LookupShape = t.LookupShape
    };
}
