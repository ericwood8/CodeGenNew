using System.Data;
using CodeGenNew.Core;

namespace CodeGenNew.Tests;

/// <summary> What a table says it is: its words, its column conversion and the shapes it has for the project's settings. </summary>
[TestClass]
public class TableShapesTests
{
    private static ProjectSettings Project(params (string Key, string Value)[] values) =>
        ProjectSettings.FromValues(values.Select(v => new KeyValuePair<string, string>(v.Key, v.Value)).Append(new("ProjectName", "Acme")));

    private static TableModel Customer() => Sample.Table("E_SalesCustomer",
    [
        Sample.Column("SalesCustomerId", SqlDbType.Int, primaryKey: true, identity: true, ordinal: 1),
        Sample.Column("Name", SqlDbType.NVarChar, characters: 50, ordinal: 2),
        Sample.Column("IsDeleted", SqlDbType.Bit, ordinal: 3)
    ], []);

    [TestMethod]
    public void The_table_and_its_columns_give_their_words_in_lower_case()
    {
        var table = Customer();
        Assert.AreEqual("sales customer", table.Words);
        Assert.AreEqual("sales customers", table.PluralWords);
        Assert.AreEqual("sales customer id", table.Columns[0].Words);
        Assert.AreEqual("is deleted", table.Columns[2].Words);
    }

    [TestMethod]
    public void A_table_has_only_the_shapes_the_project_lists_it_for()
    {
        var table = Customer();
        var plain = TableShapes.For(table, Project());
        Assert.IsFalse(plain.Has<SoftDeleteTable>());
        Assert.IsFalse(plain.Has<QueueShape>());
        Assert.IsFalse(plain.Has<TenantOwned>());

        var soft = TableShapes.For(table, Project(("SoftDeleteTables", "E_SalesCustomer")));
        Assert.IsTrue(soft.Has<SoftDeleteTable>());
        Assert.IsFalse(soft.Has<QueueShape>());
    }

    [TestMethod]
    public void A_table_whose_key_is_also_its_foreign_key_extends_that_table()
    {
        var profile = Sample.Table("CustomerProfile",
            [Sample.Column("CustomerId", SqlDbType.Int, primaryKey: true, ordinal: 1), Sample.Column("Language", SqlDbType.NVarChar, characters: 10, ordinal: 2)],
            [Sample.ForeignKey("CustomerId", "Customer", "CustomerId", "Name")]);
        var extension = TableShapes.For(profile, Project()).Extension;
        Assert.IsNotNull(extension);
        Assert.AreEqual("CustomerId", extension.Key.Name);
        Assert.AreEqual("Customer", extension.Base.ReferencedTable);
    }

    [TestMethod]
    public void A_table_with_its_own_key_or_a_composite_key_or_a_key_to_itself_extends_nothing()
    {
        var own = Sample.Table("Note",
            [Sample.Column("NoteId", SqlDbType.Int, primaryKey: true, identity: true, ordinal: 1), Sample.Column("CustomerId", SqlDbType.Int, ordinal: 2)],
            [Sample.ForeignKey("CustomerId", "Customer", "CustomerId", "Name")]);
        var composite = Sample.Table("CustomerTag",
            [Sample.Column("CustomerId", SqlDbType.Int, primaryKey: true, ordinal: 1), Sample.Column("TagId", SqlDbType.Int, primaryKey: true, ordinal: 2)],
            [Sample.ForeignKey("CustomerId", "Customer", "CustomerId", "Name"), Sample.ForeignKey("TagId", "Tag", "TagId", "Name")]);
        var itself = Sample.Table("Node",
            [Sample.Column("NodeId", SqlDbType.Int, primaryKey: true, ordinal: 1)],
            [Sample.ForeignKey("NodeId", "Node", "NodeId", "NodeId")]);
        Assert.IsNull(TableShapes.For(own, Project()).Extension);
        Assert.IsNull(TableShapes.For(composite, Project()).Extension);
        Assert.IsNull(TableShapes.For(itself, Project()).Extension);
    }

    [TestMethod]
    public void A_shape_is_worked_out_once_and_kept()
    {
        var shapes = TableShapes.For(Customer(), Project(("SoftDeleteTables", "E_SalesCustomer")));
        Assert.AreSame(shapes.Shape<SoftDeleteTable>(), shapes.Shape<SoftDeleteTable>());
    }

