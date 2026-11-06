"""Plays the session and token tables against a running API (ASP.NET, Python or Rust) over a scratch copy of Schemas/tokens.sql.

python token_check.py <base url> <sqlite file>

The API is a generated one with ApiAuth, UserTable=Employee, UserNameColumn=Login, UserRoleColumn=IsManager, UserAdminColumn=IsAdministrator and
TokenTables=Session,PasswordReset:CodeHash:ExpiresOn:UsedOn,EmailCode:CodeHash:ExpiresOn:IsUsed,RefreshToken and TokenAnonymousSpend=RefreshToken. It replaces the Employee rows of the copy with an administrator and an ordinary person and writes
token rows (the SHA-256 of a token in hex) directly. It checks: a call without a sign-in is a 401; an ordinary person is refused the list and the revoke (403) but may spend; a token is spent once
(the second try, an expired one, an unknown one and a blank body are the same 400) and the row is marked in the database; the list never shows the hash and says which rows are active; revoking
twice is not an error and an absent row is a 404; revoking a person ends every active row of that person and the list can be limited to a person; a yes/no revoked column and a date and
time one both work; a table with a used and a revoked column tests both; a hash stored in capitals is found; the spend of a table in TokenAnonymousSpend needs no sign-in; and a token that is used a second time ends its family."""
import base64
import hashlib
import sqlite3
import sys
from datetime import datetime, timedelta

import httpx

base = sys.argv[1]
db = sys.argv[2]
client = httpx.Client(base_url=base, timeout=30)
failures = []


def check(label, ok, detail=""):
    print(("ok   " if ok else "FAIL ") + label + ("" if ok else "  " + str(detail)[:300]))
    if not ok:
        failures.append(label)


def hash_password(password):
    salt = b"0123456789abcdef"
    digest = hashlib.pbkdf2_hmac("sha256", password.encode(), salt, 600_000, 32)
    return f"pbkdf2-sha256$600000${base64.b64encode(salt).decode()}${base64.b64encode(digest).decode()}"


def token_hash(token):
    return hashlib.sha256(token.encode()).hexdigest()


def stamp(delta):
    return (datetime.now() + delta).strftime("%Y-%m-%d %H:%M:%S")


def login(name, password):
    response = client.post("/api/auth/login", json={"userName": name, "password": password})
    return {"Authorization": "Bearer " + response.json()["token"]} if response.status_code == 200 else {}


connection = sqlite3.connect(db)
for table in ("Session", "PasswordReset", "EmailCode", "RefreshToken", "Employee"):
    connection.execute(f"delete from {table}")
connection.execute("insert into Employee (EmployeeId, FullName, Login, PasswordHash, IsManager, IsAdministrator) values (1, 'Admin Person', 'admin', ?, 1, 1)", (hash_password("admin-pass-1"),))
connection.execute("insert into Employee (EmployeeId, FullName, Login, PasswordHash, IsManager, IsAdministrator) values (2, 'Ordinary Person', 'ordinary', ?, 0, 0)", (hash_password("ordinary-pass-1"),))
connection.commit()
admin = login("admin", "admin-pass-1")
ordinary = login("ordinary", "ordinary-pass-1")
check("both people sign in", bool(admin) and bool(ordinary))


def session(token, hours=1, person=1):
    connection.execute("insert into Session (EmployeeId, TokenHash, CreatedOn, ExpiresOn) values (?, ?, ?, ?)", (person, token_hash(token), stamp(timedelta()), stamp(timedelta(hours=hours))))
    connection.commit()
    return connection.execute("select max(SessionId) from Session").fetchone()[0]


def revoked_of(table, key, id):
    return connection.execute(f"select {key} from {table} where {table}Id = ?", (id,)).fetchone()[0]


