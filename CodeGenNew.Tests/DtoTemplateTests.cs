using System.Data;
using CodeGenNew.Core;
using CodeGenNew.TemplateEngine;

namespace CodeGenNew.Tests;

/// <summary> The data-transfer family: a reader-filled class, the mapper to the entity, and the data-contract, typed-DataSet and serialization shapes of the same table. </summary>
[TestClass]
public class DtoTemplateTests
{
    private static ProjectSettings Project(params (string Key, string Value)[] values) =>
        ProjectSettings.FromValues(values.Select(v => new KeyValuePair<string, string>(v.Key, v.Value)).Append(new("ProjectName", "Acme")));

    private static TableModel Customer() => Sample.Table("Customer",
    [
        Sample.Column("CustomerId", SqlDbType.Int, primaryKey: true, identity: true, ordinal: 1),
        Sample.Column("Name", SqlDbType.NVarChar, characters: 50, ordinal: 2),
        Sample.Column("CreditLimit", SqlDbType.Decimal, nullable: true, precision: 10, scale: 2, ordinal: 3),
        Sample.Column("IsTaxable", SqlDbType.Bit, ordinal: 4),
        Sample.Column("CreateDate", SqlDbType.DateTime, createDateColumn: true, ordinal: 5),
        Sample.Column("Photo", SqlDbType.VarBinary, nullable: true, ordinal: 6),
        Sample.Column("class", SqlDbType.VarChar, nullable: true, characters: 10, ordinal: 7)
    ]);

    private static async Task<string> Render(string template, TableModel table, ProjectSettings? project = null)
    {
        var result = await Repo.Cache.RunAsync(Repo.Template(template), table, project ?? Project());
        Assert.IsTrue(result.Success, $"{template}: {string.Join(" | ", result.Errors)}");
        return result.GeneratedText!.Replace("\r\n", "\n");
    }

    // ------------------------------------------------------------------ CS_Dto

    [TestMethod]
    public async Task The_transfer_class_reads_required_columns_then_optional_ones_by_name()
    {
        string cs = await Render("CS_Dto_v1.tt", Customer());

        Expect.Contains(cs, "namespace Acme.App.Dtos;");
        Expect.Contains(cs, "public class CustomerDto\n{");
        Expect.Contains(cs, "public int CustomerId { get; set; }");
        Expect.Contains(cs, "public string Name { get; set; } = \"\";");
        Expect.Contains(cs, "public decimal? CreditLimit { get; set; }");
        Expect.Contains(cs, "public byte[]? Photo { get; set; }");
        Expect.Contains(cs, "public string? @class { get; set; }");
        Expect.Contains(cs, "public CustomerDto(IDataRecord record)");
        Expect.Contains(cs, "// required\n        CustomerId = Convert.ToInt32(record.GetValue(record.GetOrdinal(\"CustomerId\")));");
        Expect.Contains(cs, "Name = Convert.ToString(record.GetValue(record.GetOrdinal(\"Name\")))!;");
        Expect.Contains(cs, "// optional: a NULL stays null\n        int ordinalCreditLimit = record.GetOrdinal(\"CreditLimit\");\n        CreditLimit = record.IsDBNull(ordinalCreditLimit) ? null : Convert.ToDecimal(record.GetValue(ordinalCreditLimit));");
        Expect.Contains(cs, "Photo = record.IsDBNull(ordinalPhoto) ? null : (byte[])record.GetValue(ordinalPhoto);");
    }

    [TestMethod]
    public async Task IsIdentical_compares_only_the_columns_that_matter_and_ToString_shows_the_name()
    {
        string cs = await Render("CS_Dto_v1.tt", Customer());

        Expect.Contains(cs, "public bool IsIdentical(CustomerDto? other)");
        Expect.Contains(cs, "return Equals(Name, other.Name) &&");
        Expect.Contains(cs, "Equals(CreditLimit, other.CreditLimit) &&");
        Expect.Contains(cs, "Equals(IsTaxable, other.IsTaxable) &&");
        Expect.Contains(cs, "System.Linq.Enumerable.SequenceEqual(Photo ?? [], other.Photo ?? [])");
        Expect.DoesNotContain(cs, "Equals(CustomerId, other.CustomerId)");   // the key
        Expect.DoesNotContain(cs, "Equals(CreateDate, other.CreateDate)");   // an audit column
        Expect.Contains(cs, "public override string ToString() => Name ?? \"\";");
    }