    [TestMethod]
    public void A_type_that_is_not_a_shape_is_refused()
    {
        Assert.ThrowsExactly<ArgumentException>(() => TableShapes.For(Customer(), Project()).Shape<string>());
    }

    private static TableModel Category() => Sample.Table("Category",
    [
        Sample.Column("CategoryId", SqlDbType.Int, primaryKey: true, identity: true, ordinal: 1),
        Sample.Column("Name", SqlDbType.NVarChar, characters: 50, ordinal: 2),
        Sample.Column("ParentCategoryId", SqlDbType.Int, nullable: true, ordinal: 3),
        Sample.Column("IsDeleted", SqlDbType.Bit, ordinal: 4)
    ],
    [Sample.ForeignKey("ParentCategoryId", "Category", "CategoryId", "Name")]);

    [TestMethod]
    public void A_table_can_be_a_tree_and_soft_deleted_at_once()
    {
        var shapes = TableShapes.For(Category(), Project(("TreeTables", "Category:ParentCategoryId:3"), ("SoftDeleteTables", "Category")));
        Assert.IsTrue(shapes.Has<TreeTable>());
        Assert.IsTrue(shapes.Has<SoftDeleteTable>());
        Assert.IsFalse(shapes.Has<QueueShape>());
        Assert.IsTrue(shapes.All.Any(x => x is TreeTable));
        Assert.IsTrue(shapes.All.Any(x => x is SoftDeleteTable));
    }

    [TestMethod]
    public void A_table_with_no_listed_shape_has_an_empty_list()
    {
        Assert.AreEqual(0, TableShapes.For(Category(), Project()).All.Count);
    }

    private static DatabaseModel Tenants() => new()
    {
        DatabaseName = "Acme", SchemaName = "dbo",
        Tables =
        [
            Sample.Table("Tenant",
            [
                Sample.Column("TenantId", SqlDbType.Int, primaryKey: true, identity: true, ordinal: 1),
                Sample.Column("Name", SqlDbType.NVarChar, characters: 50, ordinal: 2),
                Sample.Column("IsActive", SqlDbType.Bit, ordinal: 3)
            ], []),
            Sample.Table("Project",
            [
                Sample.Column("ProjectId", SqlDbType.Int, primaryKey: true, identity: true, ordinal: 1),
                Sample.Column("TenantId", SqlDbType.Int, ordinal: 2),
                Sample.Column("Name", SqlDbType.NVarChar, characters: 50, ordinal: 3),
                Sample.Column("IsActive", SqlDbType.Bit, ordinal: 4),
                Sample.Column("IsDeleted", SqlDbType.Bit, ordinal: 5)
            ],
            [Sample.ForeignKey("TenantId", "Tenant", "TenantId", "Name")]),
            Sample.Table("UserTenant",
            [
                Sample.Column("UserTenantId", SqlDbType.Int, primaryKey: true, identity: true, ordinal: 1),
                Sample.Column("UserId", SqlDbType.Int, ordinal: 2),
                Sample.Column("TenantId", SqlDbType.Int, ordinal: 3)
            ],
            [Sample.ForeignKey("TenantId", "Tenant", "TenantId", "Name")])
        ]
    };

    private static ProjectSettings TenantProject() => Project(("ApiAuth", "true"), ("TenantTable", "Tenant"), ("TenantMembership", "UserTenant:UserId:TenantId"), ("SoftDeleteTables", "Project"));

    [TestMethod]
    public void A_tenant_owned_table_is_also_soft_deleted_and_has_a_name_and_an_active_flag()
    {
        var database = Tenants();
        var shapes = TableShapes.For(database.Tables[1], TenantProject(), database);
        Assert.IsTrue(shapes.Has<TenantOwned>());
        Assert.AreEqual("Tenant", shapes.Tenant!.TenantTable);
        Assert.IsTrue(shapes.Has<SoftDeleteTable>());
        Assert.IsTrue(shapes.Has<NameActivePlan>());
        Assert.IsFalse(shapes.Has<TenantTableShape>());
        Assert.IsFalse(shapes.Has<TenantMembership>());
    }

    [TestMethod]
    public void The_tenant_table_and_the_membership_table_are_told_apart()
    {
        var database = Tenants();
        var tenant = TableShapes.For(database.Tables[0], TenantProject(), database);
        Assert.IsTrue(tenant.Has<TenantTableShape>());
        Assert.IsFalse(tenant.Has<TenantOwned>());
        var membership = TableShapes.For(database.Tables[2], TenantProject(), database);
        Assert.IsTrue(membership.Has<TenantMembership>());
        Assert.IsFalse(membership.Has<TenantTableShape>());
    }

