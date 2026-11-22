using System.Text;
using CodeGenNew.Core.Naming;
using CodeGenNew.Core.Totals;

namespace CodeGenNew.Core.Rust;

/// <summary> <c>tests/api/totals_routes.rs</c> of the Rust API (RS_TestTotals): the same cases and words as the Python tests (<see cref="PythonTotalsTests"/>, <see cref="DocumentTotalsWords"/>), played in process
/// through the router. </summary>
public static class RustTotalsTests
{
    public static bool Applies(DatabaseModel database, ProjectSettings project) => project.RustTests && DocumentTotalsTestPlan.For(database, project) is not null;

    public static string File(DatabaseModel database, ProjectSettings project)
    {
        var plan = DocumentTotalsTestPlan.For(database, project)!;
        var s = plan.Shape;
        string Json(ColumnModel c) => JsonNames.Camel(c.Name);
        var o = new StringBuilder();
        void L(string text = "") => o.Append(text).Append('\n');
        string headerKey = Json(s.Key), lineKey = Json(s.Lines.PrimaryKeyColumns[0]), lines = Json(s.Columns.Lines!);
        var owned = s.Columns.Owned().Select(p => Json(p.Column)).ToList();

        L("// The document totals through the router: the totals a client writes are replaced by the ones worked out from the rows, the lines total follows every line added or removed"
            + (plan.TestsPrepaid ? ", the payable amount follows the prepaid amount" : "") + " and the check route finds nothing wrong.");
        L("// Written by RS_TestTotals; regenerated with the tables. Each test adds its own rows and removes them again.");
        L("use axum::http::StatusCode;");
        L("use serde_json::{Value, json};");
        L();
        L("use crate::test_support::*;");
        L();
        L($"const HEADERS: &str = {RustNames.Str(RustTable.Of(s.Header, project).Route)};");
        L($"const LINES: &str = {RustNames.Str(RustTable.Of(s.Lines, project).Route)};");
        L();
        L("fn number(value: &Value) -> f64 {");
        L("    value.as_f64().or_else(|| value.as_str().and_then(|text| text.parse().ok())).unwrap_or(f64::NAN)");
        L("}");
        L();
        L("async fn header(api: &Client, wrong: f64) -> Reply {");
        L($"    let mut body = json!({plan.HeaderBody});");
        foreach (string column in owned)
            L($"    body[\"{column}\"] = json!(wrong);");
        L("    api.post(HEADERS, &body).await");
        L("}");
        L();
        L("async fn line(api: &Client, header: i64, amount: f64) -> Reply {");
        L($"    let mut body = json!({plan.LineBody});");
        L($"    body[\"{Json(s.LineKey)}\"] = json!(header);");
        L($"    body[\"{Json(s.LineAmount)}\"] = json!(amount);");
        L("    api.post(LINES, &body).await");
        L("}");
        L();
        L("async fn read(api: &Client, header: i64) -> Value {");
        L("    api.get(&format!(\"{HEADERS}/{header}\")).await.json()");
        L("}");
        L();
        L("async fn remove(api: &Client, lines: &[i64], headers: &[i64]) {");
        L("    for key in lines {");
        L("        api.delete(&format!(\"{LINES}/{key}\")).await;");
        L("    }");
        L("    for key in headers {");
        L("        api.delete(&format!(\"{HEADERS}/{key}\")).await;");
        L("    }");
        L("}");
        L();
        L("#[tokio::test]");
        L("async fn the_totals_a_client_writes_are_replaced_by_the_worked_out_ones() {");
        L("    let Some(api) = Client::start().await else { return };");
        L("    let made = header(&api, 999.0).await;");
        L("    assert_eq!(made.status, StatusCode::CREATED, \"{}\", made.body);");
        L($"    let key = made.json()[\"{headerKey}\"].as_i64().unwrap();");
        L("    let row = read(&api, key).await;");
        L("    remove(&api, &[], &[key]).await;");
        foreach (string column in owned)
            L($"    assert!(number(&row[\"{column}\"]).abs() < 0.001, \"{column} is worked out from the rows, not taken from the client\");");
        L("}");
        L();
        L("#[tokio::test]");
        L("async fn the_lines_total_follows_every_line_added_and_removed() {");
        L("    let Some(api) = Client::start().await else { return };");
        L("    let made = header(&api, 0.0).await;");
        L("    assert_eq!(made.status, StatusCode::CREATED, \"{}\", made.body);");
        L($"    let key = made.json()[\"{headerKey}\"].as_i64().unwrap();");
        L("    let first = line(&api, key, 10.0).await;");
        L("    let second = line(&api, key, 5.5).await;");
        L($"    let ids: Vec<i64> = [&first, &second].iter().filter_map(|made| made.json()[\"{lineKey}\"].as_i64()).collect();");
        L("    let both = read(&api, key).await;");
        L("    let removed = api.delete(&format!(\"{LINES}/{}\", ids[1])).await;");
        L("    let one = read(&api, key).await;");
        L("    remove(&api, &ids[..1], &[key]).await;");
        L("    assert_eq!(first.status, StatusCode::CREATED, \"{}\", first.body);");
        L("    assert_eq!(second.status, StatusCode::CREATED, \"{}\", second.body);");
        L($"    assert!((number(&both[\"{lines}\"]) - 15.5).abs() < 0.001);");
        L("    assert!(removed.status.is_success(), \"{}\", removed.body);");
        L($"    assert!((number(&one[\"{lines}\"]) - 10.0).abs() < 0.001);");
        L("}");
        if (plan.TestsPrepaid)
        {
            string prepaid = Json(s.Columns.Prepaid!), payable = Json(s.Columns.Payable!);
            L();
            L("#[tokio::test]");
            L("async fn the_payable_amount_follows_the_prepaid_amount() {");
            L("    let Some(api) = Client::start().await else { return };");
            L("    let made = header(&api, 0.0).await;");
            L("    assert_eq!(made.status, StatusCode::CREATED, \"{}\", made.body);");
            L($"    let key = made.json()[\"{headerKey}\"].as_i64().unwrap();");
            L("    let first = line(&api, key, 10.0).await;");
            L($"    let line_id = first.json()[\"{lineKey}\"].as_i64().unwrap_or(0);");
            L("    let mut row = read(&api, key).await;");
            L($"    let before = number(&row[\"{payable}\"]);");
            L($"    row[\"{prepaid}\"] = json!(5);");
            L("    let saved = api.put(&format!(\"{HEADERS}/{key}\"), &row).await;");
            L("    let after = read(&api, key).await;");
            L("    remove(&api, &[line_id], &[key]).await;");
            L("    assert!(saved.status.is_success(), \"{}\", saved.body);");
            L($"    assert!((number(&after[\"{payable}\"]) - (before - 5.0)).abs() < 0.001);");
            L("}");
        }
        L();
        L("#[tokio::test]");
        L("async fn the_check_route_finds_nothing_wrong() {");
        L("    let Some(api) = Client::start().await else { return };");
        L("    let made = header(&api, 0.0).await;");
        L("    assert_eq!(made.status, StatusCode::CREATED, \"{}\", made.body);");
        L($"    let key = made.json()[\"{headerKey}\"].as_i64().unwrap();");
        L("    let first = line(&api, key, 10.0).await;");
        L($"    let line_id = first.json()[\"{lineKey}\"].as_i64().unwrap_or(0);");
        L($"    let answer = api.get(&format!(\"{{HEADERS}}/{DocumentTotalsWords.CheckRoute}?id={{key}}\")).await;");
        L("    remove(&api, &[line_id], &[key]).await;");
        L("    assert_eq!(answer.status, StatusCode::OK, \"{}\", answer.body);");
        L("    assert_eq!(answer.json()[\"checked\"].as_i64(), Some(1));");
        L("    assert_eq!(answer.json()[\"differing\"].as_i64(), Some(0), \"{}\", answer.body);");
        L("}");
        return o.ToString();
    }
}
