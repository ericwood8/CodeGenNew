namespace CodeGenNew.Core.Rust;

/// <summary> <c>tests/api/party_routes.rs</c> of the Rust API (RS_TestParties): the cases add two parties and their identifiers through the router and remove them again. The same cases and words as the Python
/// tests (<see cref="PythonPartyTests"/>, <see cref="PartyWords"/>). </summary>
public static class RustPartyTests
{
    public static bool Applies(DatabaseModel database, ProjectSettings project) => project.RustTests && PartyTestPlan.For(database, project) is not null;

    public static string File(DatabaseModel database, ProjectSettings project)
    {
        var plan = PartyTestPlan.For(database, project)!;
        var s = plan.Shape;
        string Json(ColumnModel c) => JsonNames.Camel(c.Name);
        return TemplateText.FillAll(Text, new (string, string)[]
        {
            ("§PARTYROUTE", RustNames.Str(RustTable.Of(s.Party, project).Route)), ("§IDROUTE", RustNames.Str(RustTable.Of(s.Identifiers, project).Route)),
            ("§PARTYBODY", plan.PartyBody), ("§IDBODY", plan.IdentifierBody),
            ("§PARTYKEY", Json(s.PartyKey)), ("§IDKEY", Json(s.IdentifierKey)), ("§LINK", Json(s.Link)), ("§SCHEME", Json(s.Scheme)), ("§VALUE", Json(s.Value)),
            ("§FIRSTLOW", PartyTestPlan.FirstScheme.ToLowerInvariant()), ("§FIRST", PartyTestPlan.FirstScheme), ("§OTHER", PartyTestPlan.OtherScheme), ("§T_TAKEN", PartyWords.IdentifierTakenType)
        }) + "\n";
    }

    private const string Text = """"
// The party table and its identifiers through the router: a scheme and value pair is held by one row, and a party is found by an identifier. Written by RS_TestParties; regenerated with the tables.
// Each test adds two parties and their identifiers and removes them again.
use axum::http::StatusCode;
use serde_json::{Value, json};

use crate::test_support::*;

const PARTIES: &str = §PARTYROUTE;
const IDENTIFIERS: &str = §IDROUTE;
const TAKEN: &str = "§T_TAKEN";

fn tag() -> String {
    uuid::Uuid::new_v4().simple().to_string().chars().take(8).collect::<String>().to_uppercase()
}

async fn party(api: &Client) -> Value {
    let made = api.post(PARTIES, &json!(§PARTYBODY)).await;
    assert_eq!(made.status, StatusCode::CREATED, "{}", made.body);
    made.json()["§PARTYKEY"].clone()
}

async fn identifier(api: &Client, owner: &Value, scheme: &str, value: &str) -> Reply {
    let mut body = json!(§IDBODY);
    body["§LINK"] = owner.clone();
    body["§SCHEME"] = json!(scheme);
    body["§VALUE"] = json!(value);
    api.post(IDENTIFIERS, &body).await
}

async fn remove(api: &Client, identifiers: &[&Reply], parties: &[&Value]) {
    for made in identifiers {
        if made.status == StatusCode::CREATED {
            api.delete(&format!("{IDENTIFIERS}/{}", made.json()["§IDKEY"])).await;
        }
    }
    for key in parties {
        api.delete(&format!("{PARTIES}/{key}")).await;
    }
}

#[tokio::test]
async fn a_scheme_and_value_pair_another_row_holds_is_refused() {
    let Some(api) = Client::start().await else { return };
    let tag = tag();
    let held = format!("NL{tag}");
    let (acme, bolt) = (party(&api).await, party(&api).await);
    let first = identifier(&api, &acme, "§FIRST", &format!("  {held}  ")).await;
    let same = identifier(&api, &bolt, "§FIRST", &held).await;
    let case = identifier(&api, &bolt, "§FIRSTLOW", &held.to_lowercase()).await;
    let other = identifier(&api, &bolt, "§OTHER", &held).await;
    let own = identifier(&api, &bolt, "§FIRST", &format!("DE{tag}")).await;
    let mut row = own.json();
    let route = format!("{IDENTIFIERS}/{}", row["§IDKEY"]);
    let unchanged = api.put(&route, &row).await;
    row["§VALUE"] = json!(held.to_lowercase());
    let clash = api.put(&route, &row).await;
    remove(&api, &[&first, &same, &case, &other, &own], &[&acme, &bolt]).await;
    assert_eq!(first.status, StatusCode::CREATED, "{}", first.body);
    assert!(first.json()["§VALUE"] == held && first.json()["§SCHEME"] == "§FIRST", "both are trimmed: {}", first.body);
    assert!(same.status == StatusCode::CONFLICT && same.body.contains(TAKEN), "{}", same.body);
    assert_eq!(case.status, StatusCode::CONFLICT, "case does not make a new pair: {}", case.body);
    assert_eq!(other.status, StatusCode::CREATED, "the same value under another scheme is another pair: {}", other.body);
    assert_eq!(own.status, StatusCode::CREATED, "{}", own.body);
    assert!(unchanged.status.is_success(), "a row keeps its own pair: {}", unchanged.body);
    assert!(clash.status == StatusCode::CONFLICT && clash.body.contains(TAKEN), "{}", clash.body);
}

#[tokio::test]
async fn a_party_is_found_by_its_identifier() {
    let Some(api) = Client::start().await else { return };
    let tag = tag();
    let held = format!("NL{tag}");
    let (acme, bolt) = (party(&api).await, party(&api).await);
    let first = identifier(&api, &acme, "§FIRST", &held).await;
    let other = identifier(&api, &bolt, "§OTHER", &held).await;
    let both = api.get(&format!("{PARTIES}/by-identifier?value={held}")).await;
    let one = api.get(&format!("{PARTIES}/by-identifier?value={held}&scheme=§FIRST")).await;
    let loose = api.get(&format!("{PARTIES}/by-identifier?value=%20{}%20&scheme=§FIRSTLOW", held.to_lowercase())).await;
    let none = api.get(&format!("{PARTIES}/by-identifier?value=XX{tag}")).await;
    let blank = api.get(&format!("{PARTIES}/by-identifier")).await;
    let spaces = api.get(&format!("{PARTIES}/by-identifier?value=%20")).await;
    remove(&api, &[&first, &other], &[&acme, &bolt]).await;
    assert!(first.status == StatusCode::CREATED && other.status == StatusCode::CREATED, "{} {}", first.body, other.body);
    let found = keys_of(&both.json(), "§PARTYKEY");
    assert!(found.len() == 2 && found.contains(&acme) && found.contains(&bolt), "{}", both.body);
    assert_eq!(keys_of(&one.json(), "§PARTYKEY"), vec![acme.clone()], "{}", one.body);
    assert_eq!(keys_of(&loose.json(), "§PARTYKEY"), vec![acme.clone()], "the search does not mind case or spaces: {}", loose.body);
    assert!(none.status == StatusCode::OK && keys_of(&none.json(), "§PARTYKEY").is_empty(), "{}", none.body);
    assert_eq!(blank.status, StatusCode::BAD_REQUEST, "{}", blank.body);
    assert_eq!(spaces.status, StatusCode::BAD_REQUEST, "{}", spaces.body);
}
"""";
}
