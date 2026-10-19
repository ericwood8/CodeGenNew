using System.Data;
using CodeGenNew.Core;

namespace CodeGenNew.Tests;

/// <summary> The sentences and small rules the Python, Rust and C# emitters share (RuleMessages, the gate view in GatePlan, the per-language defaults). </summary>
[TestClass]
public class SharedEmitterTests
{
    [TestMethod]
    public void The_rule_sentences_are_one_text_for_every_stack()
    {
        Assert.AreEqual("Email must not be empty.", RuleMessages.Required("Email"));
        Assert.AreEqual("Name must be 40 characters or fewer.", RuleMessages.MaxLength("Name", 40));
        Assert.AreEqual("Kind must be one of: a, b.", RuleMessages.Choices("Kind", ["a", "b"]));
        Assert.AreEqual("Email must be " + ColumnRules.NounOf(TextShape.Email) + ".", RuleMessages.Shape("Email", TextShape.Email));
        Assert.AreEqual("Born must not be later than today.", RuleMessages.NotFuture("Born"));
        Assert.AreEqual("Age must be at least 0.", RuleMessages.AtLeast("Age", 0, false));
        Assert.AreEqual("Score must be greater than 0.5.", RuleMessages.AtLeast("Score", 0.5, true));
        Assert.AreEqual("Age must be at most 120.", RuleMessages.AtMost("Age", 120, false));
        Assert.AreEqual("Score must be less than 100.", RuleMessages.AtMost("Score", 100, true));
        Assert.AreEqual("looks_like_postal_code", RuleMessages.ShapeFunction(TextShape.Postal));
        Assert.AreEqual("looks_like_url", RuleMessages.ShapeFunction(TextShape.Url));
    }

    [TestMethod]
    public void A_guarded_column_has_left_its_default_as_each_language_writes_it()
    {
        var flag = Sample.Column("IsBad", SqlDbType.Bit);
        var text = Sample.Column("Code", SqlDbType.VarChar, characters: 10);
        var number = Sample.Column("Limit", SqlDbType.Int);
        var optionalNumber = Sample.Column("Cap", SqlDbType.Int, nullable: true);

        Assert.AreEqual("row.IsBad == true", GateCode.CSharpNotDefault(flag, "row.IsBad"));
        Assert.AreEqual("!string.IsNullOrEmpty(row.Code)", GateCode.CSharpNotDefault(text, "row.Code"));
        Assert.AreEqual("row.Limit != 0", GateCode.CSharpNotDefault(number, "row.Limit"));
        Assert.AreEqual("(row.Cap ?? 0) != 0", GateCode.CSharpNotDefault(optionalNumber, "row.Cap"));

        Assert.AreEqual("bool(row.is_bad)", PythonTable.NotDefault(flag, "row.is_bad"));
        Assert.AreEqual("bool(row.code)", PythonTable.NotDefault(text, "row.code"));
        Assert.AreEqual("row.limit != 0", PythonTable.NotDefault(number, "row.limit"));
        Assert.AreEqual("(row.cap or 0) != 0", PythonTable.NotDefault(optionalNumber, "row.cap"));
        Assert.AreEqual("False", PythonTable.DefaultValue(flag));
        Assert.AreEqual("\"\"", PythonTable.DefaultValue(text));
        Assert.AreEqual("0", PythonTable.DefaultValue(number));
        Assert.AreEqual("None", PythonTable.DefaultValue(optionalNumber));

        var rustFlag = new RustField(flag, "is_bad", "isBad", "bool", false, null, "");
        var rustText = new RustField(text, "code", "code", "Option<String>", true, null, "");
        var rustNumber = new RustField(number, "limit", "limit", "i32", false, null, "");
        Assert.AreEqual("row.is_bad", RustTable.NotDefault(rustFlag, "row.is_bad"));
        Assert.AreEqual("row.code.as_deref().is_some_and(|v| !v.is_empty())", RustTable.NotDefault(rustText, "row.code"));
        Assert.AreEqual("row.limit != 0", RustTable.NotDefault(rustNumber, "row.limit"));
        Assert.AreEqual("false", RustTable.DefaultValue(rustFlag));
        Assert.AreEqual("None", RustTable.DefaultValue(rustText));
        Assert.AreEqual("0", RustTable.DefaultValue(rustNumber));
    }

    [TestMethod]
    public void Python_and_Rust_conditions_read_the_same_term()
    {
        var table = Sample.Table("Invoice", [Sample.Column("InvoiceId", SqlDbType.Int, primaryKey: true, identity: true), Sample.Column("StatusId", SqlDbType.Int), Sample.Column("Cap", SqlDbType.Int, nullable: true)]);
        var status = table.Columns[1];
        var cap = table.Columns[2];
        var inTerm = new GateTerm(table, null, status, GateOperator.In, ["2", "3"]);
        var lessTerm = new GateTerm(table, null, cap, GateOperator.Less, ["5"]);

        Assert.AreEqual("row.status_id in (2, 3)", GateCode.PythonTest("row.status_id", inTerm));
        Assert.AreEqual("[2, 3].contains(&row.status_id)", GateCode.RustTest("row.status_id", inTerm, RustGateKind.Int, false));
        Assert.AreEqual("row.status_id is 2 or 3", GateCode.Test("row.status_id", inTerm));
        Assert.AreEqual("(row.cap is not None and row.cap < 5)", GateCode.PythonTest("row.cap", lessTerm));
        Assert.AreEqual("row.cap.is_some_and(|v| v < 5)", GateCode.RustTest("row.cap", lessTerm, RustGateKind.Int, true));
    }
}
