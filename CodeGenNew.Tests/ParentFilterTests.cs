using System.Data;
using CodeGenNew.Core;
using CodeGenNew.TemplateEngine;

namespace CodeGenNew.Tests;

/// <summary> The key filters of a list: one exact-match filter per whole-number foreign key, in the search routines of the three databases, the repository, the LINQ query, the API and a drop-down of the
/// parent's rows on the Angular, React, Blazor and WinUI3 screens. </summary>
[TestClass]
public class ParentFilterTests
{
    private static ProjectSettings Project(params (string Key, string Value)[] values) =>
        ProjectSettings.FromValues(values.Select(v => new KeyValuePair<string, string>(v.Key, v.Value)).Append(new("ProjectName", "Acme")));

    private static TableModel Invoice(SqlDialect dialect = SqlDialect.SqlServer) => Sample.Table("SalesInvoice",
    [
        Sample.Column("SalesInvoiceId", SqlDbType.Int, primaryKey: true, identity: true, ordinal: 1),
        Sample.Column("CustomerId", SqlDbType.Int, ordinal: 2),
        Sample.Column("InvoiceStatusId", SqlDbType.TinyInt, nullable: true, ordinal: 3),
        Sample.Column("CustomerPO", SqlDbType.NVarChar, characters: 30, ordinal: 4),
        Sample.Column("InvoiceDate", SqlDbType.Date, ordinal: 5),
        Sample.Column("CreateUserId", SqlDbType.Int, createUserColumn: true, nullable: true, ordinal: 6)
    ],
    [
        Sample.ForeignKey("CustomerId", "Customer", "CustomerId", "CustomerName"),
        Sample.ForeignKey("InvoiceStatusId", "InvoiceStatus", "InvoiceStatusId", "Description"),
        Sample.ForeignKey("CreateUserId", "Employee", "EmployeeId", "Name")
    ], dialect: dialect);

    private static async Task<string> Render(string template, TableModel table, ProjectSettings project, string? file = null)
    {
        var result = await Repo.Cache.RunAsync(Repo.Template(template), table, project);
        Assert.IsTrue(result.Success, string.Join(" | ", result.Errors));
        if (file is null || !result.GeneratedText!.Contains("@@@FILE"))
            return result.GeneratedText!.ReplaceLineEndings("\n");
        return GeneratedFiles.Split(result.GeneratedText!).First(f => Path.GetFileName(f.RelativePath) == file).Content.ReplaceLineEndings("\n");
    }

    [TestMethod]
    public void The_key_filters_are_the_whole_number_foreign_keys_but_not_the_keys_or_the_audit_columns()
    {
        var keys = Invoice().SearchKeyColumns.Select(c => c.Name).ToList();

        CollectionAssert.AreEqual(new[] { "CustomerId", "InvoiceStatusId" }, keys);
    }

    [TestMethod]
    public void A_table_gets_at_most_four_key_filters()
    {
        var columns = new List<ColumnModel> { Sample.Column("RowId", SqlDbType.Int, primaryKey: true, identity: true, ordinal: 1) };
        var keys = new List<ForeignKeyModel>();
        for (int i = 1; i <= 6; i++)
        {
            columns.Add(Sample.Column($"Parent{i}Id", SqlDbType.Int, ordinal: i + 1));
            keys.Add(Sample.ForeignKey($"Parent{i}Id", $"Parent{i}", $"Parent{i}Id", "Name"));
        }

        Assert.HasCount(TableModel.MaxSearchKeys, Sample.Table("Wide", columns, keys).SearchKeyColumns);
    }

    [TestMethod]
    public async Task The_sql_server_routines_take_the_keys_after_the_sort_and_filter_on_the_exact_value()
    {
        string sql = await Render("SP_Search_v1.tt", Invoice(), Project());

        Expect.Contains(sql, "@SortDescending BIT = 0,\n@pCustomerId int = NULL,\n@pInvoiceStatusId tinyint = NULL\n)");
        Expect.Contains(sql, "(@pCustomerId IS NULL OR [CustomerId] = @pCustomerId)");
        Expect.Contains(sql, "(@pInvoiceStatusId IS NULL OR [InvoiceStatusId] = @pInvoiceStatusId)");
        Expect.Contains(sql, "[SalesInvoice_SearchCount]\n(\n@pCustomerPO nvarchar(30) = NULL,\n@pCustomerId int = NULL,\n@pInvoiceStatusId tinyint = NULL\n)");
    }

