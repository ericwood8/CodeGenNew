using System.Data;
using CodeGenNew.Core;
using CodeGenNew.TemplateEngine;

namespace CodeGenNew.Tests;

/// <summary> API_OData (OData.cs: the selected tables served under /odata) and the Program essentials that reference the package and call it. </summary>
[TestClass]
public class ODataApiTests
{
    private static ProjectSettings Project(params (string Key, string Value)[] values) =>
        ProjectSettings.FromValues(values.Select(v => new KeyValuePair<string, string>(v.Key, v.Value)).Append(new("ProjectName", "Acme")));

    private static TableModel TimeSheet() => Sample.Table("TimeSheet",
    [
        Sample.Column("TimeSheetId", SqlDbType.Int, primaryKey: true, identity: true, ordinal: 1),
        Sample.Column("EmployeeId", SqlDbType.Int, ordinal: 2)
    ]);

    private static DatabaseModel Database() => new() { DatabaseName = "Acme", SchemaName = "dbo", Tables = [Sample.Holiday(), Sample.NaturalKey(), Sample.CompositeKey(), TimeSheet()] };

    private static async Task<string> Render(params (string Key, string Value)[] values)
    {
        var result = await Repo.Cache.RunAsync(Repo.Template("API_OData_v1.tt"), Database(), Project(values));
        Assert.IsTrue(result.Success, string.Join(" | ", result.Errors));
        return result.GeneratedText!.ReplaceLineEndings("\n");
    }

    private static async Task<string> Program(params (string Key, string Value)[] values)
    {
        var result = await Repo.Cache.RunAsync(Repo.Template("API_EssentialProgram_v1.tt"), Project(values));
        Assert.IsTrue(result.Success, string.Join(" | ", result.Errors));
        return result.GeneratedText!.ReplaceLineEndings("\n");
    }

    [TestMethod]
    public void The_flag_adds_the_template_to_the_plan_and_the_template_is_not_in_the_default_plan()
    {
        CollectionAssert.Contains(Project(("OData", "true")).ImpliedPlanTemplates.ToArray(), "API_OData");
        CollectionAssert.DoesNotContain(Project().ImpliedPlanTemplates.ToArray(), "API_OData");
        Assert.IsFalse(TemplateConfig.Load(Repo.Template("API_OData_v1.tt.config")).InPlan);
    }

    [TestMethod]
    public async Task Each_served_table_gets_its_key_dates_and_one_mapped_set()
    {
        string odata = await Render(("OData", "true"), ("ApiNamespace", "Acme.Api.Apis"), ("ContextName", "AcmeContext"));

        Expect.Contains(odata, "namespace Acme.Api.Apis;");
        Expect.Contains(odata, "builder.EntityType<Holiday>().HasKey(e => e.HolidayId);");
        Expect.Contains(odata, "builder.EntityType<Holiday>().Property(e => e.Date).AsDate();");
        Expect.Contains(odata, "builder.EntitySet<Holiday>(\"" + ODataNames.SetName("Holiday") + "\");");
        Expect.Contains(odata, "builder.EntityType<Country>().HasKey(e => e.Code);");
        Expect.Contains(odata, "MapSet<Holiday, int, AcmeContext>(odata, lists, \"" + ODataNames.SetName("Holiday") + "\", \"HolidayId\", \":int\", false, [\"HolidayId\"], new ODataRules<Holiday, AcmeContext>());");
        Expect.Contains(odata, "MapSet<Country, string, AcmeContext>(odata, lists, \"" + ODataNames.SetName("Country") + "\", \"Code\", \"\", true, [\"Code\"], new ODataRules<Country, AcmeContext>());");
        Expect.Contains(odata, "MapSet<TimeSheet, int, AcmeContext>");
        Expect.DoesNotContain(odata, "Junction");
        Expect.Contains(odata, "var served = new HashSet<Type> { typeof(Country), typeof(Holiday), typeof(TimeSheet) };");
        Expect.Contains(odata, "JsonNamingPolicy.CamelCase.ConvertName(property.PropertyInfo.Name)");
        Expect.Contains(odata, "public static partial class ODataEndpoints");
        Expect.Contains(odata, "static partial void Stamp<T>(DbContext context, T row, bool isNew) where T : class;");
        Expect.Contains(odata, "Stamp(context, row, true);");
        Assert.AreEqual(2, odata.Split("Stamp(context, row, false);").Length - 1, "put and patch stamp the changed row");
    }

