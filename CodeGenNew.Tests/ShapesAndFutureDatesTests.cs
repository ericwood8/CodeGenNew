using System.Data;
using CodeGenNew.Core;

namespace CodeGenNew.Tests;

/// <summary> The postal code, social security number and link shapes, and the "never later than today" rule, in every validator the generated apps have. </summary>
[TestClass]
public class ShapesAndFutureDatesTests
{
    private static ProjectSettings Project(params (string Key, string Value)[] values) =>
        ProjectSettings.FromValues(values.Select(v => new KeyValuePair<string, string>(v.Key, v.Value)).Append(new("ProjectName", "Acme")));

    private static readonly (string Key, string Value)[] Future = [("NotFutureDateColumns", "BornOn,Person.ClosedOn")];

    private static TableModel Person() => Sample.Table("Person",
    [
        Sample.Column("PersonId", SqlDbType.Int, primaryKey: true, identity: true, ordinal: 1),
        Sample.Column("Name", SqlDbType.VarChar, characters: 40, ordinal: 2),
        Sample.Column("BillingZip", SqlDbType.VarChar, nullable: true, characters: 20, ordinal: 3),
        Sample.Column("SocialSecurityNumber", SqlDbType.VarChar, nullable: true, characters: 11, ordinal: 4),
        Sample.Column("SiteLink", SqlDbType.VarChar, nullable: true, characters: 100, ordinal: 5),
        Sample.Column("BornOn", SqlDbType.Date, ordinal: 6),
        Sample.Column("ClosedOn", SqlDbType.DateTime, nullable: true, ordinal: 7)
    ]);

    private static async Task<string> Rendered(string template, TableModel table, ProjectSettings project)
    {
        var result = await Repo.Cache.RunAsync(Repo.Template(template), table, project);
        Assert.IsTrue(result.Success, $"{template}: {string.Join(" | ", result.Errors)}");
        return result.GeneratedText!.ReplaceLineEndings("\n");
    }

    [TestMethod]
    public void The_names_say_the_shape_and_the_project_can_opt_a_column_out()
    {
        var person = Person();
        TextShape Of(string name, ProjectSettings p) => person.Columns.Single(c => c.Name == name).RuleOf(p, "Person")?.Shape ?? TextShape.None;

        Assert.AreEqual(TextShape.Postal, Of("BillingZip", Project()));
        Assert.AreEqual(TextShape.Ssn, Of("SocialSecurityNumber", Project()));
        Assert.AreEqual(TextShape.Url, Of("SiteLink", Project()));
        Assert.AreEqual(TextShape.None, Of("BillingZip", Project(("PlainTextColumns", "Person.BillingZip"))));
        Assert.IsTrue(person.Columns.Single(c => c.Name == "BornOn").RuleOf(Project(Future), "Person")!.NotFuture);
        Assert.IsTrue(person.Columns.Single(c => c.Name == "ClosedOn").RuleOf(Project(Future), "Person")!.NotFuture);
        Assert.IsNull(person.Columns.Single(c => c.Name == "BornOn").RuleOf(Project(), "Person"));
    }

    [TestMethod]
    public void A_postal_code_and_a_social_security_number_pattern_accept_real_values_and_refuse_wrong_ones()
    {
        var postal = new System.Text.RegularExpressions.Regex("^(?:" + ColumnRules.PostalPattern + ")$");
        foreach (string good in new[] { "77249", "33156-1234", "T2H 2B7  ", "SW1A 1AA" })
            Assert.IsTrue(postal.IsMatch(good), good);
        foreach (string bad in new[] { "1", "12", "-1234", "123456789012", "77#49", "" })
            Assert.IsFalse(postal.IsMatch(bad), bad);

        var ssn = new System.Text.RegularExpressions.Regex("^(?:" + ColumnRules.SsnPattern + ")$");
        foreach (string good in new[] { "123-45-6789", "123456789" })
            Assert.IsTrue(ssn.IsMatch(good), good);
        foreach (string bad in new[] { "123-456789", "12-345-6789", "12345678", "abc-de-fghi" })
            Assert.IsFalse(ssn.IsMatch(bad), bad);
    }

