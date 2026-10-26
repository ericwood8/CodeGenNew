namespace CodeGenNew.Core;

/// <summary> <c>tests/api/approval_routes.rs</c> of the Rust API (RS_TestApprovals): seeds the people and a row (<see cref="ApprovalTestSeed"/>) in the scratch database and plays the cycle through the router, the same
/// cases and words as the Python, ASP.NET and WinUI3 tests (<see cref="ApprovalWords"/>). The hooks have no test here: the crate keeps its routes module private. </summary>
public static class RustApprovalTests
{
    /// <summary> Whether the tests are written: <c>RustTests</c>, a plan without problems and a schema the seed can be made from. </summary>
    public static bool Applies(DatabaseModel database, ProjectSettings project)
    {
        if (!project.RustTests || !project.ApiAuth || project.RowApprovals.Length == 0)
            return false;
        var plan = ApprovalPlan.Resolve(database, project);
        return plan.Problems.Count == 0 && plan.Tables.Count > 0 && ApprovalTestSeed.For(plan, database, project, out _) is not null;
    }

    public static string File(DatabaseModel database, ProjectSettings project)
    {
        var plan = ApprovalPlan.Resolve(database, project);
        var seed = ApprovalTestSeed.For(plan, database, project, out _)!;
        string Str(string text) => "\"" + text.Replace("\\", "\\\\").Replace("\"", "\\\"") + "\"";
        string List(IReadOnlyList<string> statements) => string.Join("\n", statements.Select(s => "    " + RustTable.Raw(s) + ","));
        string text = Text.Replace("§OTHERTABLE", ApprovalTestSeed.HasOtherTablePerson(plan) ? OtherTableCase : "");
        foreach (var (token, value) in new (string, string)[]
        {
            ("§SETUP", List(seed.Setup)), ("§CLEANUP", List(seed.Cleanup)), ("§ROUTE", Str(RustTable.Of(seed.Table.Table, project).Route)), ("§ROW", seed.RowKey.ToString()),
            ("§ANN", ApprovalTestSeed.Requester), ("§BOB", ApprovalTestSeed.Approver), ("§CY", ApprovalTestSeed.AnyTableApprover), ("§DEE", ApprovalTestSeed.OtherTableApprover),
            ("§T_UNKNOWN", ApprovalWriter.RequesterUnknownType), ("§T_NOTAPPROVER", ApprovalWriter.NotApproverType), ("§T_FOUREYES", ApprovalWriter.FourEyesType),
            ("§W_NOTAPPROVER", Str(ApprovalWords.NotApprover)), ("§W_NOTREQUESTER", Str(ApprovalWords.NotRequester)), ("§W_FOUREYES", Str(ApprovalWords.FourEyes)), ("§W_ALREADY", Str(ApprovalWords.AlreadyWaiting))
        }.OrderByDescending(v => v.Item1.Length))
            text = text.Replace(token, value);
        return text + "\n";
    }