    [TestMethod]
    public async Task The_dto_namespace_follows_the_project()
    {
        Expect.Contains(await Render("CS_Dto_v1.tt", Customer(), Project(("DtoNamespace", "Shop.Transfer"))), "namespace Shop.Transfer;");
        Expect.Contains(await Render("CS_Dto_v1.tt", Customer(), ProjectSettings.None), "namespace MyApp.Dtos;");
    }

    // ------------------------------------------------------------------ CS_Mapper

    [TestMethod]
    public async Task The_mapper_copies_every_column_in_both_directions()
    {
        string cs = await Render("CS_Mapper_v1.tt", Customer(), Project(("EntityNamespace", "Acme.Api.Entities")));

        Expect.Contains(cs, "using Acme.Api.Entities;");
        Expect.Contains(cs, "namespace Acme.App.Dtos;");
        Expect.Contains(cs, "public static class CustomerMapping");
        Expect.Contains(cs, "public static CustomerDto? ToDto(Customer? entity)");
        Expect.Contains(cs, "return new CustomerDto\n        {\n            CustomerId = entity.CustomerId,\n            Name = entity.Name,");
        Expect.Contains(cs, "@class = entity.@class");
        Expect.Contains(cs, "public static Customer? ToEntity(CustomerDto? dto)");
        Expect.Contains(cs, "Name = dto.Name,");
        Expect.Contains(cs, "public static List<CustomerDto> ToDtos(IEnumerable<Customer>? entities) =>");
        Expect.Contains(cs, "public static List<Customer> ToEntities(IEnumerable<CustomerDto>? dtos) =>");
        Expect.Contains(cs, "FromReader(IDataRecord? record) => record is null ? null : new CustomerDto(record);");
    }

    [TestMethod]
    public async Task The_mapper_needs_a_key()
    {
        var keyless = Sample.Table("Log", [Sample.Column("Text", SqlDbType.NVarChar, characters: 50, ordinal: 1)]);

        var result = await Repo.Cache.RunAsync(Repo.Template("CS_Mapper_v1.tt"), keyless, Project());

        Assert.IsFalse(result.Success);
        Assert.IsTrue(result.Errors.Any(e => e.Contains("primary key")));
    }

    // ------------------------------------------------------------------ CS_DataContractDto

    [TestMethod]
    public async Task The_data_contract_has_a_member_per_column_and_four_constructors()
    {
        string cs = await Render("CS_DataContractDto_v1.tt", Customer());

        Expect.Contains(cs, "[DataContract]\npublic class CustomerContract");
        Expect.Contains(cs, "[DataMember(IsRequired = true)]\n    public int CustomerId { get; set; }");
        Expect.Contains(cs, "[DataMember]\n    public decimal? CreditLimit { get; set; }");
        Expect.Contains(cs, "public CustomerContract() { }");
        Expect.Contains(cs, "public CustomerContract(DataRow row)");
        Expect.Contains(cs, "CustomerId = Convert.ToInt32(row[\"CustomerId\"]);");
        Expect.Contains(cs, "CreditLimit = row[\"CreditLimit\"] is DBNull ? null : Convert.ToDecimal(row[\"CreditLimit\"]);");
        Expect.Contains(cs, "public CustomerContract(IDataRecord record)");
        Expect.Contains(cs, "Photo = record[\"Photo\"] is DBNull ? null : (byte[])record[\"Photo\"];");
        Expect.Contains(cs, "public CustomerContract(CustomerContract other)");
        Expect.Contains(cs, "Name = other.Name;");
    }

    // ------------------------------------------------------------------ CS_TypedDataRow

    [TestMethod]
    public async Task The_typed_table_declares_its_columns_and_binds_them_again_after_a_clone()
    {
        string cs = await Render("CS_TypedDataRow_v1.tt", Customer());

        Expect.Contains(cs, "public class CustomerDataTable : DataTable");
        Expect.Contains(cs, "public DataColumn CustomerIdColumn { get; private set; } = null!;");
        Expect.Contains(cs, "Columns.Add(new DataColumn(\"CustomerId\", typeof(int)) { AllowDBNull = false, AutoIncrement = true });");
        Expect.Contains(cs, "Columns.Add(new DataColumn(\"Name\", typeof(string)) { AllowDBNull = false, MaxLength = 50 });");
        Expect.Contains(cs, "Columns.Add(new DataColumn(\"CreditLimit\", typeof(decimal)) { AllowDBNull = true });");
        Expect.Contains(cs, "PrimaryKey = [Columns[\"CustomerId\"]!];");
        Expect.Contains(cs, "var clone = (CustomerDataTable)base.Clone();\n        clone.BindColumns();");
        Expect.Contains(cs, "protected override DataRow NewRowFromBuilder(DataRowBuilder builder) => new CustomerRow(builder);");
        Expect.Contains(cs, "public new CustomerRow NewRow() => (CustomerRow)base.NewRow();");
    }

