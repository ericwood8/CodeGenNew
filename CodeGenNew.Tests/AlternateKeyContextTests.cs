using System.Data;
using CodeGenNew.Core;

namespace CodeGenNew.Tests;

/// <summary> A foreign key to a column that is not the parent's primary key must be named in the context, or EF Core refuses the model. </summary>
[TestClass]
public class AlternateKeyContextTests
{
    private static DatabaseModel Wells() => new()
    {
        DatabaseName = "Acme",
        SchemaName = "dbo",
        Dialect = SqlDialect.SqlServer,
        Tables =
        [
            Sample.Table("Station",
            [
                Sample.Column("StationID", SqlDbType.Int, primaryKey: true, identity: true, ordinal: 1),
                Sample.Column("ExternStationID", SqlDbType.NVarChar, nullable: true, characters: 6, ordinal: 2)
            ]),
            Sample.Table("Well",
            [
                Sample.Column("WellID", SqlDbType.Int, primaryKey: true, identity: true, ordinal: 1),
                Sample.Column("PrimaryExternStationID", SqlDbType.NVarChar, nullable: true, characters: 6, ordinal: 2),
                Sample.Column("StationID", SqlDbType.Int, nullable: true, ordinal: 3)
            ],
            [
                Sample.ForeignKey("PrimaryExternStationID", "Station", "ExternStationID", "ExternStationID"),
                Sample.ForeignKey("StationID", "Station", "StationID", "ExternStationID")
            ])
        ]
    };

    [TestMethod]
    public async Task A_foreign_key_to_an_alternate_key_names_the_principal_key()
    {
        var result = await Repo.Cache.RunAsync(Repo.Template("CS_DbContext_v1.tt"), Wells(), ProjectSettings.FromValues([new("ProjectName", "Acme")]));
        Assert.IsTrue(result.Success, string.Join(" | ", result.Errors));
        string cs = result.GeneratedText!;
        Expect.Contains(cs, ".HasForeignKey(e => e.PrimaryExternStationID).HasPrincipalKey(p => p.ExternStationID);");
        Expect.DoesNotContain(cs, "HasForeignKey(e => e.StationID)");
    }
}
