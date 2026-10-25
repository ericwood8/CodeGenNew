using System.Data;
using CodeGenNew.Core;

namespace CodeGenNew.Tests;

/// <summary> A foreign key to a column that is not the parent's primary key gets no navigation and no drop-down: EF Core would make that column a required key. </summary>
[TestClass]
public class AlternateKeyTests
{
    private static TableModel Well(bool referencesPrimaryKey)
    {
        var fk = new ForeignKeyModel
        {
            ConstraintName = "FK_Well_Station",
            ReferencingColumns = ["PrimaryExternStationID"],
            ReferencedSchema = "dbo",
            ReferencedTable = "Station",
            ReferencedColumns = ["ExternStationID"],
            ReferencedDisplayColumns = ["StationName"],
            ReferencesPrimaryKey = referencesPrimaryKey
        };
        return Sample.Table("Well",
        [
            Sample.Column("WellID", SqlDbType.Int, primaryKey: true, identity: true, ordinal: 1),
            Sample.Column("PrimaryExternStationID", SqlDbType.NVarChar, nullable: true, characters: 6, ordinal: 2)
        ], [fk]);
    }

    [TestMethod]
    public void A_key_to_an_alternate_key_has_no_navigation()
    {
        Assert.IsEmpty(EntityNavigations.Of(Well(false), _ => false));
        Assert.HasCount(1, EntityNavigations.Of(Well(true), _ => false));
    }

    [TestMethod]
    public void A_key_to_an_alternate_key_has_no_drop_down()
    {
        var project = ProjectSettings.FromValues([new("ProjectName", "Acme")]);
        var well = Well(false);
        Assert.IsNull(FormModel.For(well, project).LookupOf(well.Columns[1]));
        well = Well(true);
        Assert.IsNotNull(FormModel.For(well, project).LookupOf(well.Columns[1]));
    }
}
