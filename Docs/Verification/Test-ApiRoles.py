"""Live checks of ReadRoles, WriteRoles and TableWriteRoles on a generated Python or Rust API (ApiAuth, UserTable=Employee, UserRoleColumn=IsManager, UserAdminColumn=IsAdministrator).
Generate the API from a SQLite file with the tables Employee (FullName, IsManager, IsAdministrator, IsActive, Login, PwdHash), TimeSheet (EmployeeId, Notes) and TimeSheetLine (TimeSheetId, Hours)
and the rows jdoe (id 1, administrator), akim (id 2, neither) and mgr (id 4, a manager), with WriteRoles=Manager, TableWriteRoles=TimeSheet:Administrator and TableReadRoles=TimeSheetLine:Manager.
Point DATABASE_FILE at a scratch copy, set the three passwords with the set-password command, start the API with Jwt__Key, then run this against it.
usage: ADMIN_PASSWORD=... EMPLOYEE_PASSWORD=... MANAGER_PASSWORD=... python Test-ApiRoles.py <port>
The passwords come from the environment: they are never written in a file."""
import json
import os
import sys
import urllib.error
import urllib.request

PASSWORDS = {'jdoe': os.environ['ADMIN_PASSWORD'], 'akim': os.environ['EMPLOYEE_PASSWORD'], 'mgr': os.environ['MANAGER_PASSWORD']}
BASE = 'http://127.0.0.1:' + sys.argv[1]
results = []


def call(method, path, body=None, token=None):
    data = json.dumps(body).encode() if body is not None else None
    req = urllib.request.Request(BASE + path, data=data, method=method)
    req.add_header('Content-Type', 'application/json')
    if token:
        req.add_header('Authorization', 'Bearer ' + token)
    try:
        with urllib.request.urlopen(req) as r:
            text = r.read().decode()
            return r.status, (json.loads(text) if text else None)
    except urllib.error.HTTPError as e:
        text = e.read().decode()
        try:
            return e.code, json.loads(text)
        except Exception:
            return e.code, text


def check(name, ok, detail=''):
    results.append(ok)
    print(('PASS ' if ok else 'FAIL ') + name + ('' if ok else '  ' + str(detail)[:300]))


def login(user):
    status, body = call('POST', '/api/auth/login', {'userName': user, 'password': PASSWORDS[user]})
    assert status == 200, f'sign-in of {user} answered {status}'
    return body['token']


def refused(response):
    status, body = response
    return status == 403 and isinstance(body, dict) and body.get('type') == '/problems/role-not-allowed'


def items(body):
    return body['items'] if isinstance(body, dict) and 'items' in body else body


admin, akim, mgr = login('jdoe'), login('akim'), login('mgr')

check('no token is still 401', call('GET', '/api/timesheets')[0] == 401)

# TimeSheet: anyone reads, only the administrator writes
check('an employee reads TimeSheet', call('GET', '/api/timesheets', token=akim)[0] == 200)
check('an employee reads one TimeSheet', call('GET', '/api/timesheets/1', token=akim)[0] == 200)
check('an employee cannot create a TimeSheet', refused(call('POST', '/api/timesheets', {'employeeId': 2, 'notes': 'x'}, akim)))
check('a manager cannot create a TimeSheet (the rule lists only the administrator)', refused(call('POST', '/api/timesheets', {'employeeId': 2, 'notes': 'x'}, mgr)))
check('an employee cannot update a TimeSheet', refused(call('PUT', '/api/timesheets/1', {'timeSheetId': 1, 'employeeId': 1, 'notes': 'x'}, akim)))
check('an employee cannot delete a TimeSheet', refused(call('DELETE', '/api/timesheets/1', token=akim)))
status, made = call('POST', '/api/timesheets', {'employeeId': 2, 'notes': 'role test'}, admin)
check('the administrator creates a TimeSheet', status == 201, (status, made))
sheet_id = made['timeSheetId'] if isinstance(made, dict) else None
check('the administrator deletes it', sheet_id is None or call('DELETE', f'/api/timesheets/{sheet_id}', token=admin)[0] in (200, 204))

# TimeSheetLine: only a manager (and the administrator) reads, WriteRoles=Manager writes
check('an employee cannot read TimeSheetLine', refused(call('GET', '/api/timesheetlines', token=akim)))
check('an employee cannot read one TimeSheetLine', refused(call('GET', '/api/timesheetlines/1', token=akim)))
check('an employee cannot search TimeSheetLine', refused(call('GET', '/api/timesheetlines/search?pageNumber=1&pageSize=5', token=akim)) or call('GET', '/api/timesheetlines/search?pageNumber=1&pageSize=5', token=akim)[0] == 404)
check('a manager reads TimeSheetLine', call('GET', '/api/timesheetlines', token=mgr)[0] == 200)
check('the administrator reads TimeSheetLine', call('GET', '/api/timesheetlines', token=admin)[0] == 200)
check('an employee cannot create a TimeSheetLine', refused(call('POST', '/api/timesheetlines', {'timeSheetId': 1, 'hours': 3}, akim)))
status, line = call('POST', '/api/timesheetlines', {'timeSheetId': 1, 'hours': 3}, mgr)
check('a manager creates a TimeSheetLine', status == 201, (status, line))
line_id = line['timeSheetLineId'] if isinstance(line, dict) else None
check('a manager deletes it', line_id is None or call('DELETE', f'/api/timesheetlines/{line_id}', token=mgr)[0] in (200, 204))

# Employee (the user table): no rule beyond WriteRoles
check('an employee reads the Employee lookup', call('GET', '/api/employees/lookup', token=akim)[0] == 200)
check('an employee cannot create an Employee', refused(call('POST', '/api/employees', {'fullName': 'X', 'isManager': False, 'isAdministrator': False, 'isActive': True, 'login': 'zz'}, akim)))

print(f'\n{sum(results)} passed, {len(results) - sum(results)} failed')
sys.exit(0 if all(results) else 1)
