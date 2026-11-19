using System.Data;
using CodeGenNew.Core;

namespace CodeGenNew.Tests.Shapes;

/// <summary> The shapes a project's settings give a table, asked through TableShapes: the shape sits on the table the setting names and on no other. </summary>
[TestClass]
public class TableShapesFromSettingsTests
{
    private static ProjectSettings Project(params (string Key, string Value)[] values) =>
        ProjectSettings.FromValues(new Dictionary<string, string> { ["ProjectName"] = "Shop", ["AccessMode"] = "Ef" }.Concat(values.Select(v => KeyValuePair.Create(v.Key, v.Value))).ToDictionary());

    private static ColumnModel Key(string name) => Sample.Column(name, SqlDbType.Int, primaryKey: true, identity: true, ordinal: 1);

    private static DatabaseModel Database(params TableModel[] tables) => new() { DatabaseName = "Shop", SchemaName = "dbo", Tables = [.. tables] };

    [TestMethod]
    public void A_table_the_project_lists_as_a_default_row_table_has_that_shape()
    {
        var address = Sample.Table("CustomerAddress",
        [
            Key("CustomerAddressId"), Sample.Column("CustomerId", SqlDbType.Int, ordinal: 2), Sample.Column("AddressType", SqlDbType.VarChar, characters: 10, ordinal: 3),
            Sample.Column("IsDefault", SqlDbType.Bit, ordinal: 4)
        ]);
        var project = Project(("DefaultRowTables", "CustomerAddress:IsDefault:CustomerId+AddressType"));
        Assert.IsNotNull(TableShapes.For(address, project).DefaultRow);
        Assert.IsNull(TableShapes.For(address, Project()).DefaultRow);
    }

    private static TableModel Categories() => Sample.Table("TaxCategory",
    [
        Key("TaxCategoryId"), Sample.Column("Code", SqlDbType.VarChar, characters: 10, ordinal: 2), Sample.Column("Rate", SqlDbType.Decimal, ordinal: 3),
        Sample.Column("ValidFrom", SqlDbType.Date, ordinal: 4), Sample.Column("ValidTo", SqlDbType.Date, nullable: true, ordinal: 5)
    ]);

    private static TableModel InvoiceLines() => Sample.Table("InvoiceLine",
    [
        Key("InvoiceLineId"), Sample.Column("TaxCategoryId", SqlDbType.Int, ordinal: 2), Sample.Column("TaxRate", SqlDbType.Decimal, ordinal: 3), Sample.Column("LineTotal", SqlDbType.Decimal, ordinal: 4),
        Sample.Column("TaxAmount", SqlDbType.Decimal, ordinal: 5), Sample.Column("LineDate", SqlDbType.Date, ordinal: 6), Sample.Column("InvoiceId", SqlDbType.Int, ordinal: 7)
    ]);

    [TestMethod]
    public void A_tax_line_table_and_the_category_table_it_names_are_told_apart()
    {
        var categories = Categories();
        var lines = InvoiceLines();
        var database = Database(categories, lines);
        var project = Project(("TaxLines", "InvoiceLine:TaxCategoryId:TaxRate:LineTotal:TaxAmount:TaxCategory"));
        Assert.IsNotNull(TableShapes.For(lines, project, database).TaxLine);
        Assert.IsNull(TableShapes.For(categories, project, database).TaxLine);
        Assert.IsNotNull(TableShapes.For(categories, project, database).TaxCategory);
        Assert.IsNull(TableShapes.For(lines, project, database).TaxCategory);
        Assert.IsNull(TableShapes.For(lines, Project(), database).TaxLine);
    }

    private static TableModel Keyed(string name) => Sample.Table(name, [Key(name + "Id"), Sample.Column("Name", SqlDbType.NVarChar, characters: 100, ordinal: 2)]);

    private static TableModel Attachment() => Sample.Table("Attachment",
    [
        Key("AttachmentId"), Sample.Column("OwnerType", SqlDbType.NVarChar, characters: 30, ordinal: 2), Sample.Column("OwnerId", SqlDbType.Int, ordinal: 3), Sample.Column("Title", SqlDbType.NVarChar, characters: 100, ordinal: 4)
    ]);

    [TestMethod]
    public void A_polymorphic_link_table_and_the_tables_it_points_at_are_told_apart()
    {
        var product = Keyed("Product");
        var customer = Keyed("Customer");
        var attachment = Attachment();
        var database = Database(product, customer, attachment);
        var project = Project(("PolymorphicLinks", "Attachment:OwnerType:OwnerId:Product|Customer"));
        Assert.IsNotNull(TableShapes.For(attachment, project, database).Link);
        Assert.IsNull(TableShapes.For(attachment, project, database).Linked);
        Assert.IsNotNull(TableShapes.For(product, project, database).Linked);
        Assert.IsNull(TableShapes.For(product, project, database).Link);
        Assert.IsNotNull(TableShapes.For(customer, project, database).Linked);
        Assert.IsNull(TableShapes.For(attachment, Project(), database).Link);
    }

    [TestMethod]
    public void A_party_table_and_its_identifier_table_are_told_apart()
    {
        var party = Sample.Table("Party", [Key("PartyId"), Sample.Column("LegalName", SqlDbType.VarChar, characters: 100, ordinal: 2)]);
        var identifier = Sample.Table("PartyIdentifier",
        [
            Key("PartyIdentifierId"), Sample.Column("PartyId", SqlDbType.Int, ordinal: 2), Sample.Column("IssuerPartyId", SqlDbType.Int, nullable: true, ordinal: 3), Sample.Column("Scheme", SqlDbType.VarChar, characters: 20, ordinal: 4),
            Sample.Column("Value", SqlDbType.VarChar, characters: 50, ordinal: 5), Sample.Column("Sequence", SqlDbType.Int, ordinal: 6)
        ], [Sample.ForeignKey("PartyId", "Party", "PartyId")]);
        var database = Database(party, identifier);
        var project = Project(("PartyTables", "Party:PartyIdentifier"));
        Assert.IsNotNull(TableShapes.For(party, project, database).PartyRoot);
        Assert.IsNull(TableShapes.For(party, project, database).PartyIdentifiers);
        Assert.IsNotNull(TableShapes.For(identifier, project, database).PartyIdentifiers);
        Assert.IsNull(TableShapes.For(identifier, project, database).PartyRoot);
    }

    [TestMethod]
    public void A_junction_table_has_the_junction_shape_and_a_table_with_its_own_key_does_not()
    {
        var student = Keyed("Student");
        var course = Keyed("Course");
        var link = Sample.Table("StudentCourse",
        [
            Sample.Column("StudentId", SqlDbType.Int, primaryKey: true, ordinal: 1), Sample.Column("CourseId", SqlDbType.Int, primaryKey: true, ordinal: 2)
        ], [Sample.ForeignKey("StudentId", "Student", "StudentId", "Name"), Sample.ForeignKey("CourseId", "Course", "CourseId", "Name")]);
        Assert.IsNotNull(TableShapes.For(link, Project()).Shape<JunctionShape>());
        Assert.IsNull(TableShapes.For(student, Project()).Shape<JunctionShape>());
        Assert.IsNull(TableShapes.For(course, Project()).Shape<JunctionShape>());
    }
}
