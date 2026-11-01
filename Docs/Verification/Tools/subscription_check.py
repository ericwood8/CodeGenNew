"""Plays plans, subscriptions and plan limits against a running multi-tenant API over a scratch copy of Schemas/subscriptions.sql.

python subscription_check.py <base url> <sqlite file>

The API is a generated one (ASP.NET, Python or Rust) with ApiAuth, TenantTable=Tenant, TenantMembership=EmployeeTenant:EmployeeId:TenantId:Role, UserTable=Employee, UserNameColumn=Login, UserRoleColumn=IsManager,
UserAdminColumn=IsAdministrator, SubscriptionTable=Subscription, PlanTable=Plan, PlanLimitTable=PlanLimit and PlanLimits=Project:projects,EmployeeTenant:seats. The script writes test password hashes
into the copy's Employee rows and changes its plan rows; never point it at a database that matters. It signs in as ada (administrator of tenant 1, on Pro and active), grace (administrator of tenant 2, on
Free and trialing) and pat (a platform administrator) and checks what the administrator sees, a create past a limit, a member added past the seats, a paused subscription, a trial that ended, and who may
change a subscription. With PlanTable=Plan, SubscriptionWebhookSecret=GATEWAY_WEBHOOK_SECRET and the same secret in the environment of this script, it also plays the signed webhook.

The view is checked for the price and billing period of the plan and for the list of plans (Plan.MonthlyPrice and Plan.BillingPeriod of the schema)."""
import base64
import hashlib
import hmac
import json
import os
import sqlite3
import sys
import time

import httpx

base, db = sys.argv[1], sys.argv[2]


def hash_password(password):
    salt = b"0123456789abcdef"
    digest = hashlib.pbkdf2_hmac("sha256", password.encode(), salt, 600_000, 32)
    return f"pbkdf2-sha256$600000${base64.b64encode(salt).decode()}${base64.b64encode(digest).decode()}"


connection = sqlite3.connect(db)
for login in ("ada", "grace", "pat"):
    connection.execute("UPDATE Employee SET PasswordHash = ? WHERE Login = ?", (hash_password("secret-pass"), login))
connection.commit()

client = httpx.Client(base_url=base, timeout=60)
failures = []


def check(label, condition, detail=""):
    print(("ok   " if condition else "FAIL ") + label + ("" if condition else "  " + str(detail)[:400]))
    if not condition:
        failures.append(label)


def sign_in(name, tenant=None):
    body = {"userName": name, "password": "secret-pass"}
    if tenant is not None:
        body["tenantId"] = tenant
    reply = client.post("/api/auth/login", json=body)
    return {"Authorization": "Bearer " + reply.json()["token"]}


def sql(text, *args):
    connection.execute(text, args)
    connection.commit()


def limit_of(info, key):
    return next(l for l in info["limits"] if l["key"] == key)


ada, grace, pat = sign_in("ada", 1), sign_in("grace", 2), sign_in("pat")

info = client.get("/api/subscription", headers=ada).json()
check("an administrator sees the plan and the status of their tenant", info["planName"] == "Pro" and info["status"] == "active" and info["writable"], info)
check("Pro allows ten projects and the tenant has two", limit_of(info, "projects")["allowed"] == 10 and limit_of(info, "projects")["used"] == 2, info)
check("Pro has no seat limit (NULL is unlimited)", limit_of(info, "seats")["allowed"] is None and limit_of(info, "seats")["used"] == 1, info)
check("the view shows the price and the billing period of the plan", info["planPrice"] == 29 and info["planPeriod"] == "month", info)
listed = info["plans"]
check("the view lists every plan, cheapest first, and marks the tenant's own", [p["name"] for p in listed] == ["Free", "Pro", "Enterprise"] and [p["current"] for p in listed] == [False, True, False], listed)
check("each plan lists what it allows (NULL and no row are unlimited)", [l["allowed"] for l in listed[0]["limits"]] == [2, 1] and [l["allowed"] for l in listed[1]["limits"]] == [10, None]
      and [l["allowed"] for l in listed[2]["limits"]] == [None, None] and listed[1]["price"] == 29 and listed[2]["period"] == "month", listed)
