using System.Data;
using CodeGenNew.Core;

namespace CodeGenNew.Tests;

/// <summary> Which tables the API serves as OData (<c>OData=true</c>, <c>ODataTables</c>) and the settings that steer it. </summary>
[TestClass]
public class ODataPlanTests
{
    private static readonly (string, string) Auth = ("ApiAuth", "true");

    private static ProjectSettings Project(params (string Key, string Value)[] values) =>
        ProjectSettings.FromValues(values.Select(v => new KeyValuePair<string, string>(v.Key, v.Value)).Append(new("ProjectName", "Acme")));

    private static TableModel TimeSheet() => Sample.Table("TimeSheet",
    [
        Sample.Column("TimeSheetId", SqlDbType.Int, primaryKey: true, identity: true, ordinal: 1),
        Sample.Column("EmployeeId", SqlDbType.Int, ordinal: 2),
        Sample.Column("Notes", SqlDbType.NVarChar, characters: 100, ordinal: 3)
    ]);

    private static DatabaseModel Database() => new()
    {
        DatabaseName = "Acme", SchemaName = "dbo",
        Tables = [Sample.Holiday(), Sample.NaturalKey(), Sample.CompositeKey(), Sample.Employee(), TimeSheet()]
    };

    private static string[] Names(ODataSelection selection) => selection.Tables.Select(t => t.TableName).ToArray();

    [TestMethod]
    public void The_settings_default_to_off_with_safe_limits()
    {
        var plain = Project();
        Assert.IsFalse(plain.OData);
        Assert.IsEmpty(plain.ODataTables);
        Assert.AreEqual(100, plain.ODataMaxTop);
        Assert.AreEqual(25, plain.ODataPageSize);
        Assert.AreEqual(2, plain.ODataExpandDepth);

        var set = Project(("OData", "true"), ("ODataTables", "Holiday, Country"), ("ODataMaxTop", "50"), ("ODataPageSize", "10"), ("ODataExpandDepth", "3"));
        Assert.IsTrue(set.OData);
        CollectionAssert.AreEqual(new[] { "Holiday", "Country" }, set.ODataTables);
        Assert.AreEqual(50, set.ODataMaxTop);
        Assert.AreEqual(10, set.ODataPageSize);
        Assert.AreEqual(3, set.ODataExpandDepth);
        Assert.Contains("OData", ProjectSettingsHints.BooleanKeys);
    }

    [TestMethod]
    public void A_page_is_never_longer_than_the_most_rows_a_request_may_ask_for_and_bad_numbers_fall_back()
    {
        Assert.AreEqual(20, Project(("ODataMaxTop", "20"), ("ODataPageSize", "500")).ODataPageSize);
        Assert.AreEqual(100, Project(("ODataMaxTop", "0")).ODataMaxTop);
        Assert.AreEqual(100, Project(("ODataMaxTop", "-5")).ODataMaxTop);
        Assert.AreEqual(2, Project(("ODataExpandDepth", "deep")).ODataExpandDepth);
    }

    [TestMethod]
    public void A_blank_list_serves_every_table_that_has_a_CRUD_API_whatever_its_key()
    {
        var selection = ODataPlan.For(Database(), Project(("OData", "true")));
        CollectionAssert.AreEqual(new[] { "Country", "Employee", "Holiday", "TimeSheet" }, Names(selection), "the composite key table has no CRUD API");
        Assert.IsEmpty(selection.Refusals);
        CollectionAssert.AreEqual(Names(selection), Names(ODataPlan.For(Database(), Project(("OData", "true"), ("ODataTables", "*")))));
    }

    [TestMethod]
    public void A_list_serves_only_the_tables_it_names_and_says_why_it_cannot_serve_the_others_it_names()
    {
        var selection = ODataPlan.For(Database(), Project(("ODataTables", "holiday, Junction, Nope")));
        CollectionAssert.AreEqual(new[] { "Holiday" }, Names(selection));
        Assert.HasCount(2, selection.Refusals);
        Assert.AreEqual(ODataPlan.NoCrudApi, selection.Refusals.Single(r => r.Table == "Junction").Reason);
        Assert.AreEqual("is not a table of this database", selection.Refusals.Single(r => r.Table == "Nope").Reason);
    }

    [TestMethod]
    public void A_table_whose_rows_belong_to_a_person_is_served_and_scoped_and_one_that_cannot_be_scoped_is_refused()
    {
        var owned = ("OwnedTables", "TimeSheet");
        var served = ODataPlan.For(Database(), Project(Auth, owned));
        CollectionAssert.Contains(Names(served), "TimeSheet");
        Assert.IsEmpty(served.Refusals);
        Assert.IsTrue(ODataPlan.IsScoped(TimeSheet(), Project(Auth, owned)));
        Assert.IsFalse(ODataPlan.IsScoped(TimeSheet(), Project(owned)), "nothing is scoped without ApiAuth");
        Assert.IsFalse(ODataPlan.IsScoped(Sample.Holiday(), Project(Auth, owned)));

        var unusable = ODataPlan.For(Database(), Project(Auth, ("OwnedTables", "Holiday")));
        CollectionAssert.DoesNotContain(Names(unusable), "Holiday");
        Assert.Contains("owner column", unusable.Refusals.Single(r => r.Table == "Holiday").Reason);
    }

