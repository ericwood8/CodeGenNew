using System.Text;

namespace CodeGenNew.Core;

/// <summary> The generated API tests of the row shapes (<c>TreeTables</c>, <c>SoftDeleteTables</c>, <c>EffectiveDatedTables</c>), appended to the table's own test class by <c>API_Test</c>. Each test makes its own rows with
/// the sample body of the other tests and removes them again (a soft-delete table keeps the flagged rows). </summary>
public static class RowShapeTests
{
    public static string Methods(ApiTestPlan plan, TableModel table, ProjectSettings project)
    {
        var tree = TableShapes.For(table, project).Tree;
        var soft = TableShapes.For(table, project).SoftDelete;
        var effective = TableShapes.For(table, project).Effective;
        var content = TableShapes.For(table, project).Content;
        if (tree is null && soft is null && effective is null && content is null)
            return "";
        var o = new StringBuilder();
        void L(string text = "") => o.Append(text).Append('\n');
        string id = plan.KeyJson;

        L();
        L("    private static async Task<JsonObject> MakeAsync(Action<JsonObject>? change = null)");
        L("    {");
        L("        var body = await NewBodyAsync();");
        L("        change?.Invoke(body);");
        L("        using var response = await ApiFixture.Client.PostAsJsonAsync(Route, body);");
        L("        Assert.AreEqual(HttpStatusCode.Created, response.StatusCode, await response.Content.ReadAsStringAsync());");
        L("        return (await response.Content.ReadFromJsonAsync<JsonObject>())!;");
        L("    }");
        L();
        L("    private static async Task<HttpResponseMessage> PutAsync(JsonObject row, Action<JsonObject> change)");
        L("    {");
        L("        var body = (JsonObject)row.DeepClone();");
        L("        change(body);");
        L($"        return await ApiFixture.Client.PutAsJsonAsync($\"{{Route}}/{{row[\"{id}\"]!.GetValue<long>()}}\", body);");
        L("    }");
        L();
        L("    private static async Task RemoveAsync(params JsonObject[] rows)");
        L("    {");
        L("        foreach (var row in Enumerable.Reverse(rows))");
        L($"            using (await ApiFixture.Client.DeleteAsync($\"{{Route}}/{{row[\"{id}\"]!.GetValue<long>()}}\")) {{ }}");
        L("    }");

        if (tree is not null)
        {
            string parent = JsonNames.Camel(tree.Parent.Name);
            L();
            L("    [TestMethod]");
            L("    public async Task A_row_cannot_be_its_own_parent_or_move_under_its_own_descendant()");
            L("    {");
            L("        ApiFixture.RequireDatabase();");
            L("        var top = await MakeAsync();");
            L($"        var child = await MakeAsync(b => b[\"{parent}\"] = top[\"{id}\"]!.GetValue<long>());");
            L($"        var grandchild = await MakeAsync(b => b[\"{parent}\"] = child[\"{id}\"]!.GetValue<long>());");
            L($"        using var itself = await PutAsync(top, b => b[\"{parent}\"] = top[\"{id}\"]!.GetValue<long>());");
            L("        Assert.AreEqual(HttpStatusCode.BadRequest, itself.StatusCode, await itself.Content.ReadAsStringAsync());");
            L($"        Assert.AreEqual(\"{RowShapeWords.TreeCycleType}\", (await itself.Content.ReadFromJsonAsync<JsonObject>())![\"type\"]!.GetValue<string>());");
            L($"        using var underGrandchild = await PutAsync(top, b => b[\"{parent}\"] = grandchild[\"{id}\"]!.GetValue<long>());");
            L("        Assert.AreEqual(HttpStatusCode.BadRequest, underGrandchild.StatusCode, await underGrandchild.Content.ReadAsStringAsync());");
            L("        await RemoveAsync(top, child, grandchild);");
            L("    }");
            L();
            L("    [TestMethod]");
            L("    public async Task A_row_with_children_is_not_deleted_until_they_are_promoted()");
            L("    {");
            L("        ApiFixture.RequireDatabase();");
            L("        var top = await MakeAsync();");
            L($"        var child = await MakeAsync(b => b[\"{parent}\"] = top[\"{id}\"]!.GetValue<long>());");
            L($"        using var refused = await ApiFixture.Client.DeleteAsync($\"{{Route}}/{{top[\"{id}\"]!.GetValue<long>()}}\");");
            L("        Assert.AreEqual(HttpStatusCode.Conflict, refused.StatusCode, await refused.Content.ReadAsStringAsync());");
            L($"        Assert.AreEqual(\"{RowShapeWords.TreeHasChildrenType}\", (await refused.Content.ReadFromJsonAsync<JsonObject>())![\"type\"]!.GetValue<string>());");
            L($"        using var promoted = await ApiFixture.Client.PostAsync($\"{{Route}}/{{top[\"{id}\"]!.GetValue<long>()}}/promote-children\", null);");
            L("        Assert.AreEqual(HttpStatusCode.OK, promoted.StatusCode, await promoted.Content.ReadAsStringAsync());");
            L($"        var moved = (await ApiFixture.Client.GetFromJsonAsync<JsonObject>($\"{{Route}}/{{child[\"{id}\"]!.GetValue<long>()}}\"))!;");
            L($"        Assert.IsNull(moved[\"{parent}\"], \"the top row had no parent, so the child is now a root\");");
            L($"        using var deleted = await ApiFixture.Client.DeleteAsync($\"{{Route}}/{{top[\"{id}\"]!.GetValue<long>()}}\");");
            L("        Assert.AreEqual(HttpStatusCode.OK, deleted.StatusCode, await deleted.Content.ReadAsStringAsync());");
            L("        await RemoveAsync(child);");
            L("    }");
            L();
            L("    [TestMethod]");
            L("    public async Task The_tree_lists_a_parent_before_its_children_with_their_level()");
            L("    {");
            L("        ApiFixture.RequireDatabase();");
            L("        var top = await MakeAsync();");
            L($"        var child = await MakeAsync(b => b[\"{parent}\"] = top[\"{id}\"]!.GetValue<long>());");
            L("        var nodes = (await ApiFixture.Client.GetFromJsonAsync<JsonArray>($\"{Route}/tree\"))!;");
            L($"        int Index(JsonObject row) => nodes.ToList().FindIndex(n => n![\"id\"]!.GetValue<long>() == row[\"{id}\"]!.GetValue<long>());");
            L("        Assert.IsGreaterThanOrEqualTo(0, Index(top));");
            L("        Assert.IsGreaterThan(Index(top), Index(child), \"a child follows its parent\");");
            L("        Assert.AreEqual(nodes[Index(top)]![\"depth\"]!.GetValue<int>() + 1, nodes[Index(child)]![\"depth\"]!.GetValue<int>());");
            L("        Assert.IsTrue(nodes[Index(top)]![\"hasChildren\"]!.GetValue<bool>());");
            L("        await RemoveAsync(top, child);");
            L("    }");
            if (tree.MaxDepth > 0)
            {
                L();
                L("    [TestMethod]");
                L("    public async Task A_level_past_the_depth_limit_is_refused()");
                L("    {");
                L("        ApiFixture.RequireDatabase();");
                L("        var made = new List<JsonObject>();");
                L($"        for (int level = 1; level <= {tree.MaxDepth}; level++)");
                L("        {");
                L($"            long? above = made.Count == 0 ? null : made[^1][\"{id}\"]!.GetValue<long>();");
                L($"            made.Add(await MakeAsync(b => b[\"{parent}\"] = above));");
                L("        }");
                L("        var body = await NewBodyAsync();");
                L($"        body[\"{parent}\"] = made[^1][\"{id}\"]!.GetValue<long>();");
                L("        using var response = await ApiFixture.Client.PostAsJsonAsync(Route, body);");
                L("        Assert.AreEqual(HttpStatusCode.BadRequest, response.StatusCode, await response.Content.ReadAsStringAsync());");
                L($"        Assert.AreEqual(\"{RowShapeWords.TreeTooDeepType}\", (await response.Content.ReadFromJsonAsync<JsonObject>())![\"type\"]!.GetValue<string>());");
                L("        await RemoveAsync(made.ToArray());");
                L("    }");
            }
        }

        if (tree is not null && soft is not null)
        {
            string treeParent = JsonNames.Camel(tree.Parent.Name);
            L();
            L("    [TestMethod]");
            L("    public async Task A_row_under_a_deleted_parent_is_not_listed_in_the_tree_as_a_root()");
            L("    {");
            L("        ApiFixture.RequireDatabase();");
            L("        var top = await MakeAsync();");
            L($"        var child = await MakeAsync(b => b[\"{treeParent}\"] = top[\"{id}\"]!.GetValue<long>());");
            L($"        long topKey = top[\"{id}\"]!.GetValue<long>(), childKey = child[\"{id}\"]!.GetValue<long>();");
            L("        using (await ApiFixture.Client.DeleteAsync($\"{Route}/{childKey}\")) { }");
            L("        using (await ApiFixture.Client.DeleteAsync($\"{Route}/{topKey}\")) { }");
            L("        using var restored = await ApiFixture.Client.PostAsync($\"{Route}/{childKey}/restore\", null);");
            L("        Assert.AreEqual(HttpStatusCode.OK, restored.StatusCode, await restored.Content.ReadAsStringAsync());");
            L("        var nodes = (await ApiFixture.Client.GetFromJsonAsync<JsonArray>($\"{Route}/tree\"))!;");
            L("        Assert.IsFalse(nodes.Any(n => n![\"id\"]!.GetValue<long>() == childKey), \"the child of a deleted row is not shown as a root\");");
            L("    }");
        }
        if (soft is not null)
        {
            L();
            L("    [TestMethod]");
            L("    public async Task A_deleted_row_is_hidden_from_every_read_listed_as_deleted_and_comes_back_when_restored()");
            L("    {");
            L("        ApiFixture.RequireDatabase();");
            L("        var row = await MakeAsync();");
            L($"        long key = row[\"{id}\"]!.GetValue<long>();");
            L("        using var deleted = await ApiFixture.Client.DeleteAsync($\"{Route}/{key}\");");
            L("        Assert.AreEqual(HttpStatusCode.OK, deleted.StatusCode, await deleted.Content.ReadAsStringAsync());");
            L("        using var read = await ApiFixture.Client.GetAsync($\"{Route}/{key}\");");
            L("        Assert.AreEqual(HttpStatusCode.NotFound, read.StatusCode, \"a deleted row is not read\");");
            L("        using var again = await ApiFixture.Client.DeleteAsync($\"{Route}/{key}\");");
            L("        Assert.AreEqual(HttpStatusCode.NotFound, again.StatusCode, \"a deleted row cannot be deleted twice\");");
            L("        var listed = (await ApiFixture.Client.GetFromJsonAsync<JsonArray>($\"{Route}/deleted\"))!;");
            L($"        var entry = listed.FirstOrDefault(n => n![\"{id}\"]!.GetValue<long>() == key);");
            L("        Assert.IsNotNull(entry, \"the deleted row is in the list of deleted rows\");");
            if (soft.DeletedOn is not null)
                L($"        Assert.IsNotNull(entry![\"{JsonNames.Camel(soft.DeletedOn.Name)}\"], \"the delete time is stamped\");");
            L("        using var restored = await ApiFixture.Client.PostAsync($\"{Route}/{key}/restore\", null);");
            L("        Assert.AreEqual(HttpStatusCode.OK, restored.StatusCode, await restored.Content.ReadAsStringAsync());");
            L("        using var back = await ApiFixture.Client.GetAsync($\"{Route}/{key}\");");
            L("        Assert.AreEqual(HttpStatusCode.OK, back.StatusCode, \"a restored row is read again\");");
            L("        using var twice = await ApiFixture.Client.PostAsync($\"{Route}/{key}/restore\", null);");
            L("        Assert.AreEqual(HttpStatusCode.Conflict, twice.StatusCode, \"only a deleted row can be restored\");");
            L($"        Assert.AreEqual(\"{RowShapeWords.NotDeletedType}\", (await twice.Content.ReadFromJsonAsync<JsonObject>())![\"type\"]!.GetValue<string>());");
            L("        await RemoveAsync(row);");
            L("    }");
            L();
            L("    [TestMethod]");
            L("    public async Task Restoring_a_row_that_is_not_there_answers_404()");
            L("    {");
            L("        ApiFixture.RequireDatabase();");
            L($"        using var response = await ApiFixture.Client.PostAsync($\"{{Route}}/{{{plan.AbsentKey}}}/restore\", null);");
            L("        Assert.AreEqual(HttpStatusCode.NotFound, response.StatusCode);");
            L("    }");
        }

        if (content is not null)
        {
            string title = JsonNames.Camel(content.Title.Name), slug = JsonNames.Camel(content.Slug.Name), status = JsonNames.Camel(content.Status.Name);
            string? publishedOn = content.PublishedOn is null ? null : JsonNames.Camel(content.PublishedOn.Name);
            L();
            L("    private static string Token() => Guid.NewGuid().ToString(\"N\")[..8];");
            L();
            L("    [TestMethod]");
            L("    public async Task The_slug_is_made_from_the_title_and_numbered_when_it_is_taken()");
            L("    {");
            L("        ApiFixture.RequireDatabase();");
            L("        string token = Token();");
            L($"        var first = await MakeAsync(b => {{ b[\"{title}\"] = \"W\\u00f6 \" + token; b[\"{slug}\"] = \"\"; }});");
            L($"        Assert.AreEqual(\"wo-\" + token, first[\"{slug}\"]!.GetValue<string>(), \"lower case, no accent, a hyphen for the space\");");
            L($"        var second = await MakeAsync(b => {{ b[\"{title}\"] = \"W\\u00f6 \" + token; b[\"{slug}\"] = \"\"; }});");
            L($"        Assert.AreEqual(\"wo-\" + token + \"-2\", second[\"{slug}\"]!.GetValue<string>(), \"a made slug that is taken gets a number\");");
            L("        var named = await NewBodyAsync();");
            L($"        named[\"{slug}\"] = \"WO-\" + token;");
            L("        using var refused = await ApiFixture.Client.PostAsJsonAsync(Route, named);");
            L("        Assert.AreEqual(HttpStatusCode.Conflict, refused.StatusCode, await refused.Content.ReadAsStringAsync());");
            L($"        Assert.AreEqual(\"{ContentWords.SlugTakenType}\", (await refused.Content.ReadFromJsonAsync<JsonObject>())![\"type\"]!.GetValue<string>());");
            L("        await RemoveAsync(first, second);");
            L("    }");
            L();
            L("    [TestMethod]");
            L("    public async Task A_new_title_does_not_move_the_slug()");
            L("    {");
            L("        ApiFixture.RequireDatabase();");
            L("        string token = Token();");
            L($"        var row = await MakeAsync(b => {{ b[\"{title}\"] = \"W\\u00f6 \" + token; b[\"{slug}\"] = \"\"; }});");
            L($"        using var renamed = await PutAsync(row, b => b[\"{title}\"] = \"New \" + token);");
            L("        Assert.IsTrue(renamed.IsSuccessStatusCode, await renamed.Content.ReadAsStringAsync());");
            L($"        using var blank = await PutAsync(row, b => {{ b[\"{title}\"] = \"New \" + token; b[\"{slug}\"] = \"\"; }});");
            L("        Assert.IsTrue(blank.IsSuccessStatusCode, await blank.Content.ReadAsStringAsync());");
            L($"        var stored = (await ApiFixture.Client.GetFromJsonAsync<JsonObject>($\"{{Route}}/{{row[\"{id}\"]!.GetValue<long>()}}\"))!;");
            L($"        Assert.AreEqual(\"New \" + token, stored[\"{title}\"]!.GetValue<string>());");
            L($"        Assert.AreEqual(\"wo-\" + token, stored[\"{slug}\"]!.GetValue<string>(), \"the page keeps its address\");");
            L("        await RemoveAsync(row);");
            L("    }");
            L();
            L("    [TestMethod]");
            L("    public async Task The_status_is_changed_by_the_routes_and_not_by_a_save()");
            L("    {");
            L("        ApiFixture.RequireDatabase();");
            L($"        var row = await MakeAsync(b => b[\"{status}\"] = \"{ContentWords.Published}\");");
            L($"        Assert.AreEqual(\"{ContentWords.Draft}\", row[\"{status}\"]!.GetValue<string>(), \"a new row is a draft whatever it says\");");
            if (publishedOn is not null)
                L($"        Assert.IsNull(row[\"{publishedOn}\"], \"a draft has no published time\");");
            L($"        using var edited = await PutAsync(row, b => b[\"{status}\"] = \"{ContentWords.Archived}\");");
            L("        Assert.IsTrue(edited.IsSuccessStatusCode, await edited.Content.ReadAsStringAsync());");
            L($"        var stored = (await ApiFixture.Client.GetFromJsonAsync<JsonObject>($\"{{Route}}/{{row[\"{id}\"]!.GetValue<long>()}}\"))!;");
            L($"        Assert.AreEqual(\"{ContentWords.Draft}\", stored[\"{status}\"]!.GetValue<string>(), \"a save does not change the status\");");
            L("        await RemoveAsync(row);");
            L("    }");
            if (!content.NeedsApproval)
            {
            L();
            L("    [TestMethod]");
            L("    public async Task Publish_unpublish_and_archive_change_the_status_and_only_a_published_row_is_public()");
            L("    {");
            L("        ApiFixture.RequireDatabase();");
            L("        string token = Token();");
            L($"        var row = await MakeAsync(b => {{ b[\"{title}\"] = \"Pub \" + token; b[\"{slug}\"] = \"\"; }});");
            L($"        long key = row[\"{id}\"]!.GetValue<long>();");
            L($"        string address = row[\"{slug}\"]!.GetValue<string>();");
            bool tenantOwned = content.TenantColumn is not null;   // the public routes read the tenant of the host, which TenantBypassApiTests plays
            if (!tenantOwned)
            {
                L(project.ApiAuth ? "        using var reader = ApiFixture.Anonymous();" : "        var reader = ApiFixture.Client;   // the API has no sign-in, so this client reads as anyone would");
                L("        async Task<HttpStatusCode> PublicAsync() { using var read = await reader.GetAsync($\"{Route}/public/{address}\"); return read.StatusCode; }");
            }
            L("        async Task<JsonObject> ChangeAsync(string action)");
            L("        {");
            L("            using var changed = await ApiFixture.Client.PostAsync($\"{Route}/{key}/{action}\", null);");
            L("            Assert.AreEqual(HttpStatusCode.OK, changed.StatusCode, await changed.Content.ReadAsStringAsync());");
            L("            return (await changed.Content.ReadFromJsonAsync<JsonObject>())!;");
            L("        }");
            if (!tenantOwned)
                L("        Assert.AreEqual(HttpStatusCode.NotFound, await PublicAsync(), \"a draft is not public\");");
            L("        var published = await ChangeAsync(\"publish\");");
            L($"        Assert.AreEqual(\"{ContentWords.Published}\", published[\"{status}\"]!.GetValue<string>());");
            if (publishedOn is not null)
                L($"        Assert.IsNotNull(published[\"{publishedOn}\"], \"publishing stamps the time\");");
            if (!tenantOwned)
            {
                L("        Assert.AreEqual(HttpStatusCode.OK, await PublicAsync(), \"a published row is public without a sign-in\");");
                L("        var listed = (await reader.GetFromJsonAsync<JsonArray>($\"{Route}/public?pageSize=100\"))!;");
                L($"        Assert.IsTrue(listed.Any(n => n![\"{slug}\"]!.GetValue<string>() == address), \"the published row is on the first page\");");
            }
            if (!tenantOwned && content.PublicColumns is not null && content.Table.Columns.FirstOrDefault(c => !content.PublicColumns.Contains(c)) is { } hidden)
            {
                L($"        Assert.IsNull(listed.First(n => n![\"{slug}\"]!.GetValue<string>() == address)![\"{JsonNames.Camel(hidden.Name)}\"], \"a column that is not in ContentPublicColumns stays out of the public list\");");
                L($"        using var single = await reader.GetAsync($\"{{Route}}/public/{{address}}\");");
                L($"        Assert.IsNull((await single.Content.ReadFromJsonAsync<JsonObject>())![\"{JsonNames.Camel(hidden.Name)}\"], \"nor from the public row\");");
            }
            L("        var unpublished = await ChangeAsync(\"unpublish\");");
            L($"        Assert.AreEqual(\"{ContentWords.Draft}\", unpublished[\"{status}\"]!.GetValue<string>());");
            if (!tenantOwned)
                L("        Assert.AreEqual(HttpStatusCode.NotFound, await PublicAsync());");
            if (publishedOn is not null)
            {
                L("        var again = await ChangeAsync(\"publish\");");
                L($"        Assert.AreEqual(published[\"{publishedOn}\"]!.GetValue<DateTime>(), again[\"{publishedOn}\"]!.GetValue<DateTime>(), \"publishing again keeps the first time\");");
            }
            L("        var archived = await ChangeAsync(\"archive\");");
            L($"        Assert.AreEqual(\"{ContentWords.Archived}\", archived[\"{status}\"]!.GetValue<string>());");
            if (!tenantOwned)
                L("        Assert.AreEqual(HttpStatusCode.NotFound, await PublicAsync(), \"an archived row is not public\");");
            L($"        using var absent = await ApiFixture.Client.PostAsync($\"{{Route}}/{{{plan.AbsentKey}}}/publish\", null);");
            L("        Assert.AreEqual(HttpStatusCode.NotFound, absent.StatusCode);");
            L("        await RemoveAsync(row);");
            L("    }");
            }
        }

        if (effective is not null)
        {
            string from = JsonNames.Camel(effective.From.Name), to = JsonNames.Camel(effective.To.Name);
            string Day(string text) => $"\"{text}T00:00:00\"";
            L();
            L("    [TestMethod]");
            L("    public async Task A_period_that_runs_backwards_is_refused()");
            L("    {");
            L("        ApiFixture.RequireDatabase();");
            L("        var body = await NewBodyAsync();");
            L($"        body[\"{from}\"] = {Day("1761-02-01")};");
            L($"        body[\"{to}\"] = {Day("1761-01-01")};");
            L("        using var response = await ApiFixture.Client.PostAsJsonAsync(Route, body);");
            L("        Assert.AreEqual(HttpStatusCode.BadRequest, response.StatusCode, await response.Content.ReadAsStringAsync());");
            L($"        Assert.AreEqual(\"{RowShapeWords.EffectiveRangeType}\", (await response.Content.ReadFromJsonAsync<JsonObject>())![\"type\"]!.GetValue<string>());");
            L("    }");
            L();
            L("    [TestMethod]");
            L("    public async Task A_period_that_overlaps_another_row_of_the_group_is_refused_and_a_neighbouring_one_is_not()");
            L("    {");
            L("        ApiFixture.RequireDatabase();");
            L($"        var first = await MakeAsync(b => {{ b[\"{from}\"] = {Day("1762-01-01")}; b[\"{to}\"] = {Day("1762-06-30")}; }});");
            L("        var body = await NewBodyAsync();");
            L($"        body[\"{from}\"] = {Day("1762-06-30")};");
            L($"        body[\"{to}\"] = {Day("1762-12-31")};");
            foreach (var g in effective.Groups)
                L($"        body[\"{JsonNames.Camel(g.Name)}\"] = first[\"{JsonNames.Camel(g.Name)}\"]?.DeepClone();");
            L("        using var overlapping = await ApiFixture.Client.PostAsJsonAsync(Route, body);");
            L("        Assert.AreEqual(HttpStatusCode.Conflict, overlapping.StatusCode, await overlapping.Content.ReadAsStringAsync());");
            L($"        Assert.AreEqual(\"{RowShapeWords.EffectiveOverlapType}\", (await overlapping.Content.ReadFromJsonAsync<JsonObject>())![\"type\"]!.GetValue<string>());");
            L($"        body[\"{from}\"] = {Day("1762-07-01")};");
            L("        using var neighbour = await ApiFixture.Client.PostAsJsonAsync(Route, body);");
            L("        Assert.AreEqual(HttpStatusCode.Created, neighbour.StatusCode, await neighbour.Content.ReadAsStringAsync());");
            L("        var second = (await neighbour.Content.ReadFromJsonAsync<JsonObject>())!;");
            L($"        using var stretched = await PutAsync(first, b => b[\"{to}\"] = {Day("1762-07-15")});");
            L("        Assert.AreEqual(HttpStatusCode.Conflict, stretched.StatusCode, \"a change that makes the periods overlap is refused too\");");
            L("        await RemoveAsync(first, second);");
            L("    }");
            L();
            L("    [TestMethod]");
            L("    public async Task The_current_rows_are_those_in_effect_on_the_day()");
            L("    {");
            L("        ApiFixture.RequireDatabase();");
            L($"        var row = await MakeAsync(b => {{ b[\"{from}\"] = {Day("1763-01-01")}; b[\"{to}\"] = {Day("1763-06-30")}; }});");
            L($"        long key = row[\"{id}\"]!.GetValue<long>();");
            L("        string narrow = \"\"" + string.Concat(effective.Groups.Select(g => $" + \"&{JsonNames.Camel(g.Name)}=\" + row[\"{JsonNames.Camel(g.Name)}\"]")) + ";");
            L("        async Task<bool> InEffect(string day)");
            L("        {");
            L("            var rows = (await ApiFixture.Client.GetFromJsonAsync<JsonArray>($\"{Route}/current?on={day}{narrow}\"))!;");
            L($"            return rows.Any(r => r![\"{id}\"]!.GetValue<long>() == key);");
            L("        }");
            L("        Assert.IsTrue(await InEffect(\"1763-01-01\"), \"the first day is in effect\");");
            L("        Assert.IsTrue(await InEffect(\"1763-06-30\"), \"the last day is in effect\");");
            L("        Assert.IsFalse(await InEffect(\"1763-07-01\"), \"the day after is not\");");
            L("        Assert.IsFalse(await InEffect(\"1762-12-31\"), \"the day before is not\");");
            L("        await RemoveAsync(row);");
            L("    }");
            if (effective.To.IsNullable)
            {
                L();
                L("    [TestMethod]");
                L("    public async Task A_period_with_no_last_day_overlaps_every_later_period_of_the_group()");
                L("    {");
                L("        ApiFixture.RequireDatabase();");
                L("        var firstBody = await NewBodyAsync();");
                L($"        firstBody[\"{from}\"] = {Day("9004-01-01")};");
                L($"        firstBody[\"{to}\"] = null;");
                L("        using var openResponse = await ApiFixture.Client.PostAsJsonAsync(Route, firstBody);");
                L("        if (openResponse.StatusCode == HttpStatusCode.Conflict)");
                L("            Assert.Inconclusive(\"the group already has a period with no last day, which overlaps this one\");");
                L("        Assert.AreEqual(HttpStatusCode.Created, openResponse.StatusCode, await openResponse.Content.ReadAsStringAsync());");
                L("        var open = (await openResponse.Content.ReadFromJsonAsync<JsonObject>())!;");
                L("        var body = await NewBodyAsync();");
                L($"        body[\"{from}\"] = {Day("9005-01-01")};");
                L($"        body[\"{to}\"] = {Day("9005-12-31")};");
                foreach (var g in effective.Groups)
                    L($"        body[\"{JsonNames.Camel(g.Name)}\"] = open[\"{JsonNames.Camel(g.Name)}\"]?.DeepClone();");
                L("        using var later = await ApiFixture.Client.PostAsJsonAsync(Route, body);");
                L("        Assert.AreEqual(HttpStatusCode.Conflict, later.StatusCode, await later.Content.ReadAsStringAsync());");
                L("        await RemoveAsync(open);");
                L("    }");
            }
        }
        return o.ToString();
    }
}
