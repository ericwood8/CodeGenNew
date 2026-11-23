using CodeGenNew.Core.Catalog;

namespace CodeGenNew.Core.Rust;

/// <summary> <c>tests/api/catalog_routes.rs</c> of the Rust API (RS_TestCatalog): the same cases and words as the Python tests (<see cref="PythonCatalogTests"/>, <see cref="CatalogWords"/>), played in process over a scratch database. Each case adds products of its own
/// whose title starts with a token of its own and asks for that keyword, so the rows other cases left in the scratch database do not count. </summary>
public static class RustCatalogTests
{
    public static bool Applies(DatabaseModel database, ProjectSettings project) => project.RustTests && CatalogTestPlan.For(database, project) is not null;

    public static string File(DatabaseModel database, ProjectSettings project)
    {
        var plan = CatalogTestPlan.For(database, project)!;
        string numberSet = plan.NumberJson is null ? "" : $"        if let Some(number) = number {{\n            row[{RustNames.Str(plan.NumberJson)}] = json!(number);\n        }}\n";
        string text = plan.NumberJson is null ? Text : Text + RangeText;
        var names = new (string, string)[]
        {
            ("§NUMBERSET", numberSet),
            ("§PRODUCTROUTE", RustNames.Str(plan.ProductRoute)), ("§ATTRIBUTEROUTE", RustNames.Str(plan.AttributeRoute)), ("§PRODUCTBODY", plan.ProductBody), ("§ATTRIBUTEBODY", plan.AttributeBody),
            ("§UNIQUE", string.Join(", ", plan.UniqueJson.Select(RustNames.Str))), ("§KEYJSON", RustNames.Str(plan.KeyJson)), ("§TITLEJSON", RustNames.Str(plan.TitleJson)), ("§PRODUCTCOLUMN", RustNames.Str(plan.ProductColumnJson)),
            ("§NAMEJSON", RustNames.Str(plan.NameJson)), ("§VALUEJSON", RustNames.Str(plan.ValueJson)), ("§T_FILTER", CatalogWords.BadFilterType), ("§T_PAGE", CatalogWords.BadPageType),
        };
        return TemplateText.FillAll(text, names) + "\n";
    }