    [TestMethod]
    public async Task The_limits_come_from_the_settings_and_the_validator_runs_only_with_ApiValidation()
    {
        string plain = await Render(("OData", "true"), ("ODataMaxTop", "50"), ("ODataPageSize", "10"), ("ODataExpandDepth", "3"));
        Expect.Contains(plain, "private const int MaxTop = 50;");
        Expect.Contains(plain, "private const int PageSize = 10;");
        Expect.Contains(plain, "private const int ExpandDepth = 3;");
        Expect.DoesNotContain(plain, "ValidationFilter");
        Expect.DoesNotContain(plain, "IValidator");

        string validated = await Render(("OData", "true"), ("ApiValidation", "true"));
        Expect.Contains(validated, "using FluentValidation;");
        Expect.Contains(validated, ".AddEndpointFilter<ValidationFilter<T>>()");
        Expect.Contains(validated, "IValidator<T>");
    }

    [TestMethod]
    public async Task A_table_whose_rows_belong_to_a_person_gets_a_scope_and_no_navigation_leads_into_it()
    {
        string odata = await Render(("OData", "true"), ("ApiAuth", "true"), ("OwnedTables", "TimeSheet"), ("ContextName", "AcmeContext"));

        Expect.Contains(odata, "public sealed class TimeSheetODataRules : ODataRules<TimeSheet, AcmeContext>");
        Expect.Contains(odata, "public override bool Owned => true;");
        Expect.Contains(odata, "public override int? OwnerOf(TimeSheet row) => row.EmployeeId;");
        Expect.Contains(odata, "public override void SetOwner(TimeSheet row, int owner) => row.EmployeeId = owner;");
        Expect.Contains(odata, "rows.Where(c => owners.Contains(c.EmployeeId));");
        Expect.Contains(odata, ", new TimeSheetODataRules());");
        Expect.Contains(odata, ", new ODataRules<Holiday, AcmeContext>());");
        Expect.Contains(odata, "var scoped = new HashSet<Type> { typeof(TimeSheet) };");
        Expect.Contains(odata, "if (!served.Contains(navigation.RelatedClrType) || scoped.Contains(navigation.RelatedClrType))");
        Expect.Contains(odata, "GetRequiredService<IOwnerScope>().VisibleOwnersAsync(http.User, http.RequestAborted)");
        Expect.Contains(odata, "return await OwnersAsync(http, rules) is { } owners ? rules.Scope(context, rows, owners) : rows;");
        Expect.Contains(odata, "if (await RefuseOtherOwnersAsync(context, http, rules, row) is { } refused)");
        Expect.Contains(odata, "if (await ClaimOwnerAsync(context, http, rules, row) is { } refused)");
        Expect.Contains(odata, "type: \"/problems/not-your-row\"");
        Expect.DoesNotContain(odata, "HolidayODataRules");

        string open = await Render(("OData", "true"));
        Expect.DoesNotContain(open, "IOwnerScope");
        Expect.DoesNotContain(open, "ODataRules : ");
        Expect.Contains(open, "var scoped = new HashSet<Type> {  };");
    }