    [TestMethod]
    public async Task The_csharp_validator_and_attributes_check_the_new_shapes_and_the_future_date()
    {
        string validator = await Rendered("CS_Validator_v1.tt", Person(), Project(Future));
        Expect.Contains(validator, "RuleFor(x => x.BillingZip).MaximumLength(20).Matches(@\"^(?:" + ColumnRules.PostalPattern + ")$\")");
        Expect.Contains(validator, "RuleFor(x => x.SocialSecurityNumber).MaximumLength(11).Matches(@\"^(?:" + ColumnRules.SsnPattern + ")$\")");
        Expect.Contains(validator, "RuleFor(x => x.SiteLink).MaximumLength(100).Must(value => value is null || Uri.IsWellFormedUriString");
        Expect.Contains(validator, "RuleFor(x => x.BornOn).Must(value => value.Date <= DateTime.Today)");
        Expect.Contains(validator, "RuleFor(x => x.ClosedOn).Must(value => value is null || value.Value.Date <= DateTime.Today)");

        string attributes = await Rendered("CS_Validation_v1.tt", Person(), Project());
        Expect.Contains(attributes, "[RegularExpression(@\"^(?:" + ColumnRules.PostalPattern + ")$\", ErrorMessage = \"Billing Zip is not a postal code.\")]");
        Expect.Contains(attributes, "[Url]\n    public string? SiteLink");
    }

    [TestMethod]
    public async Task The_python_and_rust_validators_check_the_new_shapes_and_the_future_date()
    {
        var project = Project(Future);
        string python = await Rendered("PY_Validate_v1.tt", Person(), project);
        Expect.Contains(python, "from datetime import date");
        Expect.Contains(python, "looks_like_postal_code");
        Expect.Contains(python, "looks_like_ssn");
        Expect.Contains(python, "must not be later than today.");
        Expect.Contains(python, "row.born_on > date.today()");
        Expect.Contains(python, "row.closed_on.date() > date.today()");

        string rust = await Rendered("RS_Validate_v1.tt", Person(), project);
        Expect.Contains(rust, "looks_like_postal_code");
        Expect.Contains(rust, "looks_like_ssn");
        Expect.Contains(rust, "*value > chrono::Local::now().date_naive()");
        Expect.Contains(rust, "value.date() > chrono::Local::now().date_naive()");
    }

    [TestMethod]
    public async Task The_python_and_rust_support_files_have_the_new_checks()
    {
        string python = await Rendered("PY_EssentialApp_v1.tt", Person(), Project());
        Expect.Contains(python, "def looks_like_postal_code(value: str) -> bool:");
        Expect.Contains(python, "def looks_like_ssn(value: str) -> bool:");
        string rust = await Rendered("RS_EssentialMain_v1.tt", Person(), Project());
        Expect.Contains(rust, "pub fn looks_like_postal_code(value: &str) -> bool");
        Expect.Contains(rust, "pub fn looks_like_ssn(value: &str) -> bool");
    }

    [TestMethod]
    public async Task The_angular_and_react_validators_check_the_new_shapes_and_the_future_date()
    {
        var project = Project(Future);
        string angular = await Rendered("TS_Validators_v1.tt", Person(), project);
        Expect.Contains(angular, "function notFuture(): ValidatorFn");
        Expect.Contains(angular, "bornOn: [notFuture()]");
        Expect.Contains(angular, "billingZip: [Validators.maxLength(20), Validators.pattern(/^(?:" + ColumnRules.PostalPattern + ")$/)]");

        string react = await Rendered("TSX_Schema_v1.tt", Person(), project);
        Expect.Contains(react, ".refine((value) => !value || value.substring(0, 10) <= new Date().toLocaleDateString(\"en-CA\"), \"Born On must not be later than today.\")");
        Expect.Contains(react, ".regex(/^(?:" + ColumnRules.SsnPattern + ")$/, \"Social Security Number is not a social security number.\")");
    }

    [TestMethod]
    public async Task The_web_forms_give_a_postal_code_and_a_social_security_number_their_pattern()
    {
        string react = await Rendered("TSX_Page_v1.tt", Person(), Project());
        Expect.Contains(react, "pattern=\"" + ColumnRules.PostalPattern + "\"");
        Expect.Contains(react, "pattern=\"" + ColumnRules.SsnPattern + "\"");
        Expect.Contains(react, "type=\"url\"");

        string blazor = await Rendered("BLZ_Page_v1.tt", Person(), Project());
        Expect.Contains(blazor, "\"" + ColumnRules.PostalPattern.Replace("\\", "\\\\") + "\" : null)\"");
        Expect.Contains(blazor, "\"" + ColumnRules.SsnPattern.Replace("\\", "\\\\") + "\" : null)\"");
    }

    // ------------------------------------------------------------------ the attributes run in the API, the WinUI3 forms check, and stored values can be checked

