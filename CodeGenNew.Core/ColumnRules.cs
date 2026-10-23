using System.Data;

namespace CodeGenNew.Core;

/// <summary> A text column whose name says what it holds, so its value can be checked for that shape. </summary>
public enum TextShape { None, Email, Phone, Url, Postal, Ssn, Custom }

/// <summary> What the schema says a column's value must be: the one fact list behind the validators of every language (FluentValidation, zod, Angular). </summary>
public sealed record ColumnRule
{
    /// <summary> Text that cannot be NULL must not be empty. </summary>
    public bool Required { get; init; }
    /// <summary> The longest text in characters; 0 for no limit. </summary>
    public int MaxLength { get; init; }
    /// <summary> The only values a text column accepts (a CHECK list or an enum); null for any text. </summary>
    public IReadOnlyList<string>? Choices { get; init; }
    public TextShape Shape { get; init; }
    /// <summary> For <see cref="TextShape.Custom"/>: the regular expression the whole text must match (an array, interval, range or shape of PostgreSQL), and what a value is called in a message. </summary>
    public string? Pattern { get; init; }
    public string? Noun { get; init; }
    public double? Min { get; init; }
    public bool MinStrict { get; init; }
    public double? Max { get; init; }
    public bool MaxStrict { get; init; }

    public bool HasRange => Min is not null || Max is not null;

    /// <summary> A date that can never be later than today (the project's <c>NotFutureDateColumns</c>). </summary>
    public bool NotFuture { get; init; }
}

public static class ColumnRules
{
    /// <summary> Columns the database or the repository fills in, so a person is never asked for them. </summary>
    public static bool IsSystemFilled(this ColumnModel column) => column.IsIdentity || column.IsComputed || column.IsAuditColumn || column.IsStampColumn;

    /// <summary> The rule for a column, or null when the schema says nothing about it or the system fills it in. A decimal or float keeps to its CHECK range, and failing that a latitude or
    /// longitude by name keeps to its degrees; a whole number keeps to the range its name or CHECK suggests. A text column the project lists in <c>PlainTextColumns</c> gets no format
    /// check; <paramref name="table"/> lets that list name one table's column. </summary>
    public static ColumnRule? RuleOf(this ColumnModel column, ProjectSettings project, string? table = null)
    {
        if (column.IsSystemFilled())
            return null;

        ColumnRule? rule = null;
        if (column.IsStringColumn)
        {
            var shape = column.ShapeOf(project, table);

            var text = column.Notation;
            rule = new ColumnRule
            {
                Required = !column.IsNullable && column.SetValues is null,   // no member chosen is an empty set, a value
                MaxLength = Math.Max(0, column.CharacterLength),
                Choices = column.HasChoices ? column.Choices : null,
                Shape = text is null ? shape : TextShape.Custom,
                Pattern = text?.Pattern,
                Noun = text?.Noun
            };
        }
        else if (column.IsIntegerColumn && (column.NumericKind != NumericKind.None || column.Check is not null) && project.RangeFor(column) is { } range)
        {
            rule = new ColumnRule { Min = range.Min, Max = range.Max };
        }
        else if (column.IsNumericColumn || column.IsMoneyColumn)
        {
            if (column.Check is { } check)
                rule = new ColumnRule { Min = check.Min, MinStrict = check.MinStrict, Max = check.Max, MaxStrict = check.MaxStrict };
            else if (Names(column, "Latitude"))
                rule = new ColumnRule { Min = -90, Max = 90 };
            else if (Names(column, "Longitude"))
                rule = new ColumnRule { Min = -180, Max = 180 };
        }

        // A column the project says is never negative keeps to 0 or more, whatever the schema says about its upper limit.
        if ((column.IsIntegerColumn || column.IsNumericColumn || column.IsMoneyColumn) && project.IsNonNegative(table ?? "", column.Name) && !(rule?.Min >= 0))
            rule = (rule ?? new ColumnRule()) with { Min = 0, MinStrict = false };

        // a date column the project says is never in the future (no schema can say so)
        if (TemplateHelpers.IsDate(column) && project.IsNotFuture(table ?? "", column.Name))
            rule = (rule ?? new ColumnRule()) with { NotFuture = true };

        bool says = rule is not null && (rule.NotFuture || rule.Required || rule.MaxLength > 0 || rule.Choices is not null || rule.Shape != TextShape.None || rule.HasRange);
        return says ? rule : null;
    }

    /// <summary> The type of the text box a web form gives a text column: <c>email</c>, <c>tel</c> or <c>url</c> when the column's name says so (the browser then refuses a value of another shape when
    /// the form is submitted), otherwise <c>text</c>. </summary>
    public static string InputType(this ColumnModel column, ProjectSettings project, string table) => column.RuleOf(project, table)?.Shape switch
    {
        TextShape.Email => "email",
        TextShape.Phone => "tel",
        TextShape.Url => "url",
        _ => "text"
    };

    /// <summary> The phone number text, as an HTML <c>pattern</c> (also valid with the unicode-sets flag browsers use) and the <c>title</c> shown with it; the validators of every language use the same characters. </summary>
    public const string PhonePattern = @"[0-9+\(\)\-.\s]{3,25}";