    [TestMethod]
    public async Task A_table_with_gates_or_guards_runs_them_before_it_saves_and_a_table_owned_through_its_parent_scopes_by_the_parent()
    {
        string gated = await Render(("OData", "true"), ("ApiAuth", "true"), ("GuardedColumns", "Holiday.Name; by Employee.IsManager"), ("ContextName", "AcmeContext"));
        Expect.Contains(gated, "public sealed class HolidayODataRules : ODataRules<Holiday, AcmeContext>");
        Expect.Contains(gated, "public override Task<IResult?> BeforeSaveAsync(AcmeContext context, ClaimsPrincipal caller, Holiday row, bool isNew) => HolidayGates.CheckAsync(context, caller, row, isNew);");
        Expect.DoesNotContain(gated, "public override bool Owned => true;");
        Expect.Contains(gated, "if (await rules.BeforeSaveAsync(context, http.User, row, true) is { } refusedSave)");
        Expect.Contains(gated, "if (await rules.BeforeSaveAsync(context, http.User, body, false) is { } refusedSave)");
        Expect.Contains(gated, "if (await rules.BeforeSaveAsync(context, http.User, row, false) is { } refusedSave)");
    }

    [TestMethod]
    public async Task A_table_listed_in_OwnedTables_that_cannot_be_scoped_is_named_in_a_comment_instead_of_served()
    {
        string odata = await Render(("OData", "true"), ("ApiAuth", "true"), ("OwnedTables", "Holiday"));
        Expect.Contains(odata, "// OData: Holiday is not served; it is listed in OwnedTables but has no NOT NULL whole-number owner column");
        Expect.DoesNotContain(odata, "MapSet<Holiday");
        Expect.DoesNotContain(odata, "EntityType<Holiday>");
    }

    [TestMethod]
    public async Task Serving_no_table_is_an_error_that_says_why()
    {
        var result = await Repo.Cache.RunAsync(Repo.Template("API_OData_v1.tt"), Database(), Project(("OData", "true"), ("ODataTables", "Nope")));
        Assert.IsFalse(result.Success);
        Expect.Contains(string.Join(" | ", result.Errors), "Nope is not a table of this database");
    }

    [TestMethod]
    public async Task The_Program_essential_references_the_package_and_calls_both_methods_only_with_the_flag()
    {
        string on = await Program(("OData", "true"));
        Expect.Contains(on, "<PackageReference Include=\"Microsoft.AspNetCore.OData\" Version=\"9.5.0\" />");
        Expect.Contains(on, "builder.Services.AddGeneratedOData();");
        Expect.Contains(on, "app.MapODataEndpoints();");

        string off = await Program();
        Expect.DoesNotContain(off, "Microsoft.AspNetCore.OData");
        Expect.DoesNotContain(off, "AddGeneratedOData");
        Expect.DoesNotContain(off, "MapODataEndpoints");
    }

    [TestMethod]
    public async Task A_name_and_active_table_keeps_the_name_rules_of_the_REST_routes()
    {
        var database = new DatabaseModel { DatabaseName = "Acme", SchemaName = "dbo", Tables = [Sample.NameActiveTableWithChildren(), Sample.Holiday()] };
        var result = await Repo.Cache.RunAsync(Repo.Template("API_OData_v1.tt"), database, Project(("OData", "true"), ("EnumTables", "none"), ("ContextName", "AcmeContext")));
        Assert.IsTrue(result.Success, string.Join(" | ", result.Errors));
        string odata = result.GeneratedText!.ReplaceLineEndings("\n");

        Expect.Contains(odata, "public sealed class TeamODataRules : ODataRules<Team, AcmeContext>");
        Expect.Contains(odata, "public override async Task<IResult?> BeforeSaveAsync(AcmeContext context, ClaimsPrincipal caller, Team row, bool isNew)");
        Expect.Contains(odata, "if (string.IsNullOrWhiteSpace(row.Name))");
        Expect.Contains(odata, "type: \"/problems/name-required\"");
        Expect.Contains(odata, "row.Name = row.Name.Trim();");
        Expect.Contains(odata, "AnyAsync(d => d.Name == row.Name && d.IsActive && d.TeamId != row.TeamId))");
        Expect.Contains(odata, "type: \"/problems/duplicate-name\"");
        Expect.Contains(odata, "$\"An active row named '{row.Name}' already exists.\"");
        Expect.Contains(odata, ", new TeamODataRules());");
        Expect.Contains(odata, ", new ODataRules<Holiday, AcmeContext>());");
        Expect.DoesNotContain(odata, "public override bool Owned => true;");
    }
}
