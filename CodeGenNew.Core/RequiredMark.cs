namespace CodeGenNew.Core;

/// <summary> The red asterisk every generated form puts after the caption of a field the person must fill in (the <c>RequiredMarks</c> setting turns it off). The character is written into the generated
/// markup of each stack; screen readers skip it because the field itself says it is required (the <c>required</c> attribute, or <c>IsRequiredForForm</c> in WinUI3). </summary>
public static class RequiredMark
{
    public const string Glyph = "*";

    /// <summary> Red with a contrast of about 7:1 on white, well above the 4.5:1 that text needs. </summary>
    public const string Color = "#b00020";

    /// <summary> The mark as an HTML or Razor span, for a label's text: <c> *</c> after a space. </summary>
    public static string Html(bool marked) =>
        marked ? $" <span aria-hidden=\"true\" style=\"color: {Color}\">{Glyph}</span>" : "";

    /// <summary> The header template a WinUI3 dialog declares once in its resources (<c>HeaderTemplate="{StaticResource RequiredHeader}"</c> on a marked control): the caption with a red asterisk after it.
    /// Plain red, the same on every theme. </summary>
    public const string XamlHeaderTemplate =
        "        <DataTemplate x:Key=\"RequiredHeader\">\n" +
        "            <TextBlock><Run Text=\"{Binding}\" /><Run Text=\" " + Glyph + "\" Foreground=\"Red\" /></TextBlock>\n" +
        "        </DataTemplate>\n";

    /// <summary> The attributes a WinUI3 control gets for a required field: UI Automation's required flag, and the red-asterisk header template when the caption is marked. </summary>
    public static string XamlAttributes(bool required, bool marked) =>
        (required ? " AutomationProperties.IsRequiredForForm=\"True\"" : "") + (marked ? " HeaderTemplate=\"{StaticResource RequiredHeader}\"" : "");

    /// <summary> The mark as a JSX span (React style objects). </summary>
    public static string Jsx(bool marked) =>
        marked ? $" <span aria-hidden=\"true\" style={{{{ color: '{Color}' }}}}>{Glyph}</span>" : "";
}
