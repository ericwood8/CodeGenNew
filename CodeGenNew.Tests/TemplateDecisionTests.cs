using System.Data;
using CodeGenNew.Core;

namespace CodeGenNew.Tests;

/// <summary> What the test and entity templates used to decide inside themselves and Core now decides: the members of an entity class, the plan of an API test class, of a Blazor page test and of the transfer-class tests, and the junction test's keys. </summary>
[TestClass]
public class TemplateDecisionTests
{
    private static ProjectSettings Project(params (string Key, string Value)[] values) =>
        ProjectSettings.FromValues(values.Select(v => new KeyValuePair<string, string>(v.Key, v.Value)).Append(new("ProjectName", "Acme")));

    private static ForeignKeyModel StatusKey(ForeignKeyDeleteRule rule = ForeignKeyDeleteRule.NoAction) => new()
    {
        ConstraintName = "FK_s", ReferencingColumns = ["CustomerStatusId"], ReferencedSchema = "dbo", ReferencedTable = "CustomerStatus", ReferencedColumns = ["CustomerStatusId"], DeleteRule = rule
    };

    private static TableModel Customer(ForeignKeyDeleteRule rule = ForeignKeyDeleteRule.NoAction) => Sample.Table("Customer",
    [
        Sample.Column("CustomerId", SqlDbType.Int, primaryKey: true, identity: true, ordinal: 1),
        Sample.Column("CustomerStatusId", SqlDbType.Int, ordinal: 2),
        Sample.Column("Name", SqlDbType.NVarChar, characters: 50, ordinal: 3),
        Sample.Column("Balance", SqlDbType.Decimal, precision: 10, scale: 2, ordinal: 4),
        Sample.Column("Joined", SqlDbType.Date, ordinal: 5),
        Sample.Column("Note", SqlDbType.NVarChar, characters: 200, nullable: true, ordinal: 6)
    ], [StatusKey(rule)]);

    // ---- the entity class ----

    [TestMethod]
    public void The_entity_members_are_the_key_the_foreign_keys_the_navigations_and_a_property_per_other_column()
    {
        var entity = EntityCode.Of(Customer(), Project());
        string body = entity.Body.Replace("\r\n", "\n");

        Assert.IsTrue(body.StartsWith("    #region Omitted\n    [Key]\n    [Display(Order = -1, AutoGenerateField = true)]\n    public required int CustomerId { get; set; }\n"));
        Expect.Contains(body, "    [ForeignKey(nameof(CustomerStatus))]\n    [Display(Order = -1, AutoGenerateField = false)]\n    public required int CustomerStatusId { get; set; }\n    #endregion Omitted");
        Expect.Contains(body, "    // entities\n    public CustomerStatus? CustomerStatus { get; set; }\n");
        Expect.Contains(body, "    [Display(Name = \"Name\", Description = \"Name\")]\n    [StringLength(50)]\n    public required string Name { get; set; } = \"\";\n");
        Expect.Contains(body, "[Precision(10, 2)]");
        Expect.Contains(body, "    [DataType(DataType.Date)]\n");
        Expect.Contains(body, "    [Display(Order = -1, Name = \"Note\", Description = \"Note\")]\n    [StringLength(200)]\n    [DataType(DataType.MultilineText)]\n    public string? Note { get; set; }\n");
        Expect.Contains(body, "    public override string? ToString() => Name;");
        Assert.IsTrue(entity.UsesSchema, "[ForeignKey] and [DatabaseGenerated] come from the Schema namespace");
    }

    // ---- an API test class ----

