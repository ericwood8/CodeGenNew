using CodeGenNew.Core;

namespace CodeGenNew.Tests;

/// <summary> The React edit form runs its check and its save as a form action (useActionState), and its Submit button is a SubmitButton that reads useFormStatus. </summary>
[TestClass]
public class ReactFormActionTests
{
    private static TableModel ItemTable() => Sample.Table("Item", [
        Sample.Column("ItemId", System.Data.SqlDbType.Int, primaryKey: true, identity: true, ordinal: 1),
        Sample.Column("ItemNumber", System.Data.SqlDbType.VarChar, characters: 30, ordinal: 2)]);

    private static TableModel WithChild(TableModel t) => new()
    {
        SchemaName = t.SchemaName, TableName = t.TableName, QuotedName = t.QuotedName, Dialect = t.Dialect,
        Columns = t.Columns, PrimaryKeyColumns = t.PrimaryKeyColumns, ForeignKeys = t.ForeignKeys,
        ChildForeignKeys = [Sample.ChildForeignKey("ItemNote", "ItemId", "ItemId", childOwnPrimaryKey: ["ItemNoteId"])],
        DisplayColumns = t.DisplayColumns, HasReferencedDisplayColumns = t.HasReferencedDisplayColumns, LookupShape = t.LookupShape
    };

    private static async Task<string> Generate(string template)
    {
        var table = template.Contains("DetailMaster") ? WithChild(ItemTable()) : ItemTable();
        var result = await Repo.Cache.RunAsync(Repo.Template(template), table, ProjectSettings.FromValues([new("ProjectName", "Acme")]));
        Assert.IsTrue(result.Success, string.Join(" | ", result.Errors));
        return result.GeneratedText!.ReplaceLineEndings("\n");
    }

    [TestMethod]
    [DataRow("TSX_Page_v1.tt")]
    [DataRow("TSX_DetailMasterPage_v1.tt")]
    public async Task The_edit_form_submits_through_a_form_action_and_a_SubmitButton(string template)
    {
        string text = await Generate(template);

        Expect.Contains(text, "import { startTransition, useActionState, useEffect, useOptimistic, useRef, useState } from 'react';");
        Expect.Contains(text, "/SubmitButton';");
        Expect.Contains(text, "const [, submitForm] = useActionState(async (_previous: null, _data: FormData): Promise<null> => {");
        Expect.Contains(text, "action={submitForm}>");
        Expect.Contains(text, "<SubmitButton>Submit</SubmitButton>");
        Expect.Contains(text, "if (selectedRow) await submit(selectedRow);");
        Expect.DoesNotContain(text, "onSubmit=");
        Expect.DoesNotContain(text, "handleSubmit");
        Expect.DoesNotContain(text, "<button type=\"submit\">");
    }

    [TestMethod]
    [DataRow("TSX_Page_v1.tt")]
    [DataRow("TSX_DetailMasterPage_v1.tt")]
    public async Task A_deleted_row_leaves_the_grid_at_once_and_returns_if_the_delete_is_refused(string template)
    {
        string text = await Generate(template);

        Expect.Contains(text, "const [shownRows, hideRow] = useOptimistic(items, (current, id: number) => current.filter((row) => row.itemId !== id));");
        Expect.Contains(text, "{shownRows.map((item) => (");
        Expect.Contains(text, "const remove = (id: number): void => {");
        Expect.Contains(text, "    startTransition(async () => {\n      hideRow(id);\n      await deleteRow(id);\n    });");
        Expect.Contains(text, "setItems((rows) => rows.filter((row) => row.itemId !== id));");
        Expect.DoesNotContain(text, "{items.map(");
    }

    [TestMethod]
    public async Task SubmitButton_is_off_while_the_form_action_runs()
    {
        string text = await Generate("TSX_EssentialSupport_v1.tt");

        Expect.Contains(text, "SubmitButton.tsx@@@");
        Expect.Contains(text, "import { useFormStatus } from 'react-dom';");
        Expect.Contains(text, "const { pending } = useFormStatus();");
        Expect.Contains(text, "<button type=\"submit\" disabled={pending}>{children}</button>");
    }
}
