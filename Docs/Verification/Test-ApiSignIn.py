"""Live checks of a generated Python or Rust API with ApiAuth, UserTable=Employee and OwnedTables=TimeSheet (the child TimeSheetLine is scoped through its parent).
Generate the API from a SQLite file that has these tables (Employee with FullName, IsManager, IsAdministrator, IsActive, Login, PwdHash; TimeSheet with EmployeeId; TimeSheetLine with TimeSheetId) and
the rows boss (id 1, manager and administrator), worker (id 2) and nopass (id 3, no password), a time sheet for each of boss and worker and a line under each; point DATABASE_FILE at a scratch copy;
set the two passwords with the set-password command; start the API with Jwt__Key; then run this against it.
usage: BOSS_PASSWORD=... WORKER_PASSWORD=... python Test-ApiSignIn.py <port> <the same sqlite file>
The passwords come from the environment: they are never written in a file."""
import json
import os
import sqlite3
import sys
import urllib.error
import urllib.request

BOSS_PASSWORD = os.environ['BOSS_PASSWORD']
WORKER_PASSWORD = os.environ['WORKER_PASSWORD']
PORT = sys.argv[1]
DB = sys.argv[2]
BASE = 'http://127.0.0.1:' + PORT
results = []


def call(method, path, body=None, token=None, raw=False):
    data = json.dumps(body).encode() if body is not None else None
    req = urllib.request.Request(BASE + path, data=data, method=method)
    req.add_header('Content-Type', 'application/json')
    if token:
        req.add_header('Authorization', 'Bearer ' + token)
    try:
        with urllib.request.urlopen(req) as r:
            text = r.read().decode()
            return r.status, (json.loads(text) if text and not raw else text)
    except urllib.error.HTTPError as e:
        text = e.read().decode()
        try:
            return e.code, json.loads(text)
        except Exception:
            return e.code, text


def sql(q):
    con = sqlite3.connect(DB)
    try:
        return con.execute(q).fetchall()
    finally:
        con.close()


def check(name, ok, detail=''):
    results.append(ok)
    print(('PASS ' if ok else 'FAIL ') + name + ('' if ok else '  ' + str(detail)[:300]))


def login(user, pw):
    status, body = call('POST', '/api/auth/login', {'userName': user, 'password': pw})
    return status, body


def items(body):
    return body['items'] if isinstance(body, dict) and 'items' in body else body


# ---- sign-in
s, body = login('boss', BOSS_PASSWORD)
check('boss signs in', s == 200 and 'token' in body, (s, body))
boss = body['token']
check('the answer names the user, the role and the owner', body['user'] == {'name': 'boss', 'role': 'Administrator', 'ownerId': 1} and body['expiresUtc'].endswith('Z'), body)
s, body = login('worker', WORKER_PASSWORD)
check('worker signs in', s == 200, (s, body))
worker_token = body['token']
check('worker is an employee with owner 2', body['user'] == {'name': 'worker', 'role': 'Employee', 'ownerId': 2}, body)
s, body = login('worker', 'wrong password')
check('a wrong password is 401', s == 401 and isinstance(body, dict) and body.get('type') == '/problems/wrong-credentials', (s, body))
s, body = login('nobody', 'whatever1')
check('an unknown user is 401', s == 401, (s, body))
s, body = login('nopass', 'whatever1')
check('a user with no password is 401', s == 401, (s, body))
s, body = call('POST', '/api/auth/login', {'userName': '', 'password': ''})
check('a blank body is 400', s == 400 and isinstance(body, dict) and body.get('type') == '/problems/sign-in-incomplete', (s, body))

# ---- every other route needs a token
for path in ('/api/employees', '/api/timesheets', '/api/timesheetlines', '/api/employees/search'):
    s, _ = call('GET', path)
    check('no token: 401 on ' + path, s == 401, s)
s, _ = call('GET', '/api/employees', token='not.a.token')
check('a bad token is 401', s == 401, s)
tampered = boss[:-3] + ('AAA' if not boss.endswith('AAA') else 'BBB')
s, _ = call('GET', '/api/employees', token=tampered)
check('a token with a changed signature is 401', s == 401, s)

