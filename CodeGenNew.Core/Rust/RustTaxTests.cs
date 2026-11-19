using System.Text;
using CodeGenNew.Core.Naming;
using CodeGenNew.Core.Tax;

namespace CodeGenNew.Core.Rust;

/// <summary> <c>tests/api/tax_routes.rs</c> of the Rust API (RS_TestTax): the same cases and words as the Python tests (<see cref="PythonTaxTests"/>, <see cref="TaxWords"/>), played in process through the router. </summary>
public static class RustTaxTests
{
    public static bool Applies(DatabaseModel database, ProjectSettings project) => project.RustTests && TaxTestPlan.For(database, project) is not null;

    public static string File(DatabaseModel database, ProjectSettings project)
    {
        var plan = TaxTestPlan.For(database, project)!;
        var s = plan.Shape;
        string Json(ColumnModel c) => JsonNames.Camel(c.Name);
        var o = new StringBuilder();
        void L(string text = "") => o.Append(text).Append('\n');

        L("// The tax lines through the router: a line carries the rate of its category and the tax worked out from it, keeps the rate when the category's rate changes, and refuses a category that does not exist"
            + (s.ValidFrom is null ? "" : " or is not in force") + (s.Document is null ? "" : "; the subtotals of a document add up by category and rate") + ".");
        L("// Written by RS_TestTax; regenerated with the tables. Each test adds its own rows and removes them again.");
        L("use axum::http::StatusCode;");
        L("use serde_json::{Value, json};");
        L();
        L("use crate::test_support::*;");
        L();
        L($"const LINES: &str = {RustNames.Str(RustTable.Of(s.Table, project).Route)};");
        L($"const CATEGORIES: &str = {RustNames.Str(RustTable.Of(s.CategoryTable, project).Route)};");
        L($"const UNKNOWN: &str = \"{TaxWords.UnknownType}\";");
        if (s.ValidFrom is not null)
            L($"const NOT_IN_FORCE: &str = \"{TaxWords.NotInForceType}\";");
        L();
        L("fn number(value: &Value) -> f64 {");
        L("    value.as_f64().or_else(|| value.as_str().and_then(|text| text.parse().ok())).unwrap_or(f64::NAN)");
        L("}");
        L();
        L("async fn category(api: &Client, rate: f64, valid_from: Option<&str>, valid_to: Option<&str>) -> i64 {");
        L($"    let mut body = json!({plan.CategoryBody});");
        L($"    body[\"{Json(s.CategoryRate)}\"] = json!(rate);");
        if (s.ValidFrom is not null)
        {
            L($"    body[\"{Json(s.ValidFrom)}\"] = json!(valid_from.unwrap_or(\"{TaxTestPlan.Day(s.ValidFrom, "2000-01-01")}\"));");
            if (s.ValidTo is not null)
                L($"    body[\"{Json(s.ValidTo)}\"] = json!(valid_to);");
        }
        else
            L("    let _ = (valid_from, valid_to);");
        foreach (var c in TaxTestPlan.UniqueTextColumns(s))
            L($"    body[\"{Json(c)}\"] = json!(uuid::Uuid::new_v4().simple().to_string()[..{(c.CharacterLength > 0 ? Math.Min(8, c.CharacterLength) : 8)}].to_string());");
        L("    let made = api.post(CATEGORIES, &body).await;");
        L("    assert_eq!(made.status, StatusCode::CREATED, \"{}\", made.body);");
        L($"    made.json()[\"{Json(s.CategoryKey)}\"].as_i64().unwrap()");
        L("}");
        L();
        L("async fn line(api: &Client, category: Option<i64>, total: f64, day: Option<&str>, document: i64) -> Reply {");
        L($"    let mut body = json!({plan.LineBody});");
        L($"    body[\"{Json(s.Category)}\"] = json!(category);");
        L($"    body[\"{Json(s.Taxable)}\"] = json!(total);");
        L($"    body[\"{Json(s.Rate)}\"] = json!(99);");
        L($"    body[\"{Json(s.Tax)}\"] = json!(1);");
        if (s.Day is not null)
            L($"    body[\"{Json(s.Day)}\"] = json!(day.unwrap_or(\"{TaxTestPlan.Day(s.Day, "2024-06-01")}\"));");
        else
            L("    let _ = day;");
        if (s.Document is null)
            L("    let _ = document;");
        if (s.Document is not null)
            L($"    body[\"{Json(s.Document)}\"] = json!(document);");
        L("    api.post(LINES, &body).await");
        L("}");
        L();
        L("async fn remove(api: &Client, lines: &[&Reply], categories: &[i64]) {");
        L("    for made in lines {");
        L("        if made.status == StatusCode::CREATED {");
        L($"            api.delete(&format!(\"{{LINES}}/{{}}\", made.json()[\"{Json(s.Key)}\"])).await;");
        L("        }");
        L("    }");
        L("    for key in categories {");
        L("        api.delete(&format!(\"{CATEGORIES}/{key}\")).await;");
        L("    }");
        L("}");
        L();
        L("#[tokio::test]");
        L("async fn a_line_carries_the_rate_of_its_category_and_the_tax_worked_out_from_it() {");
        L("    let Some(api) = Client::start().await else { return };");
        L("    let category = category(&api, 0.2, None, None).await;");
        L("    let made = line(&api, Some(category), 50.0, None, 1).await;");
        L("    let row = if made.status == StatusCode::CREATED {");
        L($"        Some(api.get(&format!(\"{{LINES}}/{{}}\", made.json()[\"{Json(s.Key)}\"])).await.json())");
        L("    } else {");
        L("        None");
        L("    };");
        L("    remove(&api, &[&made], &[category]).await;");
        L("    assert_eq!(made.status, StatusCode::CREATED, \"{}\", made.body);");
        L("    let row = row.unwrap();");
        L($"    assert_eq!(number(&row[\"{Json(s.Rate)}\"]), 0.2, \"the rate is the category's, not the one sent\");");
        L($"    assert_eq!(number(&row[\"{Json(s.Tax)}\"]), 10.0, \"the tax is the taxable amount times the rate over 100\");");
        L("}");
        L();
        L("#[tokio::test]");
        L("async fn a_line_keeps_its_rate_when_the_rate_of_its_category_changes() {");
        L("    let Some(api) = Client::start().await else { return };");
        L("    let category = category(&api, 0.2, None, None).await;");
        L("    let first = line(&api, Some(category), 100.0, None, 1).await;");
        L("    assert_eq!(first.status, StatusCode::CREATED, \"{}\", first.body);");
        L($"    let route = format!(\"{{LINES}}/{{}}\", first.json()[\"{Json(s.Key)}\"]);");
        L("    let mut stored = api.get(&format!(\"{CATEGORIES}/{category}\")).await.json();");
        L($"    stored[\"{Json(s.CategoryRate)}\"] = json!(0.25);");
        L("    let changed = api.put(&format!(\"{CATEGORIES}/{category}\"), &stored).await;");
        L("    let mut row = api.get(&route).await.json();");
        L($"    row[\"{Json(s.Taxable)}\"] = json!(200);");
        L("    let updated = api.put(&route, &row).await;");
        L("    let row = api.get(&route).await.json();");
        L("    let later = line(&api, Some(category), 100.0, None, 1).await;");
        L("    let later_row = if later.status == StatusCode::CREATED {");
        L($"        Some(api.get(&format!(\"{{LINES}}/{{}}\", later.json()[\"{Json(s.Key)}\"])).await.json())");
        L("    } else {");
        L("        None");
        L("    };");
        L("    remove(&api, &[&first, &later], &[category]).await;");
        L("    assert!(changed.status.is_success() && updated.status.is_success(), \"{} {}\", changed.body, updated.body);");
        L($"    assert_eq!(number(&row[\"{Json(s.Rate)}\"]), 0.2, \"the rate the line was written with stays\");");
        L($"    assert_eq!(number(&row[\"{Json(s.Tax)}\"]), 40.0);");
        L($"    assert_eq!(number(&later_row.unwrap()[\"{Json(s.Rate)}\"]), 0.25, \"a new line takes the category's new rate\");");
        L("}");
        L();
        L("#[tokio::test]");
        L("async fn a_line_with_a_category_that_does_not_exist_is_refused() {");
        L("    let Some(api) = Client::start().await else { return };");
        L("    let refused = line(&api, Some(2_000_000_000), 10.0, None, 1).await;");
        L("    remove(&api, &[&refused], &[]).await;");
        L("    assert!(refused.status == StatusCode::BAD_REQUEST && refused.body.contains(UNKNOWN), \"{}\", refused.body);");
        L("}");
        if (s.ValidFrom is not null && s.Day is not null)
        {
            L();
            L("#[tokio::test]");
            L("async fn a_category_outside_its_validity_is_refused() {");
            L("    let Some(api) = Client::start().await else { return };");
            L($"    let category = category(&api, 0.2, Some(\"{TaxTestPlan.Day(s.ValidFrom, "2024-01-01")}\"), " + (s.ValidTo is null ? "None" : $"Some(\"{TaxTestPlan.Day(s.ValidTo, "2024-12-31")}\")") + ").await;");
            L($"    let inside = line(&api, Some(category), 10.0, Some(\"{TaxTestPlan.Day(s.Day, "2024-06-01")}\"), 1).await;");
            L($"    let before = line(&api, Some(category), 10.0, Some(\"{TaxTestPlan.Day(s.Day, "2023-12-31")}\"), 1).await;");
            if (s.ValidTo is not null)
                L($"    let after = line(&api, Some(category), 10.0, Some(\"{TaxTestPlan.Day(s.Day, "2025-01-01")}\"), 1).await;");
            L("    remove(&api, &[&inside, &before" + (s.ValidTo is not null ? ", &after" : "") + "], &[category]).await;");
            L("    assert_eq!(inside.status, StatusCode::CREATED, \"{}\", inside.body);");
            L("    assert!(before.status == StatusCode::BAD_REQUEST && before.body.contains(NOT_IN_FORCE), \"{}\", before.body);");
            if (s.ValidTo is not null)
                L("    assert!(after.status == StatusCode::BAD_REQUEST && after.body.contains(NOT_IN_FORCE), \"{}\", after.body);");
            L("}");
        }
        if (s.Document is not null)
        {
            L();
            L("#[tokio::test]");
            L("async fn the_subtotals_of_a_document_add_up_by_category_and_rate() {");
            L("    let Some(api) = Client::start().await else { return };");
            L("    let document = 800_000 + (uuid::Uuid::new_v4().as_u128() % 100_000) as i64;");
            L("    let twenty = category(&api, 0.2, None, None).await;");
            L("    let five = category(&api, 0.05, None, None).await;");
            L("    let a = line(&api, Some(twenty), 100.0, None, document).await;");
            L("    let b = line(&api, Some(twenty), 50.0, None, document).await;");
            L("    let c = line(&api, Some(five), 200.0, None, document).await;");
            L("    let answer = api.get(&format!(\"{LINES}/tax-subtotals?document={document}\")).await;");
            L("    let missing = api.get(&format!(\"{LINES}/tax-subtotals\")).await;");
            L("    remove(&api, &[&a, &b, &c], &[twenty, five]).await;");
            L("    assert_eq!(answer.status, StatusCode::OK, \"{}\", answer.body);");
            L("    let rows = answer.json();");
            L("    let find = |category: i64| rows.as_array().unwrap().iter().find(|row| row[\"category\"].as_i64() == Some(category)).unwrap().clone();");
            L("    assert_eq!(rows.as_array().unwrap().len(), 2);");
            L("    let first = find(twenty);");
            L("    assert_eq!((number(&first[\"taxable\"]), number(&first[\"tax\"]), first[\"lines\"].as_i64()), (150.0, 30.0, Some(2)));");
            L("    assert_eq!(number(&find(five)[\"tax\"]), 10.0);");
            L("    assert_eq!(missing.status, StatusCode::BAD_REQUEST);");
            L("}");
        }
        return o.ToString();
    }
}
