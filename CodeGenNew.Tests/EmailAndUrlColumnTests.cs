using System.Data;
using CodeGenNew.Core;

namespace CodeGenNew.Tests;

/// <summary> Which text columns hold an email address or a web address: the name, or the project's EmailColumns and UrlColumns. </summary>
[TestClass]
public class EmailAndUrlColumnTests
{
    private static ProjectSettings Project(params (string Key, string Value)[] values) =>
        ProjectSettings.FromValues(values.Select(v => new KeyValuePair<string, string>(v.Key, v.Value)).Append(new("ProjectName", "Acme")));

    private static ColumnModel Column(string name) => Sample.Column(name, SqlDbType.VarChar, nullable: true, characters: 100, ordinal: 2);

    [TestMethod]
    [DataRow("Email", TextShape.Email)]
    [DataRow("BillingEmailAddress", TextShape.Email)]
    [DataRow("Mail", TextShape.Email)]
    [DataRow("EMail", TextShape.Email)]
    [DataRow("OwnerMail", TextShape.Email)]
    [DataRow("MailAddress", TextShape.Email)]
    [DataRow("Homepage", TextShape.Url)]
    [DataRow("HomePage", TextShape.Url)]
    [DataRow("Web", TextShape.Url)]
    [DataRow("CompanyWeb", TextShape.Url)]
    [DataRow("WebAddress", TextShape.Url)]
    [DataRow("Website", TextShape.Url)]
    [DataRow("SiteLink", TextShape.Url)]
    [DataRow("MailingAddress", TextShape.None)]
    [DataRow("MailingList", TextShape.None)]
    [DataRow("Webmaster", TextShape.None)]
    [DataRow("Cobweb", TextShape.None)]
    public void A_name_says_what_the_column_holds(string name, TextShape expected) =>
        Assert.AreEqual(expected, Column(name).ShapeOf(Project(), "Contact"), name);

    [TestMethod]
    public void The_project_names_the_columns_whatever_their_name_is()
    {
        var project = Project(("EmailColumns", "Reach, Contact.Backup"), ("UrlColumns", "Where"));

        Assert.AreEqual(TextShape.Email, Column("Reach").ShapeOf(project, "Contact"));
        Assert.AreEqual(TextShape.Email, Column("Backup").ShapeOf(project, "Contact"), "one table's column");
        Assert.AreEqual(TextShape.None, Column("Backup").ShapeOf(project, "Other"), "not another table's");
        Assert.AreEqual(TextShape.Url, Column("Where").ShapeOf(project, "Contact"));
        Assert.AreEqual(TextShape.None, Column("Reach").ShapeOf(Project(), "Contact"));
    }

    [TestMethod]
    public void A_listed_column_beats_its_name_and_the_plain_text_list_beats_both()
    {
        Assert.AreEqual(TextShape.Url, Column("EmailTemplate").ShapeOf(Project(("UrlColumns", "EmailTemplate")), "Contact"));
        Assert.AreEqual(TextShape.None, Column("Mail").ShapeOf(Project(("PlainTextColumns", "Mail"), ("EmailColumns", "Mail")), "Contact"));
    }

    [TestMethod]
    public void A_column_with_a_list_of_values_or_a_number_has_no_shape()
    {
        var choices = Sample.Column("Mail", SqlDbType.VarChar, characters: 10, ordinal: 2, choices: ["a", "b"]);
        var number = Sample.Column("Mail", SqlDbType.Int, ordinal: 2);

        Assert.AreEqual(TextShape.None, choices.ShapeOf(Project(("EmailColumns", "Mail")), "Contact"));
        Assert.AreEqual(TextShape.None, number.ShapeOf(Project(("EmailColumns", "Mail")), "Contact"));
    }

    [TestMethod]
    public void The_rule_the_validators_read_follows_the_shape_and_the_form_gets_the_input_type()
    {
        var project = Project(("EmailColumns", "Reach"), ("UrlColumns", "Where"));

        Assert.AreEqual(TextShape.Email, Column("Reach").RuleOf(project, "Contact")!.Shape);
        Assert.AreEqual("email", Column("Reach").InputType(project, "Contact"));
        Assert.AreEqual("url", Column("Where").InputType(project, "Contact"));
        Assert.AreEqual("email", Column("Mail").InputType(Project(), "Contact"));
        Assert.AreEqual("text", Column("MailingList").InputType(Project(), "Contact"));
    }

    [TestMethod]
    public void A_generated_test_posts_an_address_of_the_right_shape_and_adds_no_unique_ending_to_it()
    {
        var project = Project(("EmailColumns", "Reach"));

        Assert.AreEqual("someone@example.com", ApiSampleValues.Text(Column("Reach"), project, "Contact"));
        Assert.AreEqual("someone@example.com", ApiSampleValues.Text(Column("Mail"), project, "Contact"));
        Assert.AreEqual("https://example.com", ApiSampleValues.Text(Column("Homepage"), project, "Contact"));
        Assert.AreEqual("Sample reach", ApiSampleValues.Text(Column("Reach"), Project(), "Contact"));
        Assert.IsTrue(ApiSampleValues.HasFixedFormat(Column("Reach"), project, "Contact"));
        Assert.IsTrue(ApiSampleValues.HasFixedFormat(Column("Homepage"), Project(), "Contact"));
        Assert.IsFalse(ApiSampleValues.HasFixedFormat(Column("MailingList"), Project(), "Contact"));
    }

    [TestMethod]
    public void The_settings_are_known_and_described()
    {
        foreach (string key in new[] { "EmailColumns", "UrlColumns" })
        {
            Assert.Contains(key, ProjectSettings.Keys);
            Assert.IsTrue(ProjectSettingsHints.All.ContainsKey(key), key);
            Assert.IsTrue(ProjectSettingGroups.All.Any(g => g.Keys.Contains(key)), key);
        }
    }
}