# ---- the password hash
hash_before = {r[0]: r[1] for r in sql("SELECT Login, PwdHash FROM Employee")}
s, rows = call('GET', '/api/employees', token=worker_token)
check('the employee list answers', s == 200, s)
check('no hash in the list', 'pwdhash' not in json.dumps(rows).lower() and 'pbkdf2' not in json.dumps(rows), json.dumps(rows)[:200])
s, page = call('GET', '/api/employees/search', token=worker_token)
check('no hash in the search', s == 200 and 'pbkdf2' not in json.dumps(page), s)
worker = next(r for r in items(rows) if r['login'] == 'worker')
s, one = call('GET', '/api/employees/%d' % worker['employeeId'], token=worker_token)
check('no hash in one row', s == 200 and 'pbkdf2' not in json.dumps(one) and 'pwdHash' not in one, one)

body = dict(one, pwdHash='pbkdf2-sha256$600000$evil$evil')
s, r = call('PUT', '/api/employees/2', body, worker_token)
check('worker saves own row, sending a hash', s == 200, (s, r))
check('the stored hash is unchanged', sql("SELECT PwdHash FROM Employee WHERE Login = 'worker'")[0][0] == hash_before['worker'])
check('worker still signs in', login('worker', WORKER_PASSWORD)[0] == 200)

# ---- the administrator flag and the role
body = dict(one, isAdministrator=True)
s, r = call('PUT', '/api/employees/2', body, worker_token)
check('worker cannot make himself administrator: 403', s == 403 and r.get('type') == '/problems/column-not-yours', (s, r))
body = dict(one, isManager=True)
s, r = call('PUT', '/api/employees/2', body, worker_token)
check('worker cannot make himself manager: 403', s == 403, (s, r))
check('flags unchanged', sql("SELECT IsAdministrator, IsManager FROM Employee WHERE Login = 'worker'") == [(0, 0)])
body = dict(one, isManager=True)
s, r = call('PUT', '/api/employees/2', body, boss)
check('boss may make worker a manager', s == 200 and sql("SELECT IsManager FROM Employee WHERE Login = 'worker'") == [(1,)], (s, r))
body['isManager'] = False
s, r = call('PUT', '/api/employees/2', body, boss)
check('and take it away', s == 200 and sql("SELECT IsManager FROM Employee WHERE Login = 'worker'") == [(0,)], (s, r))

# ---- create
new = dict(one, employeeId=0, fullName='zed test', login='zed', isAdministrator=True, isManager=True, pwdHash='pbkdf2-sha256$600000$evil$evil')
s, r = call('POST', '/api/employees', new, worker_token)
check('worker creates a person: 201', s == 201, (s, r))
check('no flags, no hash', sql("SELECT IsAdministrator, IsManager, PwdHash FROM Employee WHERE Login = 'zed'") == [(0, 0, None)])
s, r = call('POST', '/api/employees', dict(new, fullName='zed two'), worker_token)
check('same sign-in name: 409', s == 409 and r.get('type') == '/problems/duplicate-value', (s, r))
s, r = call('POST', '/api/employees', dict(new, fullName='zed two', login=' zed '), worker_token)
check('same name with spaces: 409', s == 409, (s, r))
s, r = call('POST', '/api/employees', dict(new, fullName='ann test', login='ann'), boss)
ann = r.get('employeeId') if isinstance(r, dict) else None
check('boss creates an administrator', s == 201 and sql("SELECT IsAdministrator FROM Employee WHERE Login = 'ann'") == [(1,)], (s, r))

# ---- the last administrator
s, r = call('DELETE', '/api/employees/%d' % ann, token=boss)
check('a second administrator can be deleted', s == 200, (s, r))
s, jrow = call('GET', '/api/employees/1', token=boss)
s, r = call('PUT', '/api/employees/1', dict(jrow, isAdministrator=False), boss)
check('the last administrator cannot lose the flag: 422', s == 422 and r.get('type') == '/problems/last-administrator', (s, r))
s, r = call('PUT', '/api/employees/1', dict(jrow, isActive=False), boss)
check('nor be deactivated: 422', s == 422, (s, r))
s, r = call('DELETE', '/api/employees/1', token=boss)
check('nor be deleted: 422', s == 422, (s, r))
check('boss is intact', sql("SELECT IsAdministrator, IsActive FROM Employee WHERE EmployeeId = 1") == [(1, 1)])
s, r = call('PUT', '/api/employees/1', jrow, boss)
check('boss saves own row, hash kept', s == 200 and sql("SELECT PwdHash FROM Employee WHERE EmployeeId = 1")[0][0] == hash_before['boss'] and login('boss', BOSS_PASSWORD)[0] == 200, (s, r))

