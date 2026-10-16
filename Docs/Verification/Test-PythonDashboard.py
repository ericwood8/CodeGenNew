"""Live check of the read-role filter of a generated Python dashboard (PY_Dashboard, ApiAuth, UserTable=Employee, TableReadRoles naming a table that has widgets).
Generate the API from the SQLite file used for Test-ApiRoles.py (tables Employee, TimeSheet, TimeSheetLine; people jdoe administrator, mgr manager, akim neither) with TableReadRoles=TimeSheet:Manager,
point DATABASE_FILE at a scratch copy, set the three passwords with `setup`, start the API with Jwt__Key, then run `check`.
usage: python Test-PythonDashboard.py setup <app folder> <db file>      (run with the API's own python)
       python Test-PythonDashboard.py check <port>
The passwords come from ADMIN_PASSWORD, MANAGER_PASSWORD and EMPLOYEE_PASSWORD in the environment: they are never written in a file."""
import json
import os
import sqlite3
import sys
import urllib.error
import urllib.request

if sys.argv[1] == 'setup':
    sys.path.insert(0, sys.argv[2])
    from app.auth import hash_password
    con = sqlite3.connect(sys.argv[3])
    for login, env in (('jdoe', 'ADMIN_PASSWORD'), ('mgr', 'MANAGER_PASSWORD'), ('akim', 'EMPLOYEE_PASSWORD')):
        con.execute('UPDATE Employee SET PwdHash = ? WHERE Login = ?', (hash_password(os.environ[env]), login))
    con.commit()
    print('passwords set')
    sys.exit(0)

BASE = 'http://127.0.0.1:' + sys.argv[2]
PASSWORDS = {'jdoe': os.environ['ADMIN_PASSWORD'], 'mgr': os.environ['MANAGER_PASSWORD'], 'akim': os.environ['EMPLOYEE_PASSWORD']}


def call(path, token=None, body=None):
    req = urllib.request.Request(BASE + path, data=json.dumps(body).encode() if body else None, method='POST' if body else 'GET')
    req.add_header('Content-Type', 'application/json')
    if token:
        req.add_header('Authorization', 'Bearer ' + token)
    try:
        with urllib.request.urlopen(req) as r:
            return r.status, json.loads(r.read().decode() or 'null')
    except urllib.error.HTTPError as e:
        return e.code, None


results = []


def check(name, ok, detail=''):
    results.append(ok)
    print(('PASS ' if ok else 'FAIL ') + name + ('' if ok else '  ' + str(detail)[:300]))


status, _ = call('/api/dashboard')
check('no token is a 401', status == 401, status)
tokens = {}
for user in PASSWORDS:
    status, body = call('/api/auth/login', body={'userName': user, 'password': PASSWORDS[user]})
    check(f'{user} signs in', status == 200, status)
    tokens[user] = body['token']

tables = {}
for user in PASSWORDS:
    status, body = call('/api/dashboard', tokens[user])
    check(f'{user} gets the dashboard', status == 200, status)
    tables[user] = sorted({w['table'] for w in body['widgets']})
    print('   ', user, tables[user])
check('the administrator sees the TimeSheet widgets', 'TimeSheet' in tables['jdoe'])
check('the manager sees the TimeSheet widgets', 'TimeSheet' in tables['mgr'])
check('the employee does not see them', 'TimeSheet' not in tables['akim'])
check('the employee still sees the other tables', 'Employee' in tables['akim'], tables['akim'])
check('the manager and the administrator see the same tables', tables['jdoe'] == tables['mgr'])

# the answer is cached and shared: the employee's call after the administrator's must still be filtered, and the administrator's after the employee's must still be whole
for user in ('jdoe', 'akim', 'jdoe', 'akim'):
    status, body = call('/api/dashboard', tokens[user])
    seen = 'TimeSheet' in {w['table'] for w in body['widgets']}
    check(f'{user} on a cached answer: TimeSheet widgets {"present" if user == "jdoe" else "absent"}', seen == (user == 'jdoe'))

status, body = call('/api/dashboard?table=TimeSheet', tokens['akim'])
check('the strip of a table the employee may not read is empty', status == 200 and body['widgets'] == [], body)
print(f'{sum(results)} of {len(results)} checks passed')
sys.exit(0 if all(results) else 1)