    [TestMethod]
    public async Task The_postgres_and_mysql_routines_take_the_keys_too()
    {
        string pg = await Render("SP_Search_v1.tt", Invoice(SqlDialect.PostgreSql), Project());
        string my = await Render("SP_Search_v1.tt", Invoice(SqlDialect.MySql), Project());

        Expect.Contains(pg, "\"SortDescending\" boolean DEFAULT false,\n\"pCustomerId\" int DEFAULT NULL");
        Expect.Contains(pg, "(\"pCustomerId\" IS NULL OR \"CustomerId\" = \"pCustomerId\")");
        Expect.Contains(pg, "DROP FUNCTION IF EXISTS \"dbo\".\"SalesInvoice_SearchCount\"(");
        Expect.Contains(my, "`pCustomerId`");
        Expect.Contains(my, "(`pCustomerId` IS NULL OR `CustomerId` = `pCustomerId`)");
    }

    [TestMethod]
    public async Task The_api_repository_and_query_pass_the_keys()
    {
        var table = Invoice();
        string api = await Render("API_Search_v1.tt", table, Project());
        string repo = await Render("CS_Repo_v1.tt", table, Project());

        Expect.Contains(api, "[FromQuery] int? customerId,");
        Expect.Contains(api, "[FromQuery] byte? invoiceStatusId,");
        Expect.Contains(api, "EXEC [dbo].[SalesInvoice_Search] @pCustomerPO, @PageNumber, @PageSize, @SortColumn, @SortDescending, @pCustomerId, @pInvoiceStatusId");
        Expect.Contains(api, "EXEC [dbo].[SalesInvoice_SearchCount] @pCustomerPO, @pCustomerId, @pInvoiceStatusId");
        Expect.Contains(api, "new Microsoft.Data.SqlClient.SqlParameter(\"@pCustomerId\", (object?)customerId ?? DBNull.Value)".Replace("Microsoft.Data.SqlClient.", ""));
        Expect.Contains(repo, "bool sortDescending = false, int? customerId = null, byte? invoiceStatusId = null)");

        var ef = Project(("AccessMode", "Ef"));
        string query = await Render("CS_SearchQuery_v1.tt", table, ef);
        string efApi = await Render("API_Search_v1.tt", table, ef);

        Expect.Contains(query, "string? customerPO = null, int? customerId = null, byte? invoiceStatusId = null)");
        Expect.Contains(query, "if (customerId is { } customerIdValue)\n            query = query.Where(t => t.CustomerId == customerIdValue);");
        Expect.Contains(efApi, "SalesInvoiceSearchQuery.Filter(context.Set<SalesInvoice>().AsNoTracking(), customerPO, customerId, invoiceStatusId);");
    }

    [TestMethod]
    public async Task The_angular_page_filters_by_a_parent_drop_down()
    {
        string html = await Render("TS_Component_v1.tt", Invoice(), Project(), "salesinvoice.component.html");
        string ts = await Render("TS_Component_v1.tt", Invoice(), Project(), "salesinvoice.component.ts");

        Expect.Contains(html, "<select class=\"form-control\" aria-label=\"Filter by Customer\" [(ngModel)]=\"filters.customerId\" (ngModelChange)=\"search()\">");
        Expect.Contains(html, "<option value=\"\">All Customers</option>");
        Expect.Contains(html, "<option *ngFor=\"let p of customers\" [value]=\"p.id\">{{p.name}}</option>");
        Expect.Contains(ts, "override filters = { customerPO: '', customerId: '', invoiceStatusId: '' };");
    }

    [TestMethod]
    public async Task The_react_page_filters_by_a_parent_drop_down_and_sends_the_key()
    {
        string page = await Render("TSX_Page_v1.tt", Invoice(), Project(), "SalesInvoicePage.tsx");
        string api = await Render("TSX_Api_v1.tt", Invoice(), Project());

        Expect.Contains(page, "useState({ customerPO: '', customerId: '', invoiceStatusId: '' })");
        Expect.Contains(page, "<select aria-label=\"Filter by Customer\" value={filters.customerId} onChange={(event) => { const next = { ...filters, customerId: event.target.value }; setFilters(next); load(1, next); }}>");
        Expect.Contains(page, "<option value=\"\">All Customers</option>");
        Expect.Contains(api, "filters: { customerPO?: string; customerId?: string; invoiceStatusId?: string } = {}");
        Expect.Contains(api, "if (filters.customerId) params.set('customerId', filters.customerId);");
    }

