"""Checks the change gate and the guarded column of a generated ASP.NET API over HTTP with real sign-ins (ChangeGates and GuardedColumns, item API_Gates).

Usage:  python Test-GatedChanges.py <port> [--prove-failure]

The case is the InvoiceSystem sample's: anyone may create a SalesInvoice and it starts as Quote (InvoiceStatusId 1); nobody moves it to 2 to 6 while the customer is in Bad Standing
(CustomerStatusId 2) unless the customer's IgnoreBadStatus is set; only a manager (or an administrator) may set that column, and the server stamps who did it and when.
Run it against an API generated with ApiAuth, UserTable=Employee, the two rules, over a SCRATCH copy of the database: it changes customers and invoices there.
Needs three people in Employee.Login: an employee who is not a manager, a manager who is not an administrator, and an administrator who is a manager.
Environment: EMPLOYEE_USER / EMPLOYEE_PASSWORD, MANAGER_USER / MANAGER_PASSWORD, ADMIN_USER / ADMIN_PASSWORD (passwords only from the environment), SQLSERVER_NAME and SQLSERVER_DATABASE
(default ERICSMINIPC and InvoiceSystem_scratch; read with Windows authentication). With --prove-failure the script expects the API to be generated WITHOUT the rules and reports
the checks that fail then, which shows they can.
"""
import json
import os
import subprocess
import sys
import urllib.error
import urllib.request

PORT = sys.argv[1]
BASE = f'http://localhost:{PORT}'
SERVER = os.environ.get('SQLSERVER_NAME', 'ERICSMINIPC')
DATABASE = os.environ.get('SQLSERVER_DATABASE', 'InvoiceSystem_scratch')
PEOPLE = {
    'employee': (os.environ['EMPLOYEE_USER'], os.environ['EMPLOYEE_PASSWORD']),
    'manager': (os.environ['MANAGER_USER'], os.environ['MANAGER_PASSWORD']),
    'admin': (os.environ['ADMIN_USER'], os.environ['ADMIN_PASSWORD']),
}

passed = failed = 0


def check(name, ok, detail=''):
    global passed, failed
    if ok:
        passed += 1
    else:
        failed += 1
    print(('PASS ' if ok else 'FAIL ') + name + ('' if ok else '   ' + str(detail)))


def sql(text):
    result = subprocess.run(['sqlcmd', '-S', SERVER, '-d', DATABASE, '-E', '-C', '-b', '-h', '-1', '-W', '-s', '|', '-Q', 'SET NOCOUNT ON; ' + text], capture_output=True, text=True)
    if result.returncode:
        raise RuntimeError(result.stdout + result.stderr)
    return [line for line in result.stdout.splitlines() if line.strip()]


def call(method, path, token=None, body=None):
    data = None if body is None else json.dumps(body).encode()
    request = urllib.request.Request(BASE + path, data=data, method=method)
    request.add_header('Content-Type', 'application/json')
    if token:
        request.add_header('Authorization', 'Bearer ' + token)
    try:
        with urllib.request.urlopen(request) as response:
            raw = response.read().decode()
            return response.status, (json.loads(raw) if raw.strip().startswith(('{', '[')) else raw), response.headers
    except urllib.error.HTTPError as error:
        raw = error.read().decode()
        try:
            return error.code, json.loads(raw), error.headers
        except ValueError:
            return error.code, raw, error.headers


def login(kind):
    user, password = PEOPLE[kind]
    status, body, _ = call('POST', '/api/auth/login', body={'userName': user, 'password': password})
    assert status == 200, f'{kind} cannot sign in: {status} {body}'
    return body['token']


def problem_type(body):
    return body.get('type') if isinstance(body, dict) else None


employee, manager, admin = login('employee'), login('manager'), login('admin')

# a Bad Standing customer that is not exempt, and a Good Standing one, each with an invoice to copy
bad = sql("SELECT TOP 1 c.CustomerId FROM Customer c WHERE c.CustomerStatusId = 2 AND c.IgnoreBadStatus = 0 AND EXISTS (SELECT 1 FROM SalesInvoice s WHERE s.CustomerId = c.CustomerId) ORDER BY c.CustomerId")[0]
good = sql("SELECT TOP 1 c.CustomerId FROM Customer c WHERE c.CustomerStatusId = 1 AND EXISTS (SELECT 1 FROM SalesInvoice s WHERE s.CustomerId = c.CustomerId) ORDER BY c.CustomerId")[0]
sql(f"UPDATE Customer SET IgnoreBadStatus = 0, IgnoreBadStatusBy = NULL, IgnoreBadStatusOn = NULL WHERE CustomerId = {bad}")
sql(f"UPDATE Customer SET CustomerStatusId = 1 WHERE CustomerId = {good}")