    private const string Text = """"
// The row approvals through the router: who may ask and decide, the four eyes, withdrawal and the history. Written by RS_TestApprovals; regenerated with the tables.
// The people and the row are added to the scratch database before each test and removed after it; the hooks are not tested (the crate keeps its routes module private).
use axum::http::StatusCode;
use serde_json::{Value, json};

use crate::test_support::*;

const ROUTE: &str = §ROUTE;
const ROW: i64 = §ROW;

const SETUP: &[&str] = &[
§SETUP
];

const CLEANUP: &[&str] = &[
§CLEANUP
];

async fn seeded() -> Option<Client> {
    let api = Client::start_signed_out().await?;
    let pool = api.pool();
    for &sql in CLEANUP {
        let _ = sqlx::query(sql).execute(&pool).await;
    }
    for &sql in SETUP {
        sqlx::query(sql).execute(&pool).await.unwrap_or_else(|error| panic!("{sql}: {error}"));
    }
    Some(api)
}

async fn clear(api: &Client) {
    let pool = api.pool();
    for &sql in CLEANUP {
        let _ = sqlx::query(sql).execute(&pool).await;
    }
}

fn waiting_rows(reply: &Reply) -> Vec<i64> {
    reply.json().as_array().map(|rows| rows.iter().filter_map(|row| row["rowId"].as_i64()).collect()).unwrap_or_default()
}

fn problem_type(reply: &Reply, ending: &str) -> bool {
    reply.json()["type"].as_str().is_some_and(|text| text.ends_with(ending))
}

#[tokio::test]
async fn a_person_who_is_not_in_the_user_table_cannot_ask() {
    let Some(api) = seeded().await else { return };
    let reply = api.as_person("zz_stranger").post(&format!("{ROUTE}/{ROW}/request"), &json!({})).await;
    clear(&api).await;
    assert_eq!(reply.status, StatusCode::FORBIDDEN, "{}", reply.body);
    assert!(problem_type(&reply, "§T_UNKNOWN"), "{}", reply.body);
}

#[tokio::test]
async fn a_row_that_does_not_exist_is_not_found() {
    let Some(api) = seeded().await else { return };
    let reply = api.as_person("§ANN").post(&format!("{ROUTE}/{}/request", ROW + 500000), &json!({})).await;
    clear(&api).await;
    assert_eq!(reply.status, StatusCode::NOT_FOUND, "{}", reply.body);
}

#[tokio::test]
async fn a_request_waits_for_a_decision_and_a_second_one_is_refused() {
    let Some(api) = seeded().await else { return };
    let ann = api.as_person("§ANN");
    let first = ann.post(&format!("{ROUTE}/{ROW}/request"), &json!({ "reason": "please" })).await;
    let again = ann.post(&format!("{ROUTE}/{ROW}/request"), &json!({})).await;
    let history = ann.get(&format!("{ROUTE}/{ROW}/approvals")).await;
    clear(&api).await;
    assert_eq!(first.status, StatusCode::OK, "{}", first.body);
    assert!(first.json()["status"] == "Pending" && first.json()["reason"] == "please" && first.json()["revision"] == 1, "{}", first.body);
    assert_eq!(again.status, StatusCode::CONFLICT, "{}", again.body);
    assert_eq!(again.json()["detail"], §W_ALREADY);
    assert_eq!(history.json().as_array().map(Vec::len), Some(1));
}

#[tokio::test]
async fn only_an_approver_decides_and_never_on_their_own_request() {
    let Some(api) = seeded().await else { return };
    let (ann, bob) = (api.as_person("§ANN"), api.as_person("§BOB"));
    ann.post(&format!("{ROUTE}/{ROW}/request"), &json!({})).await;
    let refused = ann.post(&format!("{ROUTE}/{ROW}/approve"), &json!({})).await;
    let ann_waiting = ann.get(&format!("{ROUTE}/toapprove")).await;

    let waiting = bob.get(&format!("{ROUTE}/toapprove")).await;
    let not_requester = bob.post_empty(&format!("{ROUTE}/{ROW}/withdraw")).await;
    let withdrawn = ann.post_empty(&format!("{ROUTE}/{ROW}/withdraw")).await;

    bob.post(&format!("{ROUTE}/{ROW}/request"), &json!({})).await;
    let four_eyes = bob.post(&format!("{ROUTE}/{ROW}/approve"), &json!({})).await;
    let own = bob.get(&format!("{ROUTE}/toapprove")).await;
    clear(&api).await;

    assert_eq!(refused.status, StatusCode::FORBIDDEN, "{}", refused.body);
    assert!(problem_type(&refused, "§T_NOTAPPROVER") && refused.json()["detail"] == §W_NOTAPPROVER, "{}", refused.body);
    assert_eq!(ann_waiting.status, StatusCode::FORBIDDEN, "{}", ann_waiting.body);
    assert_eq!(waiting.status, StatusCode::OK, "{}", waiting.body);
    assert!(waiting_rows(&waiting).contains(&ROW), "{}", waiting.body);
    assert_eq!(not_requester.status, StatusCode::FORBIDDEN, "{}", not_requester.body);
    assert_eq!(not_requester.json()["detail"], §W_NOTREQUESTER);
    assert_eq!(withdrawn.json()["status"], "Withdrawn", "{}", withdrawn.body);
    assert_eq!(four_eyes.status, StatusCode::FORBIDDEN, "{}", four_eyes.body);
    assert!(problem_type(&four_eyes, "§T_FOUREYES") && four_eyes.json()["detail"] == §W_FOUREYES, "{}", four_eyes.body);
    assert!(!waiting_rows(&own).contains(&ROW), "{}", own.body);
}

#[tokio::test]
async fn a_decision_is_written_once_and_the_history_shows_every_cycle() {
    let Some(api) = seeded().await else { return };
    let (ann, cy) = (api.as_person("§ANN"), api.as_person("§CY"));
    ann.post(&format!("{ROUTE}/{ROW}/request"), &json!({})).await;
    ann.post_empty(&format!("{ROUTE}/{ROW}/withdraw")).await;
    ann.post(&format!("{ROUTE}/{ROW}/request"), &json!({})).await;

    let decided = cy.post(&format!("{ROUTE}/{ROW}/approve"), &json!({ "note": "fine" })).await;
    let again = cy.post(&format!("{ROUTE}/{ROW}/reject"), &json!({})).await;
    let history = ann.get(&format!("{ROUTE}/{ROW}/approvals")).await;
    clear(&api).await;

    assert_eq!(decided.status, StatusCode::OK, "{}", decided.body);
    assert!(decided.json()["status"] == "Approved" && decided.json()["decidedBy"] == "§CY" && decided.json()["decisionNote"] == "fine", "{}", decided.body);
    assert_eq!(again.status, StatusCode::CONFLICT, "{}", again.body);
    let statuses: Vec<Value> = history.json().as_array().cloned().unwrap_or_default().iter().map(|cycle| cycle["status"].clone()).collect();
    assert_eq!(statuses, [json!("Approved"), json!("Withdrawn")]);
}
§OTHERTABLE
"""";

    private const string OtherTableCase = """"

#[tokio::test]
async fn a_row_for_another_table_does_not_let_a_person_decide_this_one() {
    let Some(api) = seeded().await else { return };
    api.as_person("§CY").post(&format!("{ROUTE}/{ROW}/request"), &json!({})).await;
    let refused = api.as_person("§DEE").post(&format!("{ROUTE}/{ROW}/approve"), &json!({})).await;
    let waiting = api.as_person("§DEE").get(&format!("{ROUTE}/toapprove")).await;
    clear(&api).await;
    assert_eq!(refused.status, StatusCode::FORBIDDEN, "{}", refused.body);
    assert_eq!(refused.json()["detail"], §W_NOTAPPROVER);
    assert_eq!(waiting.status, StatusCode::FORBIDDEN, "{}", waiting.body);
}
"""";
}