check("a platform administrator has no tenant to show", client.get("/api/subscription", headers=pat).status_code == 403)
check("a caller with no sign-in is refused", client.get("/api/subscription").status_code == 401)

info = client.get("/api/subscription", headers=grace).json()
check("the second tenant is on Free, trialing, with room for one more project", info["planName"] == "Free" and info["status"] == "trialing" and limit_of(info, "projects")["used"] == 1, info)

project = {"projectId": 0, "tenantId": 0, "name": "Scratch", "budget": 1}
first = client.post("/api/projects", headers=grace, json=project)
check("a create below the limit works", first.status_code == 201, (first.status_code, first.text))
second = client.post("/api/projects", headers=grace, json={**project, "name": "Scratch two"})
check("a create at the limit is a 402 plan-limit", second.status_code == 402 and second.json()["type"].endswith("plan-limit"), (second.status_code, second.text))
check("the sentence says what the plan allows and what the tenant has", "allows 2" in second.json()["detail"] and "already has 2" in second.json()["detail"], second.text)
check("a clone past the limit is refused the same way", client.post("/api/projects/1/clone", headers=grace).status_code in (402, 404))
check("reads are never limited", client.get("/api/projects", headers=grace).status_code == 200)
check("a member added past the seats of the plan is a 402", client.post("/api/auth/members", headers=grace, json={"personId": 3, "role": "Employee"}).status_code == 402)
rows = connection.execute("SELECT COUNT(*) FROM EmployeeTenant WHERE TenantId = 2 AND EmployeeId = 3").fetchone()[0]
check("and nobody was added", rows == 0, rows)

sql("UPDATE PlanLimit SET LimitValue = 5 WHERE PlanId = 1 AND LimitKey = 'projects'")
third = client.post("/api/projects", headers=grace, json={**project, "name": "Scratch three"})
check("the limit is read at the create, so a raised limit works at once", third.status_code == 201, (third.status_code, third.text))
sql("DELETE FROM PlanLimit WHERE PlanId = 1 AND LimitKey = 'projects'")
fourth = client.post("/api/projects", headers=grace, json={**project, "name": "Scratch four"})
check("a plan with no row for a limit is unlimited", fourth.status_code == 201, (fourth.status_code, fourth.text))

check("a tenant's administrator cannot change their own subscription", client.put("/api/subscription/2", headers=grace, json={"planId": 2, "status": "active"}).status_code == 403)
changed = client.put("/api/subscription/2", headers=pat, json={"status": "cancelled"})
check("a platform administrator cancels it", changed.status_code == 200 and changed.json()["status"] == "cancelled" and not changed.json()["writable"], (changed.status_code, changed.text))
paused = client.post("/api/projects", headers=grace, json={**project, "name": "While cancelled"})
check("writes are paused (402 subscription-inactive)", paused.status_code == 402 and paused.json()["type"].endswith("subscription-inactive"), (paused.status_code, paused.text))
check("so is a delete", client.delete("/api/projects/1", headers=grace).status_code == 402)
check("reads go on", client.get("/api/projects", headers=grace).status_code == 200)
check("the administrator still sees the subscription", client.get("/api/subscription", headers=grace).json()["status"] == "cancelled")
check("sign-in and the tenant switch are never paused", client.get("/api/auth/tenants", headers=grace).status_code == 200)
check("ada, in a tenant that is active, is not affected", client.post("/api/projects", headers=ada, json={**project, "name": "Ada's"}).status_code == 201)

back = client.put("/api/subscription/2", headers=pat, json={"planId": 2, "status": "active"})
check("moved to Pro and active, the tenant writes again at once", back.status_code == 200 and client.post("/api/projects", headers=grace, json={**project, "name": "Back"}).status_code == 201, (back.status_code, back.text))

ended = client.put("/api/subscription/2", headers=pat, json={"status": "trialing", "periodEnd": "2026-01-01T00:00:00"})
check("a trial past its period end pauses writes", ended.status_code == 200 and client.post("/api/projects", headers=grace, json={**project, "name": "Late"}).status_code == 402, (ended.status_code, ended.text))
running = client.put("/api/subscription/2", headers=pat, json={"periodEnd": "2099-01-01T00:00:00"})
check("and a later period end starts it again", running.status_code == 200 and client.post("/api/projects", headers=grace, json={**project, "name": "On time"}).status_code == 201, (running.status_code, running.text))