    [TestMethod]
    public async Task The_typed_row_guards_only_the_columns_that_can_be_null()
    {
        string cs = await Render("CS_TypedDataRow_v1.tt", Customer());

        Expect.Contains(cs, "public class CustomerRow : DataRow");
        Expect.Contains(cs, "get => (int)this[_table.CustomerIdColumn];");
        Expect.Contains(cs, "get => (string)this[_table.NameColumn];");
        Expect.Contains(cs, "return (decimal)this[_table.CreditLimitColumn];");
        Expect.Contains(cs, "throw new StrongTypingException(\"Cannot get value because it is DBNull.\", e);");
        Expect.Contains(cs, "public bool IsCreditLimitNull() => IsNull(_table.CreditLimitColumn);");
        Expect.Contains(cs, "public void SetCreditLimitNull() => this[_table.CreditLimitColumn] = Convert.DBNull;");
        Expect.DoesNotContain(cs, "IsNameNull");
    }

    // ------------------------------------------------------------------ CS_SerializationDtos

    [TestMethod]
    public async Task The_three_contracts_are_side_by_side_in_one_file()
    {
        string cs = await Render("CS_SerializationDtos_v1.tt", Customer());

        Expect.Contains(cs, "[Serializable]\npublic class CustomerBasic");
        Expect.Contains(cs, "public class CustomerSerializable : ISerializable");
        Expect.Contains(cs, "protected CustomerSerializable(SerializationInfo info, StreamingContext context)");
        Expect.Contains(cs, "CustomerId = (int)info.GetValue(\"CustomerId\", typeof(int))!;");
        Expect.Contains(cs, "CreditLimit = (decimal?)info.GetValue(\"CreditLimit\", typeof(decimal?));");
        Expect.Contains(cs, "@class = (string?)info.GetValue(\"class\", typeof(string));");   // typeof cannot take string?
        Expect.Contains(cs, "info.AddValue(\"CreditLimit\", CreditLimit, typeof(decimal?));");
        Expect.Contains(cs, "[Serializable, XmlRoot(\"Customer\", IsNullable = false)]\npublic class CustomerXml");
        Expect.Contains(cs, "[XmlAttribute(\"CustomerId\")]\n    public int CustomerId { get; set; }");
        Expect.Contains(cs, "[XmlElement(\"Name\")]\n    public string Name { get; set; } = \"\";");
    }

    [TestMethod]
    public async Task A_computed_column_is_ignored_by_the_xml_contract()
    {
        var table = Sample.Table("Total",
        [
            Sample.Column("TotalId", SqlDbType.Int, primaryKey: true, ordinal: 1),
            Sample.Column("Gross", SqlDbType.Decimal, precision: 10, scale: 2, ordinal: 2, computed: true)
        ]);

        Expect.Contains(await Render("CS_SerializationDtos_v1.tt", table), "[XmlIgnore]\n    public decimal Gross { get; set; }");
    }

    // ------------------------------------------------------------------ the family as a whole

    [TestMethod]
    public void The_family_writes_its_own_file_names()
    {
        var offered = TemplateCatalog.Discover(Repo.TemplatesDirectory).ToDictionary(t => t.Name);

        Assert.AreEqual("CustomerDto.cs", offered["CS_Dto"].BuildFileName("Customer"));
        Assert.AreEqual("CustomerMapping.cs", offered["CS_Mapper"].BuildFileName("Customer"));
        Assert.AreEqual("CustomerContract.cs", offered["CS_DataContractDto"].BuildFileName("Customer"));
        Assert.AreEqual("CustomerDataTable.cs", offered["CS_TypedDataRow"].BuildFileName("Customer"));
        Assert.AreEqual("CustomerSerializationDtos.cs", offered["CS_SerializationDtos"].BuildFileName("Customer"));
    }
}
