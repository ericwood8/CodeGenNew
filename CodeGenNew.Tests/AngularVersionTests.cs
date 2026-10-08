using System.Data;
using CodeGenNew.Core;
using CodeGenNew.TemplateEngine;

namespace CodeGenNew.Tests;

/// <summary> The project setting AngularVersion: what the Angular templates write for the version the project runs (standalone flag, the eager strategy's name, built-in control flow,
/// the animation providers in a spec). Without the setting the output is what every version from 18 accepts. </summary>
[TestClass]
public class AngularVersionTests
{
    [TestMethod]
    public void A_for_directive_on_a_multi_line_element_becomes_an_at_for_block_around_it()
    {
        string html = "<table>\n    <tbody>\n        <tr *ngFor=\"let row of rows\">\n            <td>{{row.name}}</td>\n        </tr>\n    </tbody>\n</table>\n";

        string converted = AngularControlFlow.Convert(html);

        Assert.AreEqual("<table>\n    <tbody>\n        @for (row of rows; track $index) {\n          <tr>\n              <td>{{row.name}}</td>\n          </tr>\n        }\n    </tbody>\n</table>\n", converted);
    }

    [TestMethod]
    public void An_if_with_an_else_template_becomes_if_and_else_and_the_template_goes()
    {
        string html = "<div>\n    <div class=\"g\" *ngIf=\"selectedRow.id; else saveFirst\">\n        <p>rows</p>\n    </div>\n    <ng-template #saveFirst><p><em>Save first.</em></p></ng-template>\n</div>\n";

        string converted = AngularControlFlow.Convert(html);

        Assert.Contains("@if (selectedRow.id) {", converted, converted);
        Assert.Contains("} @else {", converted, converted);
        Assert.Contains("<p><em>Save first.</em></p>", converted, converted);
        Assert.DoesNotContain("ng-template", converted, converted);
        Assert.DoesNotContain("*ng", converted, converted);
    }

    [TestMethod]
    public void Nested_directives_a_directive_inside_an_attribute_quote_and_a_single_line_element_are_all_converted()
    {
        string html = "<dialog *ngIf=\"selectedRow\" (cancel)=\"a > b\">\n  <select>\n    <option *ngFor=\"let p of parents\" [ngValue]=\"p.id\">{{p.name}}</option>\n  </select>\n</dialog>\n";

        string converted = AngularControlFlow.Convert(html);

        Assert.DoesNotContain("*ng", converted, converted);
        Assert.Contains("@if (selectedRow) {", converted, converted);
        Assert.Contains("@for (p of parents; track $index) {", converted, converted);
        Assert.Contains("<dialog (cancel)=\"a > b\">", converted, converted);
        Assert.AreEqual(converted.Split('{').Length - 1 - converted.Split("{{").Length + 1, converted.Split('}').Length - 1 - converted.Split("}}").Length + 1, "every block is closed");
    }

    private static ProjectSettings Project(string? version) =>
        ProjectSettings.FromValues([new("ProjectName", "Acme"), .. version is null ? Array.Empty<KeyValuePair<string, string>>() : [new KeyValuePair<string, string>("AngularVersion", version)]]);

    private static TableModel Invoice() => Sample.Table("Invoice",
    [
        Sample.Column("InvoiceId", SqlDbType.Int, primaryKey: true, identity: true, ordinal: 1),
        Sample.Column("Name", SqlDbType.NVarChar, characters: 50, ordinal: 2),
        Sample.Column("CustomerId", SqlDbType.Int, ordinal: 3)
    ],
    [Sample.ForeignKey("CustomerId", "Customer", "CustomerId", "Name")],
    childForeignKeys: [Sample.ChildForeignKey("InvoiceLine", "InvoiceId", "InvoiceId", childOwnPrimaryKey: ["InvoiceLineId"])]);

    private static async Task<Dictionary<string, string>> Files(string template, string? version)
    {
        var result = await Repo.Cache.RunAsync(Repo.Template(template), Invoice(), Project(version));
        Assert.IsTrue(result.Success, $"{template}: {string.Join(" | ", result.Errors)}");
        return GeneratedFiles.Split(result.GeneratedText!).ToDictionary(f => Path.GetExtension(f.RelativePath) == ".ts" && f.RelativePath.EndsWith(".spec.ts") ? "spec" : Path.GetExtension(f.RelativePath).TrimStart('.'), f => f.Content.Replace("\r\n", "\n"));
    }

    [TestMethod]
    [DataRow("TS_Component_v1.tt")]
    [DataRow("TS_DetailMasterComponent_v1.tt")]
    public async Task Without_a_version_the_output_keeps_standalone_the_old_directives_and_the_animation_providers(string template)
    {
        var files = await Files(template, null);

        Expect.Contains(files["ts"], "  standalone: true,");
        Expect.Contains(files["ts"], "changeDetection: ChangeDetectionStrategy.Default,");
        Expect.Contains(files["html"], "*ngFor=");
        Expect.Contains(files["html"], "*ngIf=");
        Expect.Contains(files["spec"], "provideNoopAnimations()");
    }

    [TestMethod]
    [DataRow("TS_Component_v1.tt")]
    [DataRow("TS_DetailMasterComponent_v1.tt")]
    public async Task Angular_22_drops_standalone_names_the_strategy_Eager_uses_control_flow_and_no_animation_providers(string template)
    {
        var files = await Files(template, "22");

        Expect.DoesNotContain(files["ts"], "standalone: true");
        Expect.Contains(files["ts"], "changeDetection: ChangeDetectionStrategy.Eager,");
        Expect.DoesNotContain(files["html"], "*ng");
        Expect.Contains(files["html"], "@for (");
        Expect.Contains(files["html"], "@if (selectedRow) {");
        Expect.DoesNotContain(files["spec"], "provideNoopAnimations");
        Expect.Contains(files["spec"], "provideHttpClientTesting(), provideRouter([])");
    }

    [TestMethod]
    public async Task Angular_19_drops_standalone_but_keeps_Default_and_the_animation_providers_and_18_keeps_standalone()
    {
        var nineteen = await Files("TS_Component_v1.tt", "19");
        Expect.DoesNotContain(nineteen["ts"], "standalone: true");
        Expect.Contains(nineteen["ts"], "ChangeDetectionStrategy.Default,");
        Expect.Contains(nineteen["spec"], "provideNoopAnimations()");
        Expect.Contains(nineteen["html"], "@for (");

        var eighteen = await Files("TS_Component_v1.tt", "18");
        Expect.Contains(eighteen["ts"], "  standalone: true,");
        Expect.Contains(eighteen["html"], "@for (");

        var seventeen = await Files("TS_Component_v1.tt", "17");
        Expect.Contains(seventeen["html"], "*ngFor=");   // control flow is stable from 18
    }

    [TestMethod]
    public async Task The_detail_master_html_turns_the_save_first_template_into_an_else_branch()
    {
        var files = await Files("TS_DetailMasterComponent_v1.tt", "22");

        Expect.Contains(files["html"], "} @else {");
        Expect.Contains(files["html"], "Save this Invoice first to see its Invoice Line rows.");
        Expect.DoesNotContain(files["html"], "ng-template");
    }
}
