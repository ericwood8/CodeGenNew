using CodeGenNew.SchemaIntrospection;

namespace CodeGenNew.Tests;

[TestClass]
public class SystemTableFilterTests
{
    [TestMethod]
    [DataRow("scope_info")]
    [DataRow("scope_info_client")]
    [DataRow("scope_config")]
    [DataRow("scope_templates")]
    [DataRow("scope_parameters")]
    [DataRow("schema_info")]
    [DataRow("Order_Detail_tracking")]
    [DataRow("ORDER_TRACKING")]
    [DataRow("Customer_tracking")]
    public void Sync_framework_tables_are_left_out(string table) => Assert.IsTrue(table.IsSystemTable());

    [TestMethod]
    public void An_ordinary_table_is_kept() => Assert.IsFalse("Customer".IsSystemTable());
}
