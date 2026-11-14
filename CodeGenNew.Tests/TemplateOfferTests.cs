using System.Data;
using CodeGenNew.Core;
using CodeGenNew.Generation;
using CodeGenNew.TemplateEngine;

namespace CodeGenNew.Tests;

/// <summary> The menu offers a project-dependent template only for a table a whole-project run would write it for. </summary>
[TestClass]
public class TemplateOfferTests
{
    private static readonly IReadOnlyList<TemplateInfo> Templates = TemplateCatalog.Discover(Repo.TemplatesDirectory);

    private static TemplateInfo Template(string name) => Templates.Single(t => t.Name == name);

    private static TableModel Table(string name) => Sample.Table(name, [Sample.Column(name + "Id", SqlDbType.Int, primaryKey: true, identity: true, ordinal: 1), Sample.Column("Title", SqlDbType.VarChar, characters: 40, ordinal: 2)]);

    private static DatabaseModel Database(params TableModel[] tables) => new() { DatabaseName = "Shop", SchemaName = "dbo", Tables = [.. tables] };

    private static ProjectSettings Project(params (string, string)[] values) =>
        ProjectSettings.FromValues(new Dictionary<string, string> { ["ProjectName"] = "Shop", ["AccessMode"] = "Ef" }.Concat(values.Select(v => KeyValuePair.Create(v.Item1, v.Item2))).ToDictionary());

    [TestMethod]
    public void A_screen_is_offered_only_for_a_table_the_Screens_setting_lists()
    {
        var customer = Table("Customer");
        var hash = Table("AssetFileHash");
        var database = Database(customer, hash);
        var project = Project(("Screens", "Customer"));
        Assert.IsTrue(TemplateOffer.Applies(Template("BLZ_Page"), customer, database, project));
        Assert.IsFalse(TemplateOffer.Applies(Template("BLZ_Page"), hash, database, project));
    }

    [TestMethod]
    public void An_OData_page_is_offered_only_for_a_table_the_project_serves_as_OData()
    {
        var customer = Table("Customer");
        var database = Database(customer);
        Assert.IsFalse(TemplateOffer.Applies(Template("BLZ_ODataPage"), customer, database, Project()));
        Assert.IsTrue(TemplateOffer.Applies(Template("BLZ_ODataPage"), customer, database, Project(("OData", "true"))));
    }

    [TestMethod]
    public void A_template_that_does_not_depend_on_the_project_is_always_offered()
    {
        var customer = Table("Customer");
        Assert.IsTrue(TemplateOffer.Applies(Template("API_Crud"), customer, Database(customer), Project(("Screens", "Other"))));
    }
}
