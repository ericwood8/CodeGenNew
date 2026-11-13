namespace CodeGenNew.Core;

/// <summary> One column as a form shows it: the column plus the facts only its form knows (is it a drop-down of a parent, is the key typed by a person, did a guard make it a stamp). It answers for
/// itself which editor it gets (<c>IsLookupField</c>, <c>IsMoneyField</c> ...) and how a view model holds its value (<c>IsMoneyValue</c>, <c>IsWholeNumberValue</c> ...), so a caller never asks the form and
/// never switches on a kind. Made by <see cref="FormModel.Field(ColumnModel)"/>. </summary>
public sealed class FormColumn
{
    private readonly FormModel _form;
    private readonly ProjectSettings _project;
    private FieldKind? _editor;
    private ValueKind? _held;
    private bool? _isMoneyInput;

    internal FormColumn(FormModel form, ProjectSettings project, ColumnModel column)
    {
        _form = form;
        _project = project;
        Column = column;
    }

    public ColumnModel Column { get; }

    // The editor a column gets on the form. The order is the rule every stack follows: the first that fits wins.
    private FieldKind Editor => _editor ??=
        Column.IsSystemStampColumn || _form.IsGuardStamp(Column) || ContentScreens.IsStateColumn(_form.Table, _project, Column) || DefaultRowScreens.IsFlagColumn(_form.Table, _project, Column) || AssetScreens.IsUploadColumn(_form.Table, _project, Column) ? FieldKind.SystemStamp
        : _form.TypedKey && Column.IsPrimaryKey ? (_form.TextKey ? FieldKind.TypedKey : FieldKind.TypedNumberKey)
        : _form.LookupOf(Column) is not null ? FieldKind.Lookup
        : Column.HasChoices ? FieldKind.Choices
        : Column.IsBooleanColumn ? FieldKind.Boolean
        : TemplateHelpers.IsDate(Column) ? FieldKind.Date
        : IsMoneyInput ? FieldKind.Money
        : TemplateHelpers.IsNumber(Column) ? FieldKind.Number
        : _form.IsMultilineBox(Column) ? FieldKind.Multiline
        : FieldKind.Text;

    // How an editor that keeps numbers as numbers and everything else as text (the WinUI3 view model) holds the value. The first that fits wins. A system stamp is held as what it is (a date stays a
    // date); only the editor decides that it is shown as text.
    private ValueKind Held => _held ??=
        _form.LookupOf(Column) is not null ? ValueKind.Lookup
        : IsMoneyInput ? ValueKind.Money
        : Column.IsDecimalBox ? ValueKind.Decimal
        : Column.IsWholeNumberBox ? ValueKind.WholeNumber
        : Column.IsBooleanColumn ? ValueKind.Boolean
        : TemplateHelpers.IsDate(Column) ? ValueKind.Date
        : Column.IsGuidColumn ? ValueKind.Guid
        : TemplateHelpers.IsNumber(Column) ? ValueKind.Number
        : ValueKind.Text;

    /// <summary> Written by the system (who made the row and when): shown as text once the row exists. </summary>
    public bool IsSystemStampField => Editor == FieldKind.SystemStamp;
    /// <summary> The text key a person types on a new row; read-only afterwards. </summary>
    public bool IsTypedKeyField => Editor == FieldKind.TypedKey;
    /// <summary> The whole-number key a person types on a new row (the database does not hand it out); read-only afterwards. </summary>
    public bool IsTypedNumberKeyField => Editor == FieldKind.TypedNumberKey;
    /// <summary> A foreign key: a drop-down of the parent's rows. </summary>
    public bool IsLookupField => Editor == FieldKind.Lookup;
    /// <summary> A column whose values the database lists: a drop-down of them. </summary>
    public bool IsChoicesField => Editor == FieldKind.Choices;
    public bool IsBooleanField => Editor == FieldKind.Boolean;
    public bool IsDateField => Editor == FieldKind.Date;
    /// <summary> An amount of money: a money box. </summary>
    public bool IsMoneyField => Editor == FieldKind.Money;
    /// <summary> A whole or decimal number that is not money. </summary>
    public bool IsNumberField => Editor == FieldKind.Number;
    /// <summary> A number box: a number that is not money, or the whole-number key a person types. </summary>
    public bool IsNumberBoxField => IsNumberField || IsTypedNumberKeyField;
    /// <summary> Text that needs several lines. </summary>
    public bool IsMultilineField => Editor == FieldKind.Multiline;
    public bool IsTextField => Editor == FieldKind.Text;