# ---- owned tables
s, sheets = call('GET', '/api/timesheets', token=worker_token)
check('worker sees only his time sheet', s == 200 and [x['timeSheetId'] for x in items(sheets)] == [2], sheets)
s, sheets = call('GET', '/api/timesheets', token=boss)
check('the administrator sees both', s == 200 and sorted(x['timeSheetId'] for x in items(sheets)) == [1, 2], sheets)
s, r = call('GET', '/api/timesheets/2', token=worker_token)
check('worker reads his own', s == 200, (s, r))
s, r = call('GET', '/api/timesheets/1', token=worker_token)
check('worker cannot read the other: 403', s == 403 and r.get('type') == '/problems/not-your-row', (s, r))
s, r = call('PUT', '/api/timesheets/1', {'timeSheetId': 1, 'employeeId': 1, 'notes': 'mine now'}, worker_token)
check('worker cannot change the other: 403', s == 403, (s, r))
s, r = call('PUT', '/api/timesheets/2', {'timeSheetId': 2, 'employeeId': 1, 'notes': 'give away'}, worker_token)
check('worker cannot hand his row to someone else: 403', s == 403, (s, r))
s, r = call('DELETE', '/api/timesheets/1', token=worker_token)
check('worker cannot delete the other: 403', s == 403, (s, r))
s, r = call('POST', '/api/timesheets', {'timeSheetId': 0, 'employeeId': 0, 'notes': 'blank owner'}, worker_token)
check('a blank owner becomes worker', s == 201 and r['employeeId'] == 2, (s, r))
mine = r.get('timeSheetId')
s, r = call('POST', '/api/timesheets', {'timeSheetId': 0, 'employeeId': 1, 'notes': 'for the boss'}, worker_token)
check('worker cannot create one for the boss: 403', s == 403, (s, r))
s, r = call('POST', '/api/timesheets', {'timeSheetId': 0, 'employeeId': 2, 'notes': 'for worker by admin'}, boss)
check('the administrator may create one for worker', s == 201 and r['employeeId'] == 2, (s, r))
s, page = call('GET', '/api/timesheets/search', token=worker_token)
if s == 200:
    check('the search is scoped too', all(x['employeeId'] == 2 for x in items(page)) and len(items(page)) >= 2, page)
s, r = call('DELETE', '/api/timesheets/%d' % mine, token=worker_token)
check('worker deletes his own', s == 200, (s, r))

# ---- a child through its parent
s, lines = call('GET', '/api/timesheetlines', token=worker_token)
check('worker sees only the line under his sheet', s == 200 and [x['timeSheetLineId'] for x in items(lines)] == [2], lines)
s, r = call('GET', '/api/timesheetlines/1', token=worker_token)
check('and not the other line: 403', s == 403, (s, r))
s, r = call('POST', '/api/timesheetlines', {'timeSheetLineId': 0, 'timeSheetId': 1, 'hours': 1}, worker_token)
check('worker cannot add a line under the boss\'s sheet: 403', s == 403, (s, r))
s, r = call('POST', '/api/timesheetlines', {'timeSheetLineId': 0, 'timeSheetId': 2, 'hours': 1}, worker_token)
check('worker adds a line under his sheet', s == 201, (s, r))
s, lines = call('GET', '/api/timesheetlines', token=boss)
check('the administrator sees every line', s == 200 and len(items(lines)) >= 3, lines)

# ---- the throttle (last: it blocks the user for a minute)
for _ in range(6):
    login('throttled', 'bad password')
s, body = login('throttled', 'bad password')
check('after five wrong tries: 429', s == 429 and isinstance(body, dict) and body.get('type') == '/problems/too-many-attempts', (s, body))

print('%d of %d passed' % (sum(results), len(results)))
sys.exit(0 if all(results) else 1)
