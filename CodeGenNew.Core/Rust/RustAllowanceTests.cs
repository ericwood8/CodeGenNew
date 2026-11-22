using System.Text;
using CodeGenNew.Core.Allowance;
using CodeGenNew.Core.Naming;

namespace CodeGenNew.Core.Rust;

/// <summary> <c>tests/api/allowance_routes.rs</c> of the Rust API (RS_TestAllowance): the same cases and words as the Python tests (<see cref="PythonAllowanceTests"/>, <see cref="AllowanceChargeWords"/>), played in
/// process through the router. </summary>
public static class RustAllowanceTests
{
    public static bool Applies(DatabaseModel database, ProjectSettings project) => project.RustTests && AllowanceTestPlan.For(database, project) is not null;

    public static string File(DatabaseModel database, ProjectSettings project)
    {
        var plan = AllowanceTestPlan.For(database, project)!;
        var s = plan.Shape;
        string Json(ColumnModel c) => JsonNames.Camel(c.Name);
        var o = new StringBuilder();
        void L(string text = "") => o.Append(text).Append('\n');
        string key = Json(s.Table.PrimaryKeyColumns[0]);

        L("// The allowances and charges through the router: the amount is worked out from the base and the percentage (or the percentage from the amount), a negative value, a percentage with no base and a row with"
            + " nothing to work from are refused" + (s.Document is null ? "" : "; the totals of a document add up") + ".");
        L("// Written by RS_TestAllowance; regenerated with the tables. Each test adds its own rows and removes them again.");
        L("use axum::http::StatusCode;");
        L("use serde_json::{Value, json};");
        L();
        L("use crate::test_support::*;");
        L();
        L($"const ROUTE: &str = {RustNames.Str(RustTable.Of(s.Table, project).Route)};");
        L($"const INVALID: &str = \"{AllowanceChargeWords.InvalidType}\";");
        L();
        L("fn number(value: &Value) -> f64 {");
        L("    value.as_f64().or_else(|| value.as_str().and_then(|text| text.parse().ok())).unwrap_or(f64::NAN)");
        L("}");
        L();
        L("async fn add(api: &Client, charge: bool, base: f64, percentage: f64, amount: f64, document: i64, line: i64) -> Reply {");
        L($"    let mut body = json!({plan.Body});");
        L($"    body[\"{Json(s.Flag)}\"] = json!(charge);");
        L($"    body[\"{Json(s.Base)}\"] = json!(base);");
        L($"    body[\"{Json(s.Percent)}\"] = json!(percentage);");
        L($"    body[\"{Json(s.Amount)}\"] = json!(amount);");
        if (s.Document is not null)
            L($"    body[\"{Json(s.Document)}\"] = json!(document);");
        else
            L("    let _ = document;");
        if (s.Line is not null)
            L($"    body[\"{Json(s.Line)}\"] = json!(line);");
        else
            L("    let _ = line;");
        L("    api.post(ROUTE, &body).await");
        L("}");
        L();
        L("async fn remove(api: &Client, rows: &[&Reply]) {");
        L("    for made in rows {");
        L("        if made.status == StatusCode::CREATED {");
        L($"            api.delete(&format!(\"{{ROUTE}}/{{}}\", made.json()[\"{key}\"])).await;");
        L("        }");
        L("    }");
        L("}");
        L();
        L("#[tokio::test]");
        L("async fn the_amount_is_worked_out_from_the_base_and_the_percentage() {");
        L("    let Some(api) = Client::start().await else { return };");
        L("    let made = add(&api, false, 200.0, 10.0, 1.0, 1, 1).await;");
        L("    let row = if made.status == StatusCode::CREATED {");
        L($"        Some(api.get(&format!(\"{{ROUTE}}/{{}}\", made.json()[\"{key}\"])).await.json())");
        L("    } else {");
        L("        None");
        L("    };");
        L("    remove(&api, &[&made]).await;");
        L("    assert_eq!(made.status, StatusCode::CREATED, \"{}\", made.body);");
        L($"    assert!((number(&row.unwrap()[\"{Json(s.Amount)}\"]) - 20.0).abs() < 0.001, \"the amount sent is ignored when a percentage is given\");");
        L("}");
        L();
        L("#[tokio::test]");
        L("async fn the_percentage_is_worked_out_from_the_base_and_the_amount() {");
        L("    let Some(api) = Client::start().await else { return };");
        L("    let made = add(&api, true, 200.0, 0.0, 50.0, 1, 1).await;");
        L("    let row = if made.status == StatusCode::CREATED {");
        L($"        Some(api.get(&format!(\"{{ROUTE}}/{{}}\", made.json()[\"{key}\"])).await.json())");
        L("    } else {");
        L("        None");
        L("    };");
        L("    remove(&api, &[&made]).await;");
        L("    assert_eq!(made.status, StatusCode::CREATED, \"{}\", made.body);");
        L("    let row = row.unwrap();");
        L($"    assert!((number(&row[\"{Json(s.Percent)}\"]) - 25.0).abs() < 0.001 && (number(&row[\"{Json(s.Amount)}\"]) - 50.0).abs() < 0.001);");
        L($"    assert_eq!(row[\"{Json(s.Flag)}\"], json!(true), \"the amount stays positive, the flag says it is a charge\");");
        L("}");
        foreach (var (name, args) in new[] { ("a_negative_value_is_refused", "false, -5.0, 10.0, 1.0"), ("a_percentage_without_a_base_is_refused", "false, 0.0, 10.0, 0.0"), ("a_row_with_nothing_to_work_an_amount_from_is_refused", "false, 0.0, 0.0, 0.0") })
        {
            L();
            L("#[tokio::test]");
            L($"async fn {name}() {{");
            L("    let Some(api) = Client::start().await else { return };");
            L($"    let refused = add(&api, {args}, 1, 1).await;");
            L("    remove(&api, &[&refused]).await;");
            L("    assert!(refused.status == StatusCode::BAD_REQUEST && refused.body.contains(INVALID), \"{}\", refused.body);");
            L("}");
        }
        if (s.Document is not null)
        {
            L();
            L("#[tokio::test]");
            L("async fn the_totals_of_a_document_add_up_the_allowances_and_the_charges() {");
            L("    let Some(api) = Client::start().await else { return };");
            L("    let document = 800_000 + (uuid::Uuid::new_v4().as_u128() % 100_000) as i64;");
            L("    let a = add(&api, false, 200.0, 10.0, 0.0, document, 1).await;");
            L("    let b = add(&api, false, 0.0, 0.0, 5.0, document, 1).await;");
            L("    let c = add(&api, true, 0.0, 0.0, 60.0, document, 1).await;");
            L($"    let answer = api.get(&format!(\"{{ROUTE}}/{AllowanceChargeWords.TotalsRoute}?document={{document}}\")).await;");
            L($"    let missing = api.get(&format!(\"{{ROUTE}}/{AllowanceChargeWords.TotalsRoute}\")).await;");
            L("    remove(&api, &[&a, &b, &c]).await;");
            L("    assert_eq!(answer.status, StatusCode::OK, \"{}\", answer.body);");
            L("    let totals = answer.json();");
            L("    assert!((number(&totals[\"allowances\"]) - 25.0).abs() < 0.001 && (number(&totals[\"charges\"]) - 60.0).abs() < 0.001 && (number(&totals[\"net\"]) - 35.0).abs() < 0.001);");
            L("    assert_eq!(totals[\"rows\"].as_i64(), Some(3));");
            L("    assert_eq!(missing.status, StatusCode::BAD_REQUEST);");
            L("}");
            if (s.Line is not null)
            {
                L();
                L("#[tokio::test]");
                L("async fn the_totals_of_one_line_count_only_the_rows_of_that_line() {");
                L("    let Some(api) = Client::start().await else { return };");
                L("    let document = 800_000 + (uuid::Uuid::new_v4().as_u128() % 100_000) as i64;");
                L("    let a = add(&api, false, 200.0, 10.0, 0.0, document, 1).await;");
                L("    let b = add(&api, true, 0.0, 0.0, 60.0, document, 2).await;");
                L($"    let answer = api.get(&format!(\"{{ROUTE}}/{AllowanceChargeWords.TotalsRoute}?document={{document}}&line=1\")).await;");
                L("    remove(&api, &[&a, &b]).await;");
                L("    assert_eq!(answer.status, StatusCode::OK, \"{}\", answer.body);");
                L("    let totals = answer.json();");
                L("    assert!((number(&totals[\"allowances\"]) - 20.0).abs() < 0.001 && number(&totals[\"charges\"]) == 0.0);");
                L("    assert_eq!(totals[\"rows\"].as_i64(), Some(1));");
                L("}");
            }
        }
        return o.ToString();
    }
}