check("a list without a sign-in is 401", client.get("/api/sessions").status_code == 401)
check("a spend without a sign-in is 401", client.post("/api/sessions/spend", json={"token": "x"}).status_code == 401)
check("a revoke without a sign-in is 401", client.post("/api/sessions/1/revoke").status_code == 401)
check("an ordinary person may not list", client.get("/api/sessions", headers=ordinary).status_code == 403)
check("an ordinary person may not revoke", client.post("/api/sessions/1/revoke", headers=ordinary).status_code == 403)
check("an ordinary person may not sign a person out", client.post("/api/sessions/revoke-person/1", headers=ordinary).status_code == 403)

first = session("first-token")
spent = client.post("/api/sessions/spend", headers=ordinary, json={"token": "first-token"})
check("a token is spent by any signed-in caller", spent.status_code == 200 and spent.json().get("id") == first and spent.json().get("personId") == 1, spent.text)
check("the row is marked in the database", revoked_of("Session", "RevokedOn", first) is not None)
again = client.post("/api/sessions/spend", headers=admin, json={"token": "first-token"})
check("a second spend is 400 with the problem type", again.status_code == 400 and again.json().get("type", "").endswith("token-not-valid"), again.text)

expired = session("expired-token", hours=-1)
check("an expired token is 400", client.post("/api/sessions/spend", headers=admin, json={"token": "expired-token"}).status_code == 400)
check("an unknown token is 400", client.post("/api/sessions/spend", headers=admin, json={"token": "nobody-has-this"}).status_code == 400)
check("a blank token is 400", client.post("/api/sessions/spend", headers=admin, json={"token": ""}).status_code == 400)
check("a body without a token is 400", client.post("/api/sessions/spend", headers=admin, json={}).status_code == 400)
check("the expired row was not marked", revoked_of("Session", "RevokedOn", expired) is None)

one, two, other = session("one"), session("two"), session("other", person=2)
listed = client.get("/api/sessions", headers=admin)
rows = listed.json() if listed.status_code == 200 else []
check("the administrator lists the rows", listed.status_code == 200 and {r.get("sessionId") for r in rows} >= {first, one, two, other}, listed.text)
check("the list never shows the hash", all("tokenHash" not in r for r in rows), rows[:1])
by_id = {r["sessionId"]: r for r in rows}
check("active says which rows are live", by_id[one]["active"] is True and by_id[first]["active"] is False and by_id[expired]["active"] is False, [by_id.get(i) for i in (one, first, expired)])
mine = client.get("/api/sessions?personId=2", headers=admin).json()
check("the list can be limited to a person", [r["sessionId"] for r in mine] == [other], mine)

revoked = client.post(f"/api/sessions/{one}/revoke", headers=admin)
check("an administrator revokes a row", revoked.status_code == 200 and revoked.json().get("active") is False, revoked.text)
check("the row is marked", revoked_of("Session", "RevokedOn", one) is not None)
check("revoking twice is not an error", client.post(f"/api/sessions/{one}/revoke", headers=admin).status_code == 200)
check("a revoked token cannot be spent", client.post("/api/sessions/spend", headers=admin, json={"token": "one"}).status_code == 400)
check("an absent row is 404", client.post("/api/sessions/2000000000/revoke", headers=admin).status_code == 404)

signed_out = client.post("/api/sessions/revoke-person/1", headers=admin)
check("signing a person out ends their active rows", signed_out.status_code == 200 and signed_out.json().get("revoked") == 1, signed_out.text)
check("that person's token cannot be spent", client.post("/api/sessions/spend", headers=admin, json={"token": "two"}).status_code == 400)
check("another person's row is untouched", revoked_of("Session", "RevokedOn", other) is None)
check("signing the same person out again ends nothing", client.post("/api/sessions/revoke-person/1", headers=admin).json().get("revoked") == 0)