def copy_of_invoice(customer, status):
    template_id = sql(f"SELECT TOP 1 SalesInvoiceId FROM SalesInvoice WHERE CustomerId = {customer} ORDER BY SalesInvoiceId")[0]
    _, row, _ = call('GET', f'/api/salesinvoices/{template_id}', employee)
    row.pop('salesInvoiceId', None)
    for navigation in ('customer', 'customerMonthlySummary', 'invoiceStatus'):
        row.pop(navigation, None)
    row['salesInvoiceNumber'] = None
    row['invoiceStatusId'] = status
    return row


def make_invoice(customer):
    status, body, headers = call('POST', '/api/salesinvoices', employee, copy_of_invoice(customer, 1))
    assert status in (200, 201), f'cannot make a test invoice: {status} {body}'
    return body['salesInvoiceId'] if isinstance(body, dict) and 'salesInvoiceId' in body else int(headers['Location'].rsplit('/', 1)[1])


def invoice(invoice_id):
    return call('GET', f'/api/salesinvoices/{invoice_id}', employee)[1]


def set_status(invoice_id, status, token, **changes):
    row = invoice(invoice_id)
    for navigation in ('customer', 'customerMonthlySummary', 'invoiceStatus'):
        row.pop(navigation, None)
    row['invoiceStatusId'] = status
    row.update(changes)
    return call('PUT', f'/api/salesinvoices/{invoice_id}', token, row)


def customer(customer_id):
    row = call('GET', f'/api/customers/{customer_id}', employee)[1]
    for navigation in ('customerStatus',):
        row.pop(navigation, None)
    return row


def status_in_database(invoice_id):
    return int(sql(f"SELECT InvoiceStatusId FROM SalesInvoice WHERE SalesInvoiceId = {invoice_id}")[0])


# ----- who may change the guarded column
check('the API wants a sign-in for the guards route', call('GET', '/api/customers/guards')[0] == 401)
check('an employee who is not a manager may not change the guarded column', call('GET', '/api/customers/guards', employee)[1] == {'ignoreBadStatus': False})
check('a manager may', call('GET', '/api/customers/guards', manager)[1] == {'ignoreBadStatus': True})
check('an administrator may', call('GET', '/api/customers/guards', admin)[1] == {'ignoreBadStatus': True})

# ----- a new invoice
bad_invoice = make_invoice(bad)
check('an invoice for a Bad Standing customer is created as Quote', status_in_database(bad_invoice) == 1)
status, body, _ = call('POST', '/api/salesinvoices', employee, copy_of_invoice(bad, 2))
check('a create that asks for another status is a 400', status == 400, f'{status} {body}')
good_invoice = make_invoice(good)

# ----- the gate
status, body, _ = set_status(bad_invoice, 2, employee)
check('the move to Sales order is refused for a Bad Standing customer (409 change-not-allowed)', status == 409 and problem_type(body) == '/problems/change-not-allowed', f'{status} {body}')
check('the refusal says why in words', status == 409 and 'Customer Status Id = 2' in body.get('detail', '') and 'Ignore Bad Status' in body.get('detail', ''), body)
check('and the status in the database did not change', status_in_database(bad_invoice) == 1)
status, body, _ = set_status(bad_invoice, 6, employee)
check('every move to 2 to 6 is gated (to Shipped)', status == 409, f'{status} {body}')
status, body, _ = set_status(bad_invoice, 1, employee, notes='edited while on hold')
check('another edit of the same invoice goes ahead (the notes, the status unchanged)', status in (200, 204), f'{status} {body}')
status, body, _ = set_status(good_invoice, 2, employee)
check('a Good Standing customer\'s invoice moves to Sales order', status in (200, 204) and status_in_database(good_invoice) == 2, f'{status} {body}')
status, body, _ = set_status(good_invoice, 3, employee)
check('and on to Invoiced', status in (200, 204) and status_in_database(good_invoice) == 3, f'{status} {body}')
check('reads are never gated', call('GET', f'/api/salesinvoices/{bad_invoice}', employee)[0] == 200)

# ----- the guard
row = customer(bad)
status, body, _ = call('PUT', f'/api/customers/{bad}', employee, {**row, 'billingAddress1': 'MOVED BY AN EMPLOYEE'})
check('an employee edits the customer\'s address (the guarded value unchanged)', status in (200, 204), f'{status} {body}')
check('and the address reached the database', sql(f"SELECT BillingAddress1 FROM Customer WHERE CustomerId = {bad}")[0] == 'MOVED BY AN EMPLOYEE')
status, body, _ = call('PUT', f'/api/customers/{bad}', employee, {**customer(bad), 'ignoreBadStatus': True})
check('an employee sets IgnoreBadStatus: 403 column-not-yours', status == 403 and problem_type(body) == '/problems/column-not-yours', f'{status} {body}')
check('and it stayed off', sql(f"SELECT IgnoreBadStatus FROM Customer WHERE CustomerId = {bad}")[0] == '0')
status, body, _ = call('PUT', f'/api/customers/{bad}', employee, {**customer(bad), 'ignoreBadStatusBy': 'mallory', 'ignoreBadStatusOn': '2020-01-01T00:00:00'})
by = sql(f"SELECT ISNULL(IgnoreBadStatusBy, 'NULL') FROM Customer WHERE CustomerId = {bad}")[0]
check('a stamp sent in a body is ignored (the stored stamp stays)', status in (200, 204) and by == 'NULL', f'{status} {by}')

