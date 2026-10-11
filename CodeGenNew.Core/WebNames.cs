namespace CodeGenNew.Core;

/// <summary> The names of the Angular files and classes a table gets. The template that writes a name and every template that refers to it ask here, so a definition and a reference cannot drift apart. </summary>
public static class AngularNames
{
    /// <summary> The service class: <c>SalesInvoiceService</c>. </summary>
    public static string ServiceClass(string table) => ScreenNames.BaseName(table) + "Service";

    /// <summary> The service's file without the extension: <c>salesinvoice.service</c>. </summary>
    public static string ServiceFile(string table) => ScreenNames.Stem(table) + ".service";

    /// <summary> The service as a field of a component: <c>salesInvoiceService</c>. </summary>
    public static string ServiceField(string table) => TemplateHelpers.LowerFirst(ScreenNames.BaseName(table)) + "Service";

    /// <summary> The component class of the table's screen: <c>SalesInvoiceComponent</c>, or <c>SalesInvoiceDetailMasterComponent</c> for a table with child grids. </summary>
    public static string ComponentClass(TableModel table, ProjectSettings project) =>
        ScreenNames.BaseName(table.TableName) + (DatabaseModel.IsDetailMaster(table, project) ? "DetailMasterComponent" : "Component");

    /// <summary> The folder and file stem of the table's component: <c>salesinvoice</c>, or <c>salesinvoice-detail-master</c>. </summary>
    public static string ComponentStem(TableModel table, ProjectSettings project) =>
        ScreenNames.Stem(table.TableName) + (DatabaseModel.IsDetailMaster(table, project) ? "-detail-master" : "");
}

/// <summary> The names of the React modules and components a table gets (see <see cref="AngularNames"/>). </summary>
public static class ReactNames
{
    /// <summary> The api module's object: <c>salesInvoiceApi</c>. </summary>
    public static string ApiVar(string table) => TemplateHelpers.LowerFirst(ScreenNames.BaseName(table)) + "Api";

    /// <summary> The api module's file without the extension: <c>salesinvoiceApi</c>. </summary>
    public static string ApiFile(string table) => ScreenNames.Stem(table) + "Api";

    /// <summary> The state setter of a list a page holds: <c>customers</c> is set by <c>setCustomers</c>. </summary>
    public static string SetList(string list) => "set" + TemplateHelpers.UpperFirst(list);

    /// <summary> The page component of the table's screen: <c>SalesInvoicePage</c>, or <c>SalesInvoiceDetailMasterPage</c> for a table with child grids. </summary>
    public static string PageName(TableModel table, ProjectSettings project) =>
        ScreenNames.BaseName(table.TableName) + (DatabaseModel.IsDetailMaster(table, project) ? "DetailMasterPage" : "Page");
}