    /// <summary> A foreign key to a parent that is looked up: the parent's key, or null for none. </summary>
    public bool IsLookupValue => Held == ValueKind.Lookup;
    /// <summary> An amount of money, held as a double (NaN is blank) and saved as a rounded decimal. </summary>
    public bool IsMoneyValue => Held == ValueKind.Money;
    /// <summary> A decimal, float or real that is not money, held as a double (NaN is blank). </summary>
    public bool IsDecimalValue => Held == ValueKind.Decimal;
    /// <summary> A whole number, held as a double (NaN is blank) and saved after checking it is whole and in range. </summary>
    public bool IsWholeNumberValue => Held == ValueKind.WholeNumber;
    public bool IsBooleanValue => Held == ValueKind.Boolean;
    public bool IsDateValue => Held == ValueKind.Date;
    /// <summary> A uniqueidentifier typed as text. </summary>
    public bool IsGuidValue => Held == ValueKind.Guid;
    /// <summary> Any other number, typed as text. </summary>
    public bool IsNumberValue => Held == ValueKind.Number;
    public bool IsTextValue => Held == ValueKind.Text;

    // The timestamp of a log shows when a row was written, down to the minute.
    private bool IsLogTimestamp => _form.Log is { } log && Column.Name == log.Timestamp.Name && !IsLookupCell;

    /// <summary> A grid shows a foreign key as the parent's name. </summary>
    public bool IsLookupCell => _form.LookupOf(Column) is not null;
    /// <summary> A grid shows Yes or No. </summary>
    public bool IsBooleanCell => !IsLookupCell && !IsLogTimestamp && Column.IsBooleanCell;
    /// <summary> A grid shows a date. </summary>
    public bool IsDateCell => !IsLookupCell && !IsLogTimestamp && Column.IsDateCell;
    /// <summary> A grid shows a day and the time of day. </summary>
    public bool IsDateTimeCell => !IsLookupCell && (IsLogTimestamp || Column.IsDateTimeCell);
    /// <summary> A grid shows an amount of money, with the currency. </summary>
    public bool IsMoneyCell => !IsLookupCell && !IsLogTimestamp && Column.IsMoneyCell;
    /// <summary> A grid shows a decimal that is not money, with two decimals. </summary>
    public bool IsDecimalCell => !IsLookupCell && !IsLogTimestamp && Column.IsDecimalCell;
    /// <summary> A grid shows the value as it is. </summary>
    public bool IsPlainCell => !IsLookupCell && !IsLogTimestamp && Column.IsPlainCell;

    /// <summary> A money column that is edited in a money box: a foreign key, a list of choices, a stamp and the typed key are not. </summary>
    public bool IsMoneyInput => _isMoneyInput ??=
        Column.IsMoneyAmount && TemplateHelpers.IsNumber(Column) && !Column.IsSystemStampColumn && _form.LookupOf(Column) is null && !Column.HasChoices && !(_form.TypedKey && Column.IsPrimaryKey);

    /// <summary> The person must give this column a value: a field of the form that is NOT NULL, other than the system's own stamps, the slug of a content table (the server makes it) and a yes/no box (which always holds a value). Every stack's
    /// <c>required</c> check and red mark read this, so they cannot drift apart. </summary>
    public bool IsRequired => !Column.IsNullable && !IsSystemStampField && !IsBooleanField && !ContentScreens.IsSlug(_form.Table, _project, Column);

    /// <summary> The hint inside an empty text box: what to enter, or for the slug of a content table that the server makes it from the title. </summary>
    public string Placeholder => ContentScreens.IsSlug(_form.Table, _project, Column) ? ContentScreens.SlugHint : Labels.Placeholder(Column);

    /// <summary> The caption carries the red asterisk: <see cref="IsRequired"/>, unless the project turned <c>RequiredMarks</c> off. </summary>
    public bool IsMarked => _project.RequiredMarks && IsRequired;

    /// <summary> The editor a form gives a column . </summary>
    private enum FieldKind
    {
        /// <summary> Written by the system (who made the row and when): shown as text once the row exists. </summary>
        SystemStamp,
        /// <summary> The text key a person types on a new row; read-only afterwards. </summary>
        TypedKey,
        /// <summary> The whole-number key a person types on a new row (the database does not hand it out); read-only afterwards. </summary>
        TypedNumberKey,
        /// <summary> A foreign key: a drop-down of the parent's rows. </summary>
        Lookup,
        /// <summary> A column whose values the database lists: a drop-down of them. </summary>
        Choices,
        Boolean,
        Date,
        /// <summary> An amount of money: a money box. </summary>
        Money,
        /// <summary> A whole or decimal number that is not money. </summary>
        Number,
        /// <summary> Text that needs several lines. </summary>
        Multiline,
        Text
    }

    /// <summary> How a view model holds a column's value . </summary>
    private enum ValueKind
    {
        /// <summary> A foreign key to a parent that is looked up: the parent's key, or null for none. </summary>
        Lookup,
        /// <summary> An amount of money, held as a double (NaN is blank) and saved as a rounded decimal. </summary>
        Money,
        /// <summary> A decimal, float or real that is not money, held as a double (NaN is blank). </summary>
        Decimal,
        /// <summary> A whole number, held as a double (NaN is blank) and saved after checking it is whole and in range. </summary>
        WholeNumber,
        Boolean,
        Date,
        /// <summary> A uniqueidentifier typed as text. </summary>
        Guid,
        /// <summary> Any other number, typed as text. </summary>
        Number,
        Text
    }
}
