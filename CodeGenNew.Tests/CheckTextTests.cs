using System.Data;
using System.Text.RegularExpressions;
using CodeGenNew.Core;

namespace CodeGenNew.Tests;

/// <summary> What a CHECK constraint says about the text of a column (its length or a LIKE), and a decimal limit wider than the decimal. </summary>
[TestClass]
public class CheckTextTests
{
    private static bool Whole(TextNotation notation, string value) => Regex.IsMatch(value, "^(?:" + notation.Pattern + ")$");

    private static ProjectSettings Project() => ProjectSettings.FromValues([new("ProjectName", "Acme")]);

    [TestMethod]
    [DataRow("([Code] like 'AB%')")]
    [DataRow("CHECK (((code)::text ~~ 'AB%'::text))")]
    [DataRow("(`code` like _utf8mb4'AB%')")]
    public void A_like_in_any_of_the_three_spellings_is_a_shape(string definition)
    {
        var notation = CheckTextParser.Parse(definition, "Code")!;

        Assert.IsTrue(Whole(notation, "AB"));
        Assert.IsTrue(Whole(notation, "AB123"));
        Assert.IsTrue(Whole(notation, "abXY"), "the databases compare a LIKE without regard to case");
        Assert.IsFalse(Whole(notation, "XAB"));
        Assert.IsFalse(Whole(notation, ""));
        Assert.AreEqual("AB", notation.Sample);
        Assert.IsTrue(Whole(notation, notation.Sample));
        Assert.IsFalse(notation.Noun.AsSpan().ContainsAny('{', '}'));
    }

    [TestMethod]
    [DataRow("(len([Code])=(5))")]
    [DataRow("CHECK ((length((code)::text) = 5))")]
    [DataRow("(char_length(`code`) = 5)")]
    public void An_exact_length_in_any_of_the_three_spellings_is_a_shape(string definition)
    {
        var notation = CheckTextParser.Parse(definition, "Code")!;

        Assert.IsTrue(Whole(notation, "12345"));
        Assert.IsTrue(Whole(notation, "ab\ncd"), "a line break is a character");
        Assert.IsFalse(Whole(notation, "1234"));
        Assert.IsFalse(Whole(notation, "123456"));
        Assert.AreEqual("text of 5 characters", notation.Noun);
        Assert.IsTrue(Whole(notation, notation.Sample));
    }

    [TestMethod]
    public void Two_limits_make_a_range_of_lengths_and_one_a_minimum_or_a_maximum()
    {
        var both = CheckTextParser.Parse("(len([c3])>=(3) AND len([c3])<=(8))", "c3")!;
        Assert.IsTrue(Whole(both, "abc"));
        Assert.IsTrue(Whole(both, "abcdefgh"));
        Assert.IsFalse(Whole(both, "ab"));
        Assert.IsFalse(Whole(both, "abcdefghi"));
        Assert.AreEqual("text of 3 to 8 characters", both.Noun);

        var min = CheckTextParser.Parse("(len([c])>(2))", "c")!;
        Assert.IsFalse(Whole(min, "ab"));
        Assert.IsTrue(Whole(min, "abc"));
        Assert.AreEqual("text of at least 3 characters", min.Noun);

        var max = CheckTextParser.Parse("(len([c])<(4))", "c")!;
        Assert.IsTrue(Whole(max, ""));
        Assert.IsFalse(Whole(max, "abcd"));
        Assert.AreEqual("text of at most 3 characters", max.Noun);
    }

    [TestMethod]
    public void A_like_with_an_underscore_a_dot_or_a_percent_inside_means_what_it_says()
    {
        var notation = CheckTextParser.Parse("([c] like 'A_C.%X')", "c")!;

        Assert.IsTrue(Whole(notation, "ABC.zzX"));
        Assert.IsFalse(Whole(notation, "ABCxzzX"), "a dot is a dot");
        Assert.IsFalse(Whole(notation, "AC.X"), "an underscore is one character");
        Assert.AreEqual("AAC.X", notation.Sample);
    }

    [TestMethod]
    [DataRow("(datalength([c4])>(2) OR [c4]='x')")]
    [DataRow("([c5] like '[A-Z]%')")]
    [DataRow("(len([other])=(5))")]
    [DataRow("(year([c])>=(2000))")]
    [DataRow("(len([c]) < [d])")]
    [DataRow("([c] like 'AB%' AND [d] > 3)")]
    [DataRow("(len([c])=(3) AND len([c])=(5))")]
    public void Anything_else_is_left_to_the_database(string definition) =>
        Assert.IsNull(CheckTextParser.Parse(definition, "c"), definition);

    // ---- what the column and its rule do with it ----

    private static ColumnModel Code(TextNotation? notation) =>
        Sample.Column("Code", SqlDbType.VarChar, characters: 10, ordinal: 2, checkNotation: notation);

    [TestMethod]
    public void A_text_column_with_such_a_check_is_held_to_it_by_every_validator_and_the_tests_use_a_value_that_has_it()
    {
        var column = Code(CheckTextParser.Parse("(len([Code])=(5))", "Code"));

        var rule = column.RuleOf(Project())!;
        Assert.AreEqual(TextShape.Custom, rule.Shape);
        Assert.AreEqual("text of 5 characters", ColumnRules.NounOf(rule));
        Assert.AreEqual("AAAAA", ApiSampleValues.Text(column));
        Assert.IsTrue(ApiSampleValues.HasFixedFormat(column, Project()));
        Expect.Contains(string.Join(" ", ValidationAttributes.Of(column, Project(), "Part")), "[RegularExpression(");
        Assert.IsNull(Code(null).RuleOf(Project())!.Pattern);
    }

    [TestMethod]
    public void A_decimal_limit_wider_than_the_decimal_is_the_limit_of_the_decimal()
    {
        var wide = Sample.Column("Rate", SqlDbType.Decimal, precision: 5, scale: 2, ordinal: 2, check: new CheckRange(-100000, false, 100000, false));
        var narrow = Sample.Column("Rate", SqlDbType.Decimal, precision: 5, scale: 2, ordinal: 2, check: new CheckRange(0, true, 500, false));
        var openEnded = Sample.Column("Rate", SqlDbType.Decimal, precision: 5, scale: 2, ordinal: 2, check: new CheckRange(0, false, null, false));

        var rule = wide.RuleOf(Project())!;
        Assert.AreEqual(-999.99, rule.Min);
        Assert.AreEqual(999.99, rule.Max);
        Assert.IsFalse(rule.MinStrict);
        var inside = narrow.RuleOf(Project())!;
        Assert.AreEqual((0.0, 500.0, true, false), (inside.Min, inside.Max, inside.MinStrict, inside.MaxStrict), "a limit inside the decimal is kept as it is, strict or not");
        var open = openEnded.RuleOf(Project())!;
        Assert.AreEqual((0.0, null), (open.Min, open.Max), "no upper limit stays none");
    }
}