    [TestMethod]
    public async Task The_blazor_page_filters_by_a_parent_drop_down()
    {
        string page = await Render("BLZ_Page_v1.tt", Invoice(), Project(), "SalesInvoicePage.razor");

        Expect.Contains(page, "<select aria-label=\"Filter by Customer\" @bind=\"filterCustomerId\" @bind:after=\"SearchAsync\"><option value=\"\">All Customers</option>");
        Expect.Contains(page, "private string? filterCustomerId;");
        Expect.Contains(page, "[\"customerId\"] = filterCustomerId,");
        Expect.Contains(page, "filterCustomerId = null;");
    }

    [TestMethod]
    public async Task The_winui3_page_filters_by_a_parent_drop_down_and_passes_the_key_to_the_repository()
    {
        var project = Project();
        string xaml = await Render("WinUI3_MasterScreen_v1.tt", Invoice(), project, "SalesInvoiceListPage.xaml");
        string vm = await Render("WinUI3_MasterScreen_v1.tt", Invoice(), project, "SalesInvoiceListViewModel.cs");

        Expect.Contains(xaml, "AutomationProperties.Name=\"Filter by Customer\" ItemsSource=\"{x:Bind ViewModel.CustomerIdChoices}\"");
        Expect.Contains(xaml, "SelectedItem=\"{x:Bind ViewModel.CustomerIdFilter, Mode=TwoWay}\" SelectionChanged=\"OnKeyFilterChanged\"");
        Expect.Contains(vm, "public sealed record SalesInvoiceKeyChoice(object? Id, string Name);");
        Expect.Contains(vm, "customerId: CustomerIdFilter?.Id is null ? null : Convert.ToInt32(CustomerIdFilter.Id), invoiceStatusId: InvoiceStatusIdFilter?.Id is null ? null : Convert.ToByte(InvoiceStatusIdFilter.Id), pageNumber: PageNumber");
        Expect.Contains(vm, "\"All Customers\"");
    }

    [TestMethod]
    public async Task The_python_search_route_takes_the_keys_and_matches_them_exactly()
    {
        string py = await Render("PY_Routes_v1.tt", Invoice(SqlDialect.PostgreSql), Project(), "sales_invoice.py");

        Expect.Contains(py, "customer_id: int | None = Query(None, alias=\"customerId\"),");
        Expect.Contains(py, "invoice_status_id: int | None = Query(None, alias=\"invoiceStatusId\"),");
        Expect.Contains(py, "    if customer_id is not None:\n        statement = statement.where(SalesInvoice.customer_id == customer_id)");
    }

    [TestMethod]
    public async Task The_rust_search_query_takes_the_keys_and_binds_them()
    {
        string rs = await Render("RS_Repo_v1.tt", Invoice(SqlDialect.PostgreSql), Project(), "sales_invoice.rs");

        Expect.Contains(rs, "#[serde(rename = \"customerId\")]\n    pub customer_id: Option<i64>,");
        Expect.Contains(rs, "if let Some(value) = query.customer_id {\n        builder.push(r#\" AND t.\"CustomerId\" = \"#).push_bind(value);\n    }");
    }

    private static TableModel Shipment(SqlDialect dialect = SqlDialect.SqlServer) => Sample.Table("Shipment",
    [
        Sample.Column("ShipmentId", SqlDbType.Int, primaryKey: true, identity: true, ordinal: 1),
        Sample.Column("CountryCode", SqlDbType.NVarChar, characters: 2, ordinal: 2),
        Sample.Column("RegionId", SqlDbType.UniqueIdentifier, nullable: true, ordinal: 3),
        Sample.Column("Reference", SqlDbType.NVarChar, characters: 30, ordinal: 4)
    ],
    [
        Sample.ForeignKey("CountryCode", "Country", "Code", "Name"),
        Sample.ForeignKey("RegionId", "Region", "RegionId", "Name")
    ], dialect: dialect);

    [TestMethod]
    public void A_text_or_uniqueidentifier_foreign_key_is_a_key_filter_and_a_text_key_is_no_search_box()
    {
        var table = Shipment();

        CollectionAssert.AreEqual(new[] { "CountryCode", "RegionId" }, table.SearchKeyColumns.Select(c => c.Name).ToList());
        CollectionAssert.AreEqual(new[] { "Reference" }, table.SearchableColumns.Select(c => c.Name).ToList());
    }