status, body, _ = set_status(bad_invoice, 2, manager)
check('a manager is blocked like anyone else until the flag is set', status == 409, f'{status} {body}')
status, body, _ = call('PUT', f'/api/customers/{bad}', manager, {**customer(bad), 'ignoreBadStatus': True})
check('a manager sets IgnoreBadStatus', status in (200, 204), f'{status} {body}')
stamp = sql(f"SELECT CONVERT(varchar(10), IgnoreBadStatus) + '|' + ISNULL(IgnoreBadStatusBy, 'NULL') + '|' + CASE WHEN IgnoreBadStatusOn IS NULL THEN 'NULL' ELSE 'set' END FROM Customer WHERE CustomerId = {bad}")[0]
check('the server stamped who and when', stamp == f'1|{PEOPLE["manager"][0]}|set', stamp)
status, body, _ = set_status(bad_invoice, 2, employee)
check('with the flag set an employee\'s move to Sales order goes through', status in (200, 204) and status_in_database(bad_invoice) == 2, f'{status} {body}')
status, body, _ = call('PUT', f'/api/customers/{bad}', employee, {**customer(bad), 'billingAddress2': 'ANOTHER EDIT'})
stamp_after = sql(f"SELECT ISNULL(IgnoreBadStatusBy, 'NULL') FROM Customer WHERE CustomerId = {bad}")[0]
check('a later edit by an employee keeps the stamp', status in (200, 204) and stamp_after == PEOPLE['manager'][0], f'{status} {stamp_after}')

status, body, _ = call('PUT', f'/api/customers/{bad}', admin, {**customer(bad), 'ignoreBadStatus': False})
cleared = sql(f"SELECT CONVERT(varchar(10), IgnoreBadStatus) + '|' + ISNULL(IgnoreBadStatusBy, 'NULL') FROM Customer WHERE CustomerId = {bad}")[0]
check('an administrator clears it (and the stamp says who)', status in (200, 204) and cleared == f'0|{PEOPLE["admin"][0]}', f'{status} {cleared}')
status, body, _ = set_status(bad_invoice, 3, employee)
check('the next move (2 to 3) is refused again', status == 409 and status_in_database(bad_invoice) == 2, f'{status} {body}')
status, body, _ = set_status(bad_invoice, 2, employee, notes='saved again at the same status')
check('saving at the status it already has is not a move', status in (200, 204), f'{status} {body}')

# ----- a customer who goes into Bad Standing after the invoice was saved
late = make_invoice(good)
status, body, _ = call('PUT', f'/api/customers/{good}', manager, {**customer(good), 'customerStatusId': 2})
check('a manager puts a customer into Bad Standing', status in (200, 204), f'{status} {body}')
status, body, _ = set_status(late, 2, employee)
check('an invoice saved earlier is caught at the move, not at the create', status == 409 and status_in_database(late) == 1, f'{status} {body}')
call('PUT', f'/api/customers/{good}', manager, {**customer(good), 'customerStatusId': 1})

# ----- the other ways a row is made: a clone and a CSV import
status, body, _ = call('POST', f'/api/salesinvoices/{good_invoice}/clone', employee)
copy_id = body.get('salesInvoiceId') if isinstance(body, dict) else None
check('a clone of an invoice that was already Invoiced starts as Quote', status in (200, 201) and copy_id is not None and status_in_database(copy_id) == 1, f'{status} {body}')

call('PUT', f'/api/customers/{bad}', manager, {**customer(bad), 'ignoreBadStatus': True})
status, body, _ = call('POST', f'/api/customers/{bad}/clone', employee)
copy_customer = body.get('customerId') if isinstance(body, dict) else None
row = sql(f"SELECT CONVERT(varchar(10), IgnoreBadStatus) + '|' + ISNULL(IgnoreBadStatusBy, 'NULL') FROM Customer WHERE CustomerId = {copy_customer}")[0] if copy_customer else ''
check('the clone an employee makes of an exempt customer does not carry the exemption over', status in (200, 201) and row == '0|NULL', f'{status} {row}')
status, body, _ = call('POST', f'/api/customers/{bad}/clone', manager)
copy_customer = body.get('customerId') if isinstance(body, dict) else None
row = sql(f"SELECT CONVERT(varchar(10), IgnoreBadStatus) + '|' + ISNULL(IgnoreBadStatusBy, 'NULL') FROM Customer WHERE CustomerId = {copy_customer}")[0] if copy_customer else ''
check('the clone a manager makes keeps it, stamped with the manager', status in (200, 201) and row == f'1|{PEOPLE["manager"][0]}', f'{status} {row}')

csv_status = call('POST', '/api/salesinvoices/import', employee, None)[0]
check('the CSV import into a gated table is for the administrator only', csv_status == 403, csv_status)

print(f'{passed} of {passed + failed} passed')
sys.exit(1 if failed else 0)