connection.execute("insert into PasswordReset (Email, CodeHash, ExpiresOn) values ('a@b.test', ?, ?)", (token_hash("reset-code"), stamp(timedelta(hours=1))))
connection.execute("insert into EmailCode (Email, CodeHash, ExpiresOn, IsUsed) values ('a@b.test', ?, ?, 0)", (token_hash("mail-code"), stamp(timedelta(hours=1))))
connection.commit()
reset = client.post("/api/passwordresets/spend", headers=admin, json={"token": "reset-code"})
check("a reset code with a date column is spent", reset.status_code == 200 and "personId" not in reset.json(), reset.text)
check("its used stamp is set", connection.execute("select UsedOn from PasswordReset").fetchone()[0] is not None)
check("and not twice", client.post("/api/passwordresets/spend", headers=admin, json={"token": "reset-code"}).status_code == 400)
mail = client.post("/api/emailcodes/spend", headers=admin, json={"token": "mail-code"})
check("a code with a yes/no column is spent", mail.status_code == 200, mail.text)
check("its flag is set", connection.execute("select IsUsed from EmailCode").fetchone()[0] in (1, True))
check("and not twice", client.post("/api/emailcodes/spend", headers=admin, json={"token": "mail-code"}).status_code == 400)
listing = client.get("/api/emailcodes", headers=admin).json()
check("that list has no hash and the row is not active", len(listing) == 1 and "codeHash" not in listing[0] and listing[0]["active"] is False, listing)
check("a table without a person has no sign-out route", client.post("/api/emailcodes/revoke-person/1", headers=admin).status_code in (404, 405))

def refresh(token, family, upper=False, hours=1):
    digest = token_hash(token).upper() if upper else token_hash(token)
    connection.execute("insert into RefreshToken (EmployeeId, TokenHash, FamilyId, ExpiresOn) values (1, ?, ?, ?)", (digest, family, stamp(timedelta(hours=hours))))
    connection.commit()
    return connection.execute("select max(RefreshTokenId) from RefreshToken").fetchone()[0]


def cell(column, id):
    return connection.execute(f"select {column} from RefreshToken where RefreshTokenId = ?", (id,)).fetchone()[0]


a1, a2 = refresh("fam-a-1", "family-a"), refresh("fam-a-2", "family-a")
b1 = refresh("fam-b-1", "family-b", upper=True)
check("the list of an anonymous-spend table still needs the administrator", client.get("/api/refreshtokens").status_code == 401)
check("its revoke still needs a sign-in", client.post(f"/api/refreshtokens/{a1}/revoke").status_code == 401)
spent = client.post("/api/refreshtokens/spend", json={"token": "fam-a-1"})
check("a table in TokenAnonymousSpend is spent without a sign-in", spent.status_code == 200 and spent.json().get("id") == a1 and spent.json().get("family") == "family-a", spent.text)
check("a spend sets the used column and not the revoked one", cell("UsedOn", a1) is not None and cell("RevokedOn", a1) is None)
check("a wrong token is the same 400 for a caller without a sign-in", client.post("/api/refreshtokens/spend", json={"token": "nobody"}).status_code == 400)
listing = client.get("/api/refreshtokens", headers=admin).json()
by_id = {r["refreshTokenId"]: r for r in listing}
check("a row with only the used column set is not active", by_id[a1]["active"] is False and by_id[a2]["active"] is True, [by_id.get(a1), by_id.get(a2)])
check("the sibling is still live before the reuse", cell("UsedOn", a2) is None and cell("RevokedOn", a2) is None)
reused = client.post("/api/refreshtokens/spend", json={"token": "fam-a-1"})
check("a used token that comes again is 400", reused.status_code == 400, reused.text)
check("and ends the rest of its family", cell("RevokedOn", a2) is not None)
check("the family's sibling can no longer be spent", client.post("/api/refreshtokens/spend", json={"token": "fam-a-2"}).status_code == 400)
check("another family is untouched", cell("UsedOn", b1) is None and cell("RevokedOn", b1) is None)
upper = client.post("/api/refreshtokens/spend", json={"token": "fam-b-1"})
check("a hash stored in capitals is found", upper.status_code == 200 and upper.json().get("family") == "family-b", upper.text)
dead = refresh("fam-c-1", "family-c", hours=-1)
c2 = refresh("fam-c-2", "family-c")
check("an expired token is refused and is not a reuse", client.post("/api/refreshtokens/spend", json={"token": "fam-c-1"}).status_code == 400 and cell("RevokedOn", c2) is None)

print("failures:", len(failures))
sys.exit(1 if failures else 0)