check("an unknown tenant is 404", client.put("/api/subscription/999", headers=pat, json={"planId": 1, "status": "active"}).status_code == 404)
check("an unknown plan is 400", client.put("/api/subscription/2", headers=pat, json={"planId": 999}).status_code == 400)
sql("DELETE FROM Subscription WHERE TenantId = 3")
check("a tenant with no subscription needs a plan and a status", client.put("/api/subscription/3", headers=pat, json={"status": "active"}).status_code == 400)
made = client.put("/api/subscription/3", headers=pat, json={"planId": 1, "status": "active", "gatewayId": "sub_new"})
row = connection.execute("SELECT PlanId, Status, GatewaySubscriptionId, CurrentPeriodStart IS NOT NULL FROM Subscription WHERE TenantId = 3").fetchall()
check("a platform administrator makes the subscription, stamped with its period start", made.status_code == 200 and row == [(1, "active", "sub_new", 1)], (made.status_code, row))

secret = os.environ.get("GATEWAY_WEBHOOK_SECRET", "")
if secret:
    def hook(message, signed_at=None, secret=secret, header_value=None):
        body = json.dumps(message).encode()
        at = int(time.time()) if signed_at is None else signed_at
        digest = hmac.new(secret.encode(), f"{at}.".encode() + body, hashlib.sha256).hexdigest()
        return client.post("/api/subscription/webhook", content=body, headers={"Content-Type": "application/json", "X-Gateway-Signature": header_value or f"t={at},v1={digest}"})

    check("a webhook with no signature is refused (401)", client.post("/api/subscription/webhook", json={"gatewayId": "sub_contoso", "status": "cancelled"}).status_code == 401)
    check("a webhook signed with another secret is refused (401)", hook({"gatewayId": "sub_contoso", "status": "cancelled"}, secret="not-the-secret").status_code == 401)
    old = hook({"gatewayId": "sub_contoso", "status": "cancelled"}, signed_at=int(time.time()) - 3600)
    check("a signed message that is an hour old is a replay (400 subscription-webhook-expired)", old.status_code == 400 and old.json()["type"].endswith("subscription-webhook-expired"), (old.status_code, old.text))
    check("and nothing was changed by the refused ones", client.get("/api/subscription", headers=grace).json()["status"] == "trialing")
    applied = hook({"id": "evt_1", "gatewayId": "sub_contoso", "status": "past_due"})
    check("a signed webhook sets the status of the subscription the gateway names", applied.status_code == 200 and applied.json()["applied"] is True, (applied.status_code, applied.text))
    check("the tenant is paused at once", client.get("/api/subscription", headers=grace).json()["status"] == "past_due" and client.post("/api/projects", headers=grace, json={**project, "name": "Past due"}).status_code == 402)
    again = hook({"id": "evt_1", "gatewayId": "sub_contoso", "status": "active"})
    check("the same event id is applied once", again.status_code == 200 and again.json()["applied"] is False and client.get("/api/subscription", headers=grace).json()["status"] == "past_due", (again.status_code, again.text))
    resumed = hook({"id": "evt_2", "gatewayId": "sub_contoso", "status": "active", "periodEnd": "2099-01-01T00:00:00Z"})
    check("a later event resumes it, with a new period end", resumed.status_code == 200 and client.post("/api/projects", headers=grace, json={**project, "name": "Resumed"}).status_code == 201, (resumed.status_code, resumed.text))
    check("an unknown gateway id is 404", hook({"gatewayId": "sub_nobody", "status": "active"}).status_code == 404)
    check("a message with no status is 400", hook({"gatewayId": "sub_contoso"}).status_code == 400)
else:
    print("(set GATEWAY_WEBHOOK_SECRET to the secret the API was started with to play the webhook)")

print("\nFAILED: " + ", ".join(failures) if failures else "\nall passed")
sys.exit(1 if failures else 0)
