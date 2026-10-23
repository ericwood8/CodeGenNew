namespace CodeGenNew.Core;

/// <summary> The sentences a column's rules answer with (<see cref="ColumnRules"/>), and the names of the shape checks, so every stack that writes the rules as code says the same thing. A <paramref name="label"/>
/// is the column's words (<see cref="Labels.Words"/>). </summary>
public static class RuleMessages
{
    public static string Required(string label) => label + " must not be empty.";

    public static string MaxLength(string label, int maxLength) => label + " must be " + maxLength + " characters or fewer.";

    public static string Choices(string label, IEnumerable<string> choices) => label + " must be one of: " + string.Join(", ", choices) + ".";

    public static string Shape(string label, TextShape shape) => label + " must be " + ColumnRules.NounOf(shape) + ".";

    public static string Shape(string label, ColumnRule rule) => label + " must be " + ColumnRules.NounOf(rule) + ".";

    public static string NotFuture(string label) => label + " must not be later than today.";

    public static string AtLeast(string label, double min, bool strict) => label + " must be " + (strict ? "greater than " : "at least ") + TemplateHelpers.Num(min) + ".";

    public static string AtMost(string label, double max, bool strict) => label + " must be " + (strict ? "less than " : "at most ") + TemplateHelpers.Num(max) + ".";

    /// <summary> The name of the support function that checks a text shape in the Python and Rust stacks (<c>looks_like_email</c>). </summary>
    public static string ShapeFunction(TextShape shape) => shape switch
    {
        TextShape.Email => "looks_like_email",
        TextShape.Phone => "looks_like_phone",
        TextShape.Postal => "looks_like_postal_code",
        TextShape.Ssn => "looks_like_ssn",
        _ => "looks_like_url"
    };
}