    private const string Text = """"
// Catalog search with facets through the routes of the product table: the counts of each attribute value, the values of one attribute as alternatives, an attribute counted without its own filter, the keyword over the attribute values,
// paging and the refusals. Written by RS_TestCatalog; regenerated with the tables. Each case adds products of its own.
use std::collections::BTreeMap;
use std::sync::atomic::{AtomicUsize, Ordering};

use axum::http::StatusCode;
use serde_json::{Value, json};

use crate::test_support::*;

const PRODUCTS: &str = §PRODUCTROUTE;
const ATTRIBUTES: &str = §ATTRIBUTEROUTE;
const UNIQUE: &[&str] = &[§UNIQUE];
static SERIAL: AtomicUsize = AtomicUsize::new(0);

fn token() -> String {
    let nanos = std::time::SystemTime::now().duration_since(std::time::UNIX_EPOCH).map(|d| d.subsec_nanos()).unwrap_or(0);
    format!("zz{:x}{:x}", nanos, SERIAL.fetch_add(1, Ordering::SeqCst))
}

/// A product whose title starts with the token, with its attributes: a name, a value and a number.
async fn product(api: &Client, token: &str, text: &str, attributes: &[(&str, &str, Option<f64>)]) -> Value {
    let mut body = json!(§PRODUCTBODY);
    for name in UNIQUE {
        body[*name] = json!(format!("{token}{}", SERIAL.fetch_add(1, Ordering::SeqCst)));
    }
    body[§TITLEJSON] = json!(format!("{token} {text}").trim());
    let made = api.post(PRODUCTS, &body).await;
    assert_eq!(made.status, StatusCode::CREATED, "{}", made.body);
    let key = made.json()[§KEYJSON].clone();
    for (name, value, number) in attributes {
        let mut row = json!(§ATTRIBUTEBODY);
        row[§PRODUCTCOLUMN] = key.clone();
        row[§NAMEJSON] = json!(name);
        row[§VALUEJSON] = json!(value);
§NUMBERSET        let _ = number;
        let added = api.post(ATTRIBUTES, &row).await;
        assert_eq!(added.status, StatusCode::CREATED, "{}", added.body);
    }
    key
}

async fn ask(api: &Client, route: &str, query: &str) -> Reply {
    api.get(&format!("{PRODUCTS}/{route}?{query}")).await
}

async fn facets(api: &Client, token: &str, more: &str) -> Value {
    let answer = ask(api, "facets", &format!("q={token}{more}")).await;
    assert_eq!(answer.status, StatusCode::OK, "{}", answer.body);
    answer.json()
}

fn counts(answer: &Value) -> BTreeMap<String, BTreeMap<String, i64>> {
    answer["facets"].as_array().unwrap().iter().map(|group| {
        let values = group["values"].as_array().unwrap().iter().map(|item| (item["value"].as_str().unwrap().to_string(), item["count"].as_i64().unwrap())).collect();
        (group["name"].as_str().unwrap().to_string(), values)
    }).collect()
}

fn counted(pairs: &[(&str, i64)]) -> BTreeMap<String, i64> {
    pairs.iter().map(|(value, count)| (value.to_string(), *count)).collect()
}

#[tokio::test]
async fn the_facets_count_the_products_that_carry_each_value_and_an_attribute_ignores_its_own_filter() {
    let Some(api) = Client::start().await else { return };
    let token = token();
    product(&api, &token, "a", &[("Material", "Copper", None), ("Color", "Red", None)]).await;
    product(&api, &token, "b", &[("Material", "Copper", None), ("Color", "Blue", None)]).await;
    product(&api, &token, "c", &[("Material", "Steel", None), ("Color", "Red", None)]).await;
    let everything = facets(&api, &token, "").await;
    assert_eq!(everything["total"], 3);
    assert_eq!(counts(&everything)["Material"], counted(&[("Copper", 2), ("Steel", 1)]));
    let chosen = facets(&api, &token, "&filter=Material:Copper").await;
    assert_eq!(chosen["total"], 2);
    assert_eq!(counts(&chosen)["Material"], counted(&[("Copper", 2), ("Steel", 1)]), "an attribute is counted without its own filter");
    assert_eq!(counts(&chosen)["Color"], counted(&[("Red", 1), ("Blue", 1)]));
    assert_eq!(facets(&api, &token, "&filter=Material:Copper&filter=Material:Steel").await["total"], 3, "several values of one attribute are alternatives");
    assert_eq!(facets(&api, &token, "&filter=Material:Copper&filter=Color:Red").await["total"], 1, "different attributes all have to match");
    let missing = facets(&api, &token, "&filter=Material:Copper&filter=Color:Green").await;
    assert_eq!(missing["total"], 0);
    assert_eq!(counts(&missing)["Color"].get("Green"), Some(&0));
}

#[tokio::test]
async fn the_keyword_reads_the_attribute_values_in_any_case() {
    let Some(api) = Client::start().await else { return };
    let token = token();
    let word = format!("zq{}", token.trim_start_matches("zz"));
    product(&api, &token, "plain", &[("Finish", &word, None)]).await;
    product(&api, &token, "other", &[("Finish", "Matte", None)]).await;
    let found = ask(&api, "narrow", &format!("q={}", word.to_uppercase())).await;
    assert_eq!(found.json()["total"], 1, "{}", found.body);
    assert_eq!(ask(&api, "narrow", &format!("q={token}")).await.json()["total"], 2);
}

#[tokio::test]
async fn narrow_pages_the_matching_products_in_key_order() {
    let Some(api) = Client::start().await else { return };
    let token = token();
    let mut keys = Vec::new();
    for n in 0..3 {
        keys.push(product(&api, &token, &n.to_string(), &[("Material", "Copper", None)]).await);
    }
    let first = ask(&api, "narrow", &format!("q={token}&pageSize=2")).await.json();
    assert_eq!(first["total"], 3);
    assert_eq!(first["items"].as_array().unwrap().iter().map(|item| item["id"].clone()).collect::<Vec<_>>(), keys[..2].to_vec());
    let second = ask(&api, "narrow", &format!("q={token}&pageSize=2&page=2")).await.json();
    assert_eq!(second["items"].as_array().unwrap().iter().map(|item| item["id"].clone()).collect::<Vec<_>>(), keys[2..].to_vec());
    assert_eq!(ask(&api, "narrow", "pageSize=100000").await.json()["pageSize"], 200);
}

#[tokio::test]
async fn a_bad_filter_and_a_bad_page_are_refused() {
    let Some(api) = Client::start().await else { return };
    let bad = ask(&api, "facets", "filter=Material").await;
    assert_eq!(bad.status, StatusCode::BAD_REQUEST, "{}", bad.body);
    assert!(bad.json()["type"].as_str().unwrap_or("").ends_with("§T_FILTER"), "{}", bad.body);
    let page = ask(&api, "narrow", "page=0").await;
    assert_eq!(page.status, StatusCode::BAD_REQUEST, "{}", page.body);
    assert!(page.json()["type"].as_str().unwrap_or("").ends_with("§T_PAGE"), "{}", page.body);
}
"""";

    private const string RangeText = """"

#[tokio::test]
async fn a_range_keeps_the_products_with_a_number_inside_it_and_the_group_tells_its_ends() {
    let Some(api) = Client::start().await else { return };
    let token = token();
    product(&api, &token, "a", &[("Voltage", "120 V", Some(120.0))]).await;
    product(&api, &token, "b", &[("Voltage", "240 V", Some(240.0))]).await;
    product(&api, &token, "c", &[("Voltage", "600 V", Some(600.0))]).await;
    let inside = facets(&api, &token, "&range=Voltage:100:300").await;
    let voltage = inside["facets"].as_array().unwrap().iter().find(|group| group["name"] == "Voltage").unwrap().clone();
    assert_eq!(inside["total"], 2);
    assert_eq!(voltage["min"].as_f64(), Some(120.0));
    assert_eq!(voltage["max"].as_f64(), Some(600.0));
    assert_eq!(voltage["selectedMin"].as_f64(), Some(100.0));
    assert_eq!(voltage["selectedMax"].as_f64(), Some(300.0));
    assert_eq!(facets(&api, &token, "&range=Voltage::200").await["total"], 1);
    assert_eq!(facets(&api, &token, "&range=Voltage:500:").await["total"], 1);
    assert_eq!(facets(&api, &token, "&range=Voltage:1000:2000").await["total"], 0);
}

#[tokio::test]
async fn a_range_without_numbers_is_refused() {
    let Some(api) = Client::start().await else { return };
    for value in ["Voltage::", "Voltage:abc:5", "Voltage:100"] {
        let bad = ask(&api, "narrow", &format!("range={value}")).await;
        assert_eq!(bad.status, StatusCode::BAD_REQUEST, "{}", bad.body);
        assert!(bad.json()["type"].as_str().unwrap_or("").ends_with("§T_FILTER"), "{}", bad.body);
    }
}
"""";
}