    [TestMethod]
    public async Task ApiAnnotations_writes_the_attributes_and_the_api_runs_them_before_it_saves()
    {
        var project = Project(("ApiAnnotations", "true"));
        CollectionAssert.Contains(project.ImpliedPlanTemplates.ToArray(), "CS_Validation");
        CollectionAssert.DoesNotContain(Project().ImpliedPlanTemplates.ToArray(), "CS_Validation");

        string metadata = await Rendered("CS_Validation_v1.tt", Person(), project);
        Expect.DoesNotContain(metadata, "[MetadataType");
        Expect.DoesNotContain(metadata, "public partial class Person");
        Expect.Contains(metadata, "public class PersonMetadata");
        Expect.Contains(await Rendered("CS_Validation_v1.tt", Person(), Project()), "[MetadataType(typeof(PersonMetadata))]");

        string program = await Rendered("API_EssentialProgram_v1.tt", Person(), project);
        Expect.Contains(program, "public static class Annotations");
        Expect.Contains(program, "Validator.TryValidateObject(row, new ValidationContext(row), results, validateAllProperties: true)");
        Expect.Contains(program, "new AssociatedMetadataTypeTypeDescriptionProvider(type, metadata)");
        Expect.DoesNotContain(await Rendered("API_EssentialProgram_v1.tt", Person(), Project()), "class Annotations");

        string crud = await CrudApiTests.Essential(("ApiAnnotations", "true"));
        Expect.Contains(crud, "if (Annotations.Check(newRow) is { } invalidNew)");
        Expect.Contains(crud, "if (Annotations.Check(updatedRow, await StoredAsync(context, id)) is { } invalidChange)");
        Assert.IsLessThan(crud.IndexOf("Annotations.Check(newRow)", StringComparison.Ordinal), crud.IndexOf("Stamp(newRow, true);", StringComparison.Ordinal), "the stamped dates are set before the attributes run");
        Expect.DoesNotContain(await CrudApiTests.Essential(), "Annotations.Check");
    }

    [TestMethod]
    public async Task The_winui3_forms_refuse_a_wrong_shape_and_a_future_date_before_they_save()
    {
        string detail = await Rendered("WinUI3_DetailScreen_v1.tt", Person(), Project(Future));
        Expect.Contains(detail, "if (!string.IsNullOrWhiteSpace(BillingZip) && !string.Equals(entity.BillingZip, BillingZip) && !(System.Text.RegularExpressions.Regex.IsMatch(BillingZip, @\"^(?:" + ColumnRules.PostalPattern + ")$\"))) { ErrorMessage = \"Billing Zip is not a postal code.\"; return false; }");
        Expect.Contains(detail, "{ ErrorMessage = \"Social Security Number is not a social security number.\"; return false; }");
        Expect.Contains(detail, "Uri.TryCreate(SiteLink, UriKind.Absolute, out var SiteLinkUri)");
        Expect.Contains(detail, "else if (BornOn.Value.Date > DateTime.Today && ((DateTime?)entity.BornOn)?.Date != BornOn.Value.Date) { ErrorMessage = \"Born On must not be later than today.\"; return false; }");
        Expect.DoesNotContain(await Rendered("WinUI3_DetailScreen_v1.tt", Person(), Project(("PlainTextColumns", "BillingZip"))), "is not a postal code");
    }

    [TestMethod]
    public void Stored_values_the_validators_would_refuse_are_found_with_a_few_examples()
    {
        var table = Sample.Table("Person",
        [
            Sample.Column("PersonId", SqlDbType.Int, primaryKey: true, identity: true, ordinal: 1),
            Sample.Column("Phone", SqlDbType.VarChar, nullable: true, characters: 30, ordinal: 2),
            Sample.Column("BillingZip", SqlDbType.VarChar, nullable: true, characters: 20, ordinal: 3),
            Sample.Column("ClosedOn", SqlDbType.DateTime, nullable: true, ordinal: 4)
        ],
        rows:
        [
            [1, "(281) 449-9332", "77249", null],
            [2, "call me", "T2H 2B7  ", DateTime.Today.AddDays(-1)],
            [3, "", "#", DateTime.Today.AddDays(5)],
            [4, null, null, null]
        ]);

        var findings = DataCheck.Of(table, Project(Future)).ToDictionary(f => f.Column);

        Assert.AreEqual(1, findings["Phone"].Count);
        CollectionAssert.AreEqual(new[] { "call me" }, findings["Phone"].Examples.ToArray());
        Assert.AreEqual("a phone number", findings["Phone"].Rule);
        Assert.AreEqual(1, findings["BillingZip"].Count, "a Canadian code with spaces around it passes");
        Assert.AreEqual(4, findings["BillingZip"].Total);
        Assert.AreEqual(1, findings["ClosedOn"].Count);
        Assert.AreEqual(3, findings.Count);
        Assert.IsEmpty(DataCheck.Of(table, Project(("PlainTextColumns", "Phone,BillingZip"))).Where(f => f.Column != "ClosedOn").ToList());
    }
}
