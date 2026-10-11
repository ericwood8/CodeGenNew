namespace CodeGenNew.Core;

/// <summary> The names the WinUI3 screens give their drop-down options. The class that declares an option type and every property that uses it ask here, so the two cannot drift apart. </summary>
public static class WinUI3Names
{
    /// <summary> The view-model property that holds the options of a parent's drop-down: <c>RegionOptions</c>. </summary>
    public static string OptionsProperty(FormParent parent) => parent.Table + "Options";

    /// <summary> The class of one option of a drop-down whose ids are <paramref name="keyCs"/>: <c>CustomerDetailLookupOption</c> for an int, <c>CustomerDetailLookupOptionString</c> for text. The int class
    /// keeps the plain name. <paramref name="screen"/> is <c>Detail</c> or <c>DetailMaster</c>. </summary>
    public static string LookupOptionClass(string table, string screen, string keyCs) =>
        table + screen + "LookupOption" + (keyCs == "int" ? "" : TemplateHelpers.UpperFirst(keyCs));

    /// <summary> The dictionary of a parent's names a list page looks its foreign keys up in: <c>regionNames</c>. </summary>
    public static string NamesDictionary(FormParent parent) => TemplateHelpers.LowerFirst(parent.Table) + "Names";
}