    /// <summary> A postal code: 3 to 10 letters, digits, spaces or hyphens (a US ZIP or ZIP+4, a Canadian or British code), spaces around it ignored. </summary>
    public const string PostalPattern = @"\s*[A-Za-z0-9][A-Za-z0-9 \-]{1,8}[A-Za-z0-9]\s*";

    /// <summary> A US social security number: nine digits, or 3-2-4 with hyphens. </summary>
    public const string SsnPattern = @"[0-9]{9}|[0-9]{3}-[0-9]{2}-[0-9]{4}";

    /// <summary> The regular expression a shape is checked with (the same in every language that has regular expressions), or null for a shape that has none (email and URL use each language's own check). </summary>
    public static string? PatternOf(TextShape shape) => shape switch
    {
        TextShape.Phone => PhonePattern,
        TextShape.Postal => PostalPattern,
        TextShape.Ssn => SsnPattern,
        _ => null
    };

    /// <summary> The regular expression of the rule's shape (the rule's own for <see cref="TextShape.Custom"/>), or null. </summary>
    public static string? PatternOf(ColumnRule rule) => rule.Shape == TextShape.Custom ? rule.Pattern : PatternOf(rule.Shape);

    /// <summary> What a value of the rule's shape is called in a message. </summary>
    public static string NounOf(ColumnRule rule) => rule.Shape == TextShape.Custom ? rule.Noun ?? "a value of the column's type" : NounOf(rule.Shape);

    /// <summary> A C# condition that is true when <paramref name="value"/> (a non-empty string) has the shape, for code that checks a form field itself (the WinUI3 view models). </summary>
    public static string CSharpTest(ColumnRule rule, string value) => rule.Shape switch
    {
        TextShape.Email => $"new System.ComponentModel.DataAnnotations.EmailAddressAttribute().IsValid({value})",
        TextShape.Url => $"Uri.TryCreate({value}, UriKind.Absolute, out var {value}Uri) && ({value}Uri.Scheme == Uri.UriSchemeHttp || {value}Uri.Scheme == Uri.UriSchemeHttps)",
        _ => $"System.Text.RegularExpressions.Regex.IsMatch({value}, @\"^(?:{PatternOf(rule)!.Replace("\"", "\"\"")})$\")"
    };

    /// <summary> What a value of the shape is called in a message: "an email address", "a postal code". </summary>
    public static string NounOf(TextShape shape) => shape switch
    {
        TextShape.Email => "an email address",
        TextShape.Phone => "a phone number",
        TextShape.Postal => "a postal code",
        TextShape.Ssn => "a social security number",
        _ => "a web address"
    };

    /// <summary> The HTML attributes a phone number box adds to its type (<c>pattern="..." title="..."</c>, without a leading space), or empty for any other column. </summary>
    public static string InputPattern(this ColumnModel column, ProjectSettings project, string table) =>
        column.RuleOf(project, table)?.Shape is { } shape && PatternOf(shape) is { } pattern
            ? $"pattern=\"{pattern}\" title=\"{(shape == TextShape.Phone ? "A phone number: digits, spaces and + ( ) - ." : shape == TextShape.Postal ? "A postal code: 3 to 10 letters, digits, spaces or hyphens" : "A social security number: 123-45-6789 or 123456789")}\""
            : "";

    /// <summary> The shape a text column's value must have: an email address, phone number, web address, postal code or social security number. The project's <c>EmailColumns</c> and <c>UrlColumns</c> say so
    /// for a column whatever its name; otherwise the name does (Email, or a name that is Mail or ends in Mail with a capital, as OwnerMail; Phone, Fax; Url, Website, Link, Homepage, WebPage, WebAddress, a name that is Web or ends in Web with a capital; Zip, PostalCode, PostCode;
    /// Ssn, SocialSecurity). A column with a list of values, or one the project lists in <c>PlainTextColumns</c>, has none. </summary>
    public static TextShape ShapeOf(this ColumnModel column, ProjectSettings project, string? table = null)
    {
        if (!column.IsStringColumn || column.HasChoices || project.IsPlainText(table ?? "", column.Name))
            return TextShape.None;
        if (project.IsEmail(table ?? "", column.Name))
            return TextShape.Email;
        if (project.IsUrl(table ?? "", column.Name))
            return TextShape.Url;
        if (Names(column, "Email") || column.Name.EqualsIgnoreCase("Mail") || column.Name.EndsWith("Mail", StringComparison.Ordinal) || column.Name.EqualsIgnoreCase("MailAddress"))
            return TextShape.Email;
        if (Names(column, "Phone", "Fax"))
            return TextShape.Phone;
        if (Names(column, "Url", "Website", "Link", "Homepage", "WebPage", "WebAddress") || column.Name.EqualsIgnoreCase("Web") || column.Name.EndsWith("Web", StringComparison.Ordinal))
            return TextShape.Url;
        if (Names(column, "Zip", "PostalCode", "PostCode"))
            return TextShape.Postal;
        return Names(column, "Ssn", "SocialSecurity") ? TextShape.Ssn : TextShape.None;
    }

    private static bool Names(ColumnModel column, params string[] words) => words.Any(word => column.Name.ContainsIgnoreCase(word));
}