    [TestMethod]
    public void A_text_key_without_a_drop_down_stays_a_search_box_that_matches_exactly()
    {
        var withDropDown = FormModel.For(Shipment(), Project());
        var oneBox = FormModel.For(Shipment(), Project(("ListProtocol", "Search")));

        CollectionAssert.AreEqual(new[] { "CountryCode" }, withDropDown.SearchKeys.Select(k => k.Column.Name).Where(n => n == "CountryCode").ToList());
        Assert.IsEmpty(withDropDown.TextKeyBoxes);
        CollectionAssert.AreEqual(new[] { "CountryCode" }, oneBox.TextKeyBoxes.Select(c => c.Name).ToList());
        CollectionAssert.AreEqual(new[] { "CountryCode", "Reference" }, oneBox.SearchFields.Select(f => f.Name).ToList());
        CollectionAssert.AreEqual(new[] { "Reference" }, withDropDown.SearchFields.Select(f => f.Name).ToList());
    }

    [TestMethod]
    public async Task The_routines_and_the_api_filter_a_text_and_a_uniqueidentifier_key_exactly()
    {
        var table = Shipment();
        string sql = await Render("SP_Search_v1.tt", table, Project());
        string api = await Render("API_Search_v1.tt", table, Project());
        string query = await Render("CS_SearchQuery_v1.tt", table, Project(("AccessMode", "Ef")));
        string pg = await Render("API_Search_v1.tt", Shipment(SqlDialect.PostgreSql), Project());

        Expect.Contains(sql, "@pCountryCode nvarchar(2) = NULL,\n@pRegionId uniqueidentifier = NULL");
        Expect.Contains(sql, "(@pCountryCode IS NULL OR [CountryCode] = @pCountryCode)");
        Expect.Contains(api, "[FromQuery] string? countryCode,");
        Expect.Contains(api, "[FromQuery] Guid? regionId,");
        Expect.Contains(api, "(object?)(string.IsNullOrEmpty(countryCode) ? null : countryCode) ?? DBNull.Value");
        Expect.Contains(query, "if (!string.IsNullOrEmpty(countryCode))\n            query = query.Where(t => t.CountryCode == countryCode);");
        Expect.Contains(query, "if (regionId is { } regionIdValue)");
        Expect.Contains(pg, "NpgsqlTypes.NpgsqlDbType.Text) { Value = (object?)(string.IsNullOrEmpty(countryCode)");
        Expect.Contains(pg, "\"@pRegionId\", NpgsqlTypes.NpgsqlDbType.Uuid)");
    }

    [TestMethod]
    public async Task The_screens_pass_a_text_and_a_uniqueidentifier_key_through()
    {
        var table = Shipment();
        string html = await Render("TS_Component_v1.tt", table, Project(), "shipment.component.html");
        string vm = await Render("WinUI3_MasterScreen_v1.tt", table, Project(), "ShipmentListViewModel.cs");

        Expect.Contains(html, "aria-label=\"Filter by Country Code\"");
        Expect.Contains(vm, "countryCode: (string?)CountryCodeFilter?.Id, regionId: (Guid?)RegionIdFilter?.Id, pageNumber: PageNumber");
        Expect.DoesNotContain(vm, "_countryCodeSearch");
    }

    [TestMethod]
    public async Task Python_and_rust_take_a_text_and_a_uniqueidentifier_key()
    {
        var table = Shipment(SqlDialect.PostgreSql);
        string py = await Render("PY_Routes_v1.tt", table, Project(), "shipment.py");
        string rs = await Render("RS_Repo_v1.tt", table, Project(), "shipment.rs");

        Expect.Contains(py, "from uuid import UUID\n");
        Expect.Contains(py, "country_code: str | None = Query(None, alias=\"countryCode\"),");
        Expect.Contains(py, "region_id: UUID | None = Query(None, alias=\"regionId\"),");
        Expect.Contains(py, "    if country_code:\n        statement = statement.where(Shipment.country_code == country_code)");
        Expect.Contains(py, "    if region_id is not None:");
        Expect.Contains(rs, "pub country_code: Option<String>,");
        Expect.Contains(rs, "pub region_id: Option<Uuid>,");
        Expect.Contains(rs, "if let Some(value) = non_blank(&query.country_code) {");
    }

    [TestMethod]
    public async Task The_search_list_protocol_keeps_its_one_box_and_has_no_key_drop_downs()
    {
        string page = await Render("TSX_Page_v1.tt", Invoice(), Project(("ListProtocol", "Search")), "SalesInvoicePage.tsx");

        Expect.DoesNotContain(page, "Filter by Customer");
    }
}