    [TestMethod]
    public void A_table_with_gates_is_served_and_the_user_table_a_NoApiTables_entry_or_a_DTO_is_refused_with_its_reason()
    {
        var guarded = ODataPlan.For(Database(), Project(Auth, ("GuardedColumns", "Holiday.Name; by Employee.IsManager")));
        CollectionAssert.Contains(Names(guarded), "Holiday");
        Assert.IsEmpty(guarded.Refusals, "gates and guards run in the OData routes too");

        var user = ODataPlan.For(Database(), Project(Auth, ("UserTable", "Employee")));
        Assert.Contains("password", user.Refusals.Single(r => r.Table == "Employee").Reason);

        var noApi = ODataPlan.For(Database(), Project(("NoApiTables", "Country")));
        Assert.Contains("NoApiTables", noApi.Refusals.Single(r => r.Table == "Country").Reason);

        var dtos = ODataPlan.For(Database(), Project(("Dtos", "true")));
        Assert.IsEmpty(dtos.Tables, "with DTOs the REST API hides columns the OData entity would show");
        Assert.HasCount(4, dtos.Refusals);
    }

    [TestMethod]
    public void A_screen_knows_from_the_name_and_the_settings_whether_another_table_is_probably_served()
    {
        Assert.IsTrue(ODataPlan.ServesByName("Holiday", Project(("OData", "true"))));
        Assert.IsFalse(ODataPlan.ServesByName("Holiday", Project()), "OData is off");
        Assert.IsFalse(ODataPlan.ServesByName("Holiday", Project(("OData", "true"), ("ODataTables", "Country"))));
        Assert.IsTrue(ODataPlan.ServesByName("holiday", Project(("OData", "true"), ("ODataTables", "Holiday"))));
        Assert.IsFalse(ODataPlan.ServesByName("Holiday", Project(("OData", "true"), ("NoApiTables", "Holiday"))));
        Assert.IsTrue(ODataPlan.ServesByName("Holiday", Project(("OData", "true"), Auth, ("OwnedTables", "TimeSheet"))), "owner scoping and gates are applied by the OData routes");
        Assert.IsTrue(ODataPlan.ServesByName("Holiday", Project(("OData", "true"), Auth, ("GuardedColumns", "Holiday.Name; by Employee.IsManager"))));
        Assert.IsFalse(ODataPlan.ServesByName("Employee", Project(("OData", "true"), Auth, ("UserTable", "Employee"))));
        Assert.IsFalse(ODataPlan.ServesByName("Holiday", Project(("OData", "true"), ("Dtos", "true"))));
    }

    [TestMethod]
    public void A_table_whose_form_edits_child_lists_is_refused_because_the_OData_routes_do_not_save_them()
    {
        var timeSheet = ODataAngularTests.TimeSheetWithKeyedDetail();
        Assert.IsNull(ODataPlan.RefusalOf(timeSheet, Project(("OData", "true"))));
        string? refusal = ODataPlan.RefusalOf(timeSheet, Project(("OData", "true"), ("EditableChildren", "TimeSheetDetail")));
        Assert.IsNotNull(refusal);
        Assert.Contains("EditableChildren", refusal);
    }

    [TestMethod]
    public void The_entity_set_has_the_name_of_the_REST_route()
    {
        foreach (var table in Database().CrudApiTables(Project()))
        {
            Assert.AreEqual(ScreenNames.ApiRoute(table.TableName), ODataNames.SetName(table.TableName));
            Assert.AreEqual("/odata/" + ScreenNames.ApiRoute(table.TableName), ODataNames.Route(table.TableName));
        }
        Assert.EndsWith(ODataNames.SetName("Customer"), BlazorNames.ApiRoute("Customer"));
    }

    [TestMethod]
    public void A_name_and_active_table_is_served_and_is_never_scoped_to_a_person()
    {
        var team = Sample.NameActiveTableWithChildren();
        var database = new DatabaseModel { DatabaseName = "Acme", SchemaName = "dbo", Tables = [team, Sample.Holiday()] };
        var settings = Project(("OData", "true"), ("EnumTables", "none"), Auth, ("OwnedTables", "Team"));

        Assert.IsTrue(ODataPlan.HasApi(team, settings));
        Assert.IsTrue(ODataPlan.Serves(team, settings));
        CollectionAssert.Contains(Names(ODataPlan.For(database, settings)), "Team");
        Assert.IsFalse(ODataPlan.IsScoped(team, settings), "the REST routes never scope a name and active table");
        Assert.IsTrue(ODataPlan.ServesByName("Team", settings));
        Assert.IsTrue(ODataPlan.AngularScreenTables(database, Project(("OData", "true"), ("EnumTables", "none"), ("Screens", "Team"))).Any(t => t.TableName == "Team"));
    }
}