    [TestMethod]
    public void A_shape_that_needs_another_table_is_refused_without_the_database()
    {
        var database = Tenants();
        Assert.ThrowsExactly<InvalidOperationException>(() => TableShapes.For(database.Tables[2], TenantProject()).Has<TenantMembership>());
    }

    private static DatabaseModel Approvals() => new()
    {
        DatabaseName = "Acme", SchemaName = "dbo",
        Tables =
        [
            Sample.Table("Employee",
            [
                Sample.Column("EmployeeId", SqlDbType.Int, primaryKey: true, identity: true, ordinal: 1),
                Sample.Column("UserName", SqlDbType.NVarChar, characters: 50, ordinal: 2),
                Sample.Column("PasswordHash", SqlDbType.NVarChar, characters: 200, ordinal: 3),
                Sample.Column("IsManager", SqlDbType.Bit, ordinal: 4)
            ], []),
            Sample.Table("Project",
            [
                Sample.Column("ProjectId", SqlDbType.Int, primaryKey: true, identity: true, ordinal: 1),
                Sample.Column("Name", SqlDbType.NVarChar, characters: 50, ordinal: 2)
            ], []),
            Sample.Table("ProjectApproval",
            [
                Sample.Column("ProjectApprovalId", SqlDbType.Int, primaryKey: true, identity: true, ordinal: 1),
                Sample.Column("ProjectId", SqlDbType.Int, ordinal: 2),
                Sample.Column("ApprovalStatusId", SqlDbType.Int, ordinal: 3),
                Sample.Column("Reason", SqlDbType.NVarChar, nullable: true, characters: 500, ordinal: 4),
                Sample.Column("RequestedBy", SqlDbType.NVarChar, characters: 50, ordinal: 5),
                Sample.Column("RequestedOn", SqlDbType.DateTime, ordinal: 6),
                Sample.Column("DecidedBy", SqlDbType.NVarChar, nullable: true, characters: 50, ordinal: 7),
                Sample.Column("DecidedOn", SqlDbType.DateTime, nullable: true, ordinal: 8),
                Sample.Column("DecisionNote", SqlDbType.NVarChar, nullable: true, characters: 500, ordinal: 9),
                Sample.Column("Revision", SqlDbType.Int, ordinal: 10)
            ],
            [Sample.ForeignKey("ProjectId", "Project", "ProjectId", "Name")])
        ]
    };

    private static ProjectSettings ApprovalProject() => Project(("ApiAuth", "true"), ("UserTable", "Employee"), ("UserRoleColumn", "IsManager"), ("RowApprovals", "Project"));

    [TestMethod]
    public void An_approval_table_and_its_side_table_are_told_apart()
    {
        var database = Approvals();
        var project = ApprovalProject();
        var table = TableShapes.For(database.Tables[1], project, database);
        Assert.IsTrue(table.Has<ApprovalTable>());
        Assert.IsFalse(table.Has<ApprovalSide>());
        Assert.AreEqual("ProjectApproval", table.Shape<ApprovalTable>()!.Side.TableName);

        var side = TableShapes.For(database.Tables[2], project, database);
        Assert.IsTrue(side.Has<ApprovalSide>());
        Assert.IsFalse(side.Has<ApprovalTable>());
        Assert.AreEqual("Project", side.Shape<ApprovalSide>()!.Approval.Table.TableName);
    }

    [TestMethod]
    public void The_user_table_has_the_user_shape_and_a_plain_table_has_no_approval()
    {
        var database = Approvals();
        var user = TableShapes.For(database.Tables[0], ApprovalProject(), database);
        Assert.IsTrue(user.Has<UserTablePlan>());
        Assert.IsFalse(user.Has<ApprovalTable>());
        Assert.IsFalse(TableShapes.For(database.Tables[1], Project(), database).Has<ApprovalTable>());
    }

    [TestMethod]
    public void Every_caller_for_the_same_table_and_project_gets_the_same_shapes()
    {
        var table = Customer();
        var project = Project();
        Assert.AreSame(TableShapes.For(table, project), TableShapes.For(table, project));
        Assert.AreNotSame(TableShapes.For(table, project), TableShapes.For(table, Project()));
    }
}