    [TestMethod]
    public void The_api_test_plan_names_the_route_the_body_the_changed_column_and_the_parents_a_delete_must_refuse()
    {
        var plan = ApiTestPlan.For(Customer(), Project(("ApiValidation", "true")))!;

        Assert.AreEqual("/api/customers", plan.Route);
        Assert.AreEqual(("customerId", "long", "id", "int.MaxValue"), (plan.KeyJson, plan.KeyCs, plan.IdRef, plan.AbsentKey));
        Assert.AreEqual(("name", "string"), (plan.ChangedJson, plan.ChangedType));
        Assert.AreEqual("await ApiFixture.ExistingKeyAsync(\"/api/customerstatus\", \"customerStatusId\", 1, \"CustomerStatus\")", plan.Expression(plan.BodyColumns.Single(c => c.Name == "CustomerStatusId")));
        Assert.AreEqual("ApiFixture.Unique(\"Sample name\", 50)", plan.Expression(plan.BodyColumns.Single(c => c.Name == "Name")));
        Assert.AreEqual("CustomerStatus", plan.UsedParents.Single().Key.ReferencedTable);
        Assert.AreEqual("/api/customerstatus", plan.UsedParents.Single().Route);
        Assert.AreEqual("Name", plan.LongText!.Name, "with ApiValidation a text longer than its column is refused");
        Assert.IsNull(plan.Ranged);
        Assert.IsFalse(plan.CanClone && !plan.IntKey);

        Assert.AreEqual(0, ApiTestPlan.For(Customer(ForeignKeyDeleteRule.Cascade), Project())!.UsedParents.Count, "a key that cascades is never exercised");
        Assert.IsNull(ApiTestPlan.For(Customer(), Project(("NoRepositoryTables", "Customer"))), "a table with no repository has no API to test");
    }

    // ---- a Blazor page test ----

    [TestMethod]
    public void The_blazor_test_plan_has_the_row_the_key_the_role_and_the_header_and_box_to_use()
    {
        var plan = BlazorTestPlan.For(Customer(), Project());
        Assert.IsNotNull(plan.Row);
        Assert.AreEqual("/api/customers", plan.ApiRoute);
        Assert.AreEqual("/odata/customers", plan.ODataSet);
        Assert.AreEqual("", plan.SignIn);
        Assert.AreEqual("Tester", plan.Role);
        Assert.AreEqual("Name", plan.SortName);
        Assert.IsNotNull(plan.Search);
        Assert.AreEqual(0, plan.Children.Count + plan.Editable.Count);

        var signedIn = BlazorTestPlan.For(Customer(), Project(("Auth", "true"), ("ApiAuth", "true"), ("WriteRoles", "Administrator,Manager")));
        Assert.AreEqual("Manager", signedIn.Role);
        Assert.AreEqual("        SignIn(\"Manager\");\n", signedIn.SignIn);
    }

    // ---- the transfer-class tests ----

    [TestMethod]
    public void The_transfer_class_tests_compare_every_column_but_the_hash_and_change_one_that_matters()
    {
        var table = Sample.Table("Account",
        [
            Sample.Column("AccountId", SqlDbType.Int, primaryKey: true, identity: true, ordinal: 1),
            Sample.Column("Title", SqlDbType.NVarChar, characters: 40, ordinal: 2),
            Sample.Column("Total", SqlDbType.Int, ordinal: 3)
        ]);
        var columns = DtoTestColumns.Of(table, Project());

        CollectionAssert.AreEqual(new[] { "AccountId", "Title", "Total" }, columns.All.Select(c => c.Name).ToArray());
        CollectionAssert.AreEqual(new[] { "AccountId", "Title", "Total" }, columns.Checked.Select(c => c.Name).ToArray());
        CollectionAssert.AreEqual(new[] { "Title", "Total" }, columns.Meaningful.Select(c => c.Name).ToArray());
        Assert.AreEqual("Title", columns.Changed!.Name);
        Assert.IsNull(columns.HashColumn);
    }

    // ---- the junction test's keys ----

    private static TableModel CustomerTag(SqlDbType targetType = SqlDbType.Int) => Sample.Table("CustomerTag",
    [
        Sample.Column("CustomerId", SqlDbType.Int, primaryKey: true, ordinal: 1),
        Sample.Column("TagId", targetType, primaryKey: true, ordinal: 2)
    ],
    [
        Sample.ForeignKey("CustomerId", "Customer", "CustomerId", "Name"),
        Sample.ForeignKey("TagId", "Tag", "TagId", "Name")
    ]);

    [TestMethod]
    [DataRow(SqlDbType.Int, false, "int.MaxValue")]
    [DataRow(SqlDbType.SmallInt, false, "int.MaxValue")]
    [DataRow(SqlDbType.TinyInt, false, "")]
    [DataRow(SqlDbType.UniqueIdentifier, true, "Guid.NewGuid()")]
    [DataRow(SqlDbType.NVarChar, true, "")]
    public void The_junction_test_takes_the_type_of_each_key_and_a_missing_target_from_the_shape(SqlDbType target, bool text, string absent)
    {
        var junction = CustomerTag(target).Junction!;

        Assert.IsFalse(junction.AnchorKeyIsText);
        Assert.AreEqual(text, junction.TargetKeyIsText);
        Assert.AreEqual(absent, junction.AbsentTargetCs);
        Assert.IsTrue(junction.BothSidesHaveApi(Project()));
        Assert.IsFalse(junction.BothSidesHaveApi(Project(("NoApiTables", "Tag"))));
    }

    // ---- the API class of a table, and the base classes ----

    [TestMethod]
    public void The_crud_api_plan_names_the_base_the_key_the_lookup_and_what_the_class_stamps()
    {
        var table = Sample.Table("Customer",
        [
            Sample.Column("CustomerId", SqlDbType.Int, primaryKey: true, identity: true, ordinal: 1),
            Sample.Column("Name", SqlDbType.NVarChar, characters: 50, ordinal: 2),
            Sample.Column("Created", SqlDbType.DateTime, ordinal: 3, createDateColumn: true),
            Sample.Column("Opened", SqlDbType.Date, ordinal: 4),
            Sample.Column("Note", SqlDbType.NVarChar, characters: 50, nullable: true, ordinal: 5)
        ]);
        var plan = CrudApiPlan.For(table, Project(("WriteRoles", "Administrator,Manager"), ("ApiAuth", "true")));

        Assert.IsTrue(plan.Supported);
        Assert.IsNull(plan.Refusal);
        Assert.AreEqual(("Customer", "CrudApi", "", "LookupItem", "CustomerId", "int"), (plan.Entity, plan.ApiBase, plan.KeyArgument, plan.LookupItem, plan.KeyProperty, plan.KeyCSharp));
        Assert.AreEqual("c.Name", plan.LookupName);
        Assert.AreEqual("Opened", plan.SortColumn!.Name, "the first date no one sets");
        CollectionAssert.AreEqual(new[] { "Created" }, plan.StampOnNew.Select(c => c.Name).ToArray());
        Assert.AreEqual(0, plan.StampOnChange.Count);
        CollectionAssert.AreEqual(new[] { "Administrator", "Manager" }, plan.WriteRoles.ToArray());
        Assert.AreEqual("LocalNow", CrudApiPlan.Now(plan.StampOnNew[0]));
        Assert.IsFalse(plan.UsesDtos);
        Assert.IsNull(plan.UserPlan);
    }

    [TestMethod]
    public void A_table_the_api_cannot_be_written_for_has_the_reason()
    {
        var composite = Sample.Table("Link",
        [
            Sample.Column("AId", SqlDbType.Int, primaryKey: true, ordinal: 1),
            Sample.Column("BId", SqlDbType.Int, primaryKey: true, ordinal: 2)
        ]);
        var plan = CrudApiPlan.For(composite, Project());
        Assert.IsFalse(plan.Supported);
        Expect.Contains(plan.Refusal!, "a composite primary key (AId, BId)");

        var enumLike = CrudApiPlan.For(Customer(), Project(("NoRepositoryTables", "Customer")));
        Assert.IsFalse(enumLike.Supported);
        Expect.Contains(enumLike.Refusal!, "it is an enum or lookup table");
    }

    [TestMethod]
    public void The_base_class_options_follow_the_dtos_validation_and_sign_in_settings()
    {
        var plain = CrudApiOptions.Of(Project());
        Assert.AreEqual(("TEntity", "TEntity", "", ""), (plain.Input, plain.Output, plain.ValidatorParameter, plain.ValidatorArgument));
        Assert.AreEqual("row", plain.Map("row"));
        Assert.AreEqual("rows", plain.MapAll("rows"));
        Assert.AreEqual("Acme.ApiService.Apis", plain.ApiNamespace);

        var dtos = CrudApiOptions.Of(Project(("Dtos", "true"), ("ApiValidation", "true"), ("ApiAuth", "true"), ("EditableChildren", "Project.Task")));
        Assert.AreEqual(("TInput", "TOutput", ", [FromServices] IValidator<TEntity>? validator", ", validator"), (dtos.Input, dtos.Output, dtos.ValidatorParameter, dtos.ValidatorArgument));
        Assert.AreEqual("ToOutput(row)", dtos.Map("row"));
        Assert.AreEqual("rows.Select(ToOutput).ToList()", dtos.MapAll("rows"));
        Assert.IsTrue(dtos.Validation && dtos.Auth && dtos.Children);
    }
}
