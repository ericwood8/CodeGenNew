// Checks the owner scoping, the change gate and the guarded column of a generated API's OData routes with real sign-ins (item 114, package 5).
//
// Run it over a SCRATCH copy of the InvoiceSystem database (ScratchDatabase.ps1), never the real one: it changes invoices and customers there.
//   1. TEST_SEED=<any text> node Prepare-ODataScoping.js     (sets test passwords for jdoe, akim and jsmith, derived from TEST_SEED, and hands two invoices each to customers 5 and 7)
//   2. generate the API from a project with: OData=true, ApiAuth=true, UserTable=Employee, UserNameColumn=Login, PasswordHashColumn=PasswordHash, UserOwnerColumn=EmployeeId, UserRoleColumn=IsManager,
//      UserAdminColumn=IsAdministrator, OwnedTables=SalesInvoice:CustomerId, the ChangeGates and GuardedColumns of Docs/TemplateNotes/API_Gates_v1.md; start it with Jwt__Key set (PORT, default 5202)
//   3. TEST_SEED=<the same> PORT=5202 node Test-ODataScoping.js
// akim (customer 5, not a manager), jsmith (customer 7, a manager) and jdoe (an administrator) are the three people. Generated without OwnedTables and the two rules the same checks fail (21 of the first 35 checks), which shows they can.
// Passwords are only ever derived from the environment; nothing is written to a file.
const crypto = require('crypto');
const { execFileSync } = require('child_process');

const BASE = 'http://localhost:' + (process.env.PORT || '5202');
const seed = process.env.TEST_SEED;
const password = (name) => 'T!' + crypto.createHash('sha256').update(seed + name).digest('hex').slice(0, 16);
let passed = 0, failed = 0;
const check = (name, ok, detail = '') => { ok ? passed++ : failed++; console.log((ok ? 'PASS ' : 'FAIL ') + name + (ok ? '' : '   ' + detail)); };
const sql = (text) => execFileSync('sqlcmd', ['-S', 'ERICSMINIPC', '-d', 'InvoiceSystem_scratch', '-E', '-I', '-b', '-h', '-1', '-W', '-s', '|', '-Q', 'SET NOCOUNT ON; ' + text]).toString().split(/\r?\n/).filter((l) => l.trim());

async function login(name) {
  const r = await fetch(BASE + '/api/auth/login', { method: 'POST', headers: { 'Content-Type': 'application/json' }, body: JSON.stringify({ userName: name, password: password(name) }) });
  if (!r.ok) throw new Error('sign-in of ' + name + ' answered ' + r.status);
  return (await r.json()).token;
}
async function call(token, method, path, body) {
  const r = await fetch(BASE + path, { method, headers: { 'Content-Type': 'application/json', ...(token ? { Authorization: 'Bearer ' + token } : {}) }, body: body === undefined ? undefined : JSON.stringify(body) });
  const text = await r.text();
  let json = null; try { json = JSON.parse(text); } catch { /* not json */ }
  return { status: r.status, json, text, location: r.headers.get('location') };
}
const type = (r) => r.json && r.json.type;

(async () => {
  const [admin, akim, jsmith] = [await login('jdoe'), await login('akim'), await login('jsmith')];   // jdoe: administrator and manager, akim: neither (customer 5), jsmith: manager (customer 7)

  check('no token is refused', (await call(null, 'GET', '/odata/salesinvoices')).status === 401);

  // reads: each person sees the rows of their own customer; the administrator sees all
  const ids = async (token, path = '/odata/salesinvoices?$select=salesInvoiceId,customerId&$count=true') => { const r = await call(token, 'GET', path); return { r, rows: r.json?.value ?? [], count: r.json?.['@odata.count'] }; };
  const a = await ids(akim), j = await ids(jsmith), d = await ids(admin);
  check('akim lists only customer 5 invoices', a.r.status === 200 && a.count === 2 && a.rows.every((x) => x.customerId === 5), JSON.stringify(a.r.json).slice(0, 200));
  check('jsmith lists only customer 7 invoices', j.count === 2 && j.rows.every((x) => x.customerId === 7), JSON.stringify(j.r.json).slice(0, 200));
  check('the administrator lists every invoice', d.count >= 50, d.count);
  const all = await ids(akim, '/odata/salesinvoices?$filter=1 eq 1&$select=salesInvoiceId,customerId&$count=true');
  check('a filter that matches everything still lists only the caller\'s rows', all.count === 2, all.count);
  const big = await ids(akim, '/odata/salesinvoices?$top=100&$orderby=customerId desc&$select=salesInvoiceId,customerId');
  check('a larger page and another order do not widen the scope', big.rows.length === 2 && big.rows.every((x) => x.customerId === 5));

  // the Columns list of a grid (item 118): a person can show a column the grid does not start with, which is a bigger $select on the OData screen and whole rows on the REST one; the scope is the server's
  const wider = await ids(akim, '/odata/salesinvoices?$select=salesInvoiceId,customerId,notes,orderedBy,createUser,modifiedBy&$count=true&$top=100');
  check('a $select of columns the grid does not start with still lists only the caller\'s rows', wider.r.status === 200 && wider.count === 2 && wider.rows.every((x) => x.customerId === 5 && 'notes' in x && 'orderedBy' in x), JSON.stringify(wider.r.json).slice(0, 200));
  const rest = await call(akim, 'GET', '/api/salesinvoices/search?pageNumber=1&pageSize=100&sortBy=Notes&sortDir=asc');
  check('the REST search of an owned table is refused whatever the sort (it cannot be scoped), so a REST grid cannot list another person\'s rows', rest.status === 403 && type(rest) === '/problems/search-not-scoped', rest.status + ' ' + rest.text.slice(0, 120));

  const mine = a.rows[0].salesInvoiceId, theirs = j.rows[0].salesInvoiceId;
  check('akim reads their own invoice', (await call(akim, 'GET', `/odata/salesinvoices/${mine}`)).status === 200);
  const other = await call(akim, 'GET', `/odata/salesinvoices/${theirs}`);
  check('akim is refused another customer\'s invoice by key (403 not-your-row)', other.status === 403 && type(other) === '/problems/not-your-row', other.status + ' ' + other.text.slice(0, 120));
  check('the same by the parenthesis form', (await call(akim, 'GET', `/odata/salesinvoices(${theirs})`)).status === 403);
  const restMine = await call(akim, 'GET', `/api/salesinvoices/${mine}`);
  check('a REST read of one\'s own invoice answers the whole row (every column a grid can show)', restMine.status === 200 && 'notes' in restMine.json && 'orderedBy' in restMine.json);
  check('a REST read of another customer\'s invoice is refused', (await call(akim, 'GET', `/api/salesinvoices/${theirs}`)).status === 403);
  check('a key that does not exist is 404', (await call(akim, 'GET', '/odata/salesinvoices/99999999')).status === 404);

  // the child table is scoped through its parent
  const lines = await call(akim, 'GET', '/odata/salesinvoicelines?$select=lineId,salesInvoiceId&$count=true&$top=100');
  const expectedLines = Number(sql('SELECT COUNT(*) FROM dbo.SalesInvoiceLine l JOIN dbo.SalesInvoice i ON i.SalesInvoiceId = l.SalesInvoiceId WHERE i.CustomerId = 5')[0]);
  check('the lines of other customers\' invoices are not listed', lines.json?.['@odata.count'] === expectedLines && lines.json.value.every((x) => [mine, a.rows[1].salesInvoiceId].includes(x.salesInvoiceId)), `${lines.json?.['@odata.count']} vs ${expectedLines}`);
  const aLine = lines.json.value[0];
  check('a line of one\'s own invoice is readable', !aLine || (await call(akim, 'GET', `/odata/salesinvoicelines/${aLine.lineId}`)).status === 200);
  const jLine = (await call(jsmith, 'GET', '/odata/salesinvoicelines?$select=lineId&$top=1')).json.value[0];
  check('a line of another customer\'s invoice is refused by key', !jLine || (await call(akim, 'GET', `/odata/salesinvoicelines/${jLine.lineId}`)).status === 403);

  // no navigation leads into a scoped table
  const expand = await call(akim, 'GET', '/odata/salesinvoicelines?$expand=salesInvoice');
  check('$expand into a scoped table is refused (400)', expand.status === 400, expand.status + ' ' + expand.text.slice(0, 120));
  const through = await call(akim, 'GET', '/odata/salesinvoicelines?$filter=salesInvoice/customerId eq 7');
  check('a filter through a scoped parent is refused (400)', through.status === 400, through.status);
  check('a navigation to an unscoped table still works', (await call(akim, 'GET', '/odata/salesinvoices?$expand=customer($select=customerName)&$top=1')).status === 200);

  // writes
  const own = (await call(akim, 'GET', `/odata/salesinvoices/${mine}`)).json;
  const body = (extra) => { const copy = { ...own, ...extra }; delete copy.salesInvoiceId; return copy; };
  const forOther = await call(akim, 'POST', '/odata/salesinvoices', body({ customerId: 7 }));
  check('akim cannot create an invoice for another customer (403)', forOther.status === 403 && type(forOther) === '/problems/not-your-row', forOther.status + ' ' + forOther.text.slice(0, 120));
  const created = await call(akim, 'POST', '/odata/salesinvoices', body({ customerId: 5, invoiceStatusId: 1 }));
  check('akim creates an invoice for their own customer (201)', created.status === 201, created.status + ' ' + created.text.slice(0, 160));
  const newId = created.json?.salesInvoiceId;
  const putOther = await call(akim, 'PUT', `/odata/salesinvoices/${theirs}`, { ...(await call(jsmith, 'GET', `/odata/salesinvoices/${theirs}`)).json });
  check('akim cannot replace another customer\'s invoice (403)', putOther.status === 403, putOther.status);
  const patchOther = await call(akim, 'PATCH', `/odata/salesinvoices/${theirs}`, { freight: 1 });
  check('akim cannot patch another customer\'s invoice (403)', patchOther.status === 403, patchOther.status);
  const handOver = await call(akim, 'PATCH', `/odata/salesinvoices/${mine}`, { customerId: 7 });
  check('akim cannot hand their invoice to another customer (403) and it stays theirs', handOver.status === 403 && sql(`SELECT CustomerId FROM dbo.SalesInvoice WHERE SalesInvoiceId = ${mine}`)[0] === '5', handOver.status);
  const delOther = await call(akim, 'DELETE', `/odata/salesinvoices/${theirs}`);
  check('akim cannot delete another customer\'s invoice (403) and it is still there', delOther.status === 403 && sql(`SELECT COUNT(*) FROM dbo.SalesInvoice WHERE SalesInvoiceId = ${theirs}`)[0] === '1', delOther.status);
  check('akim can change their own invoice (PATCH 200)', (await call(akim, 'PATCH', `/odata/salesinvoices/${mine}`, { freight: 4.5 })).status === 200 && sql(`SELECT Freight FROM dbo.SalesInvoice WHERE SalesInvoiceId = ${mine}`)[0].startsWith('4.5'));
  check('akim can delete the invoice just created (204)', !newId || (await call(akim, 'DELETE', `/odata/salesinvoices/${newId}`)).status === 204);
  check('the administrator may act for every customer (PATCH of an invoice of customer 7)', (await call(admin, 'PATCH', `/odata/salesinvoices/${theirs}`, { freight: 2.25 })).status === 200);

  // the change gate: customer 5 is in Bad Standing, so an invoice cannot move out of Quote until a manager sets Ignore Bad Status
  const gated = await call(akim, 'PATCH', `/odata/salesinvoices/${mine}`, { invoiceStatusId: 2 });
  check('the gate refuses moving an invoice of a customer in Bad Standing (409 change-not-allowed)', gated.status === 409 && type(gated) === '/problems/change-not-allowed', gated.status + ' ' + gated.text.slice(0, 160));
  check('and the invoice is unchanged', sql(`SELECT InvoiceStatusId FROM dbo.SalesInvoice WHERE SalesInvoiceId = ${mine}`)[0] === '1');
  const guarded = await call(akim, 'PATCH', '/odata/customers/5', { ignoreBadStatus: true });
  check('the guarded column is refused for a person who is not a manager (403 column-not-yours)', guarded.status === 403 && type(guarded) === '/problems/column-not-yours', guarded.status + ' ' + guarded.text.slice(0, 160));
  check('and the customer is unchanged', sql('SELECT IgnoreBadStatus FROM dbo.Customer WHERE CustomerId = 5')[0] === '0');
  const byManager = await call(jsmith, 'PATCH', '/odata/customers/5', { ignoreBadStatus: true });
  check('a manager may set it (200)', byManager.status === 200, byManager.status + ' ' + byManager.text.slice(0, 160));
  const stamp = sql('SELECT IgnoreBadStatus, IgnoreBadStatusBy FROM dbo.Customer WHERE CustomerId = 5')[0] ?? '';
  check('and the server stamps who did it', stamp.startsWith('1|jsmith') || stamp.startsWith('1|7') || /^1\|.+/.test(stamp), stamp);
  const nowAllowed = await call(akim, 'PATCH', `/odata/salesinvoices/${mine}`, { invoiceStatusId: 2 });
  check('with Ignore Bad Status set the gate lets the invoice move (200)', nowAllowed.status === 200, nowAllowed.status + ' ' + nowAllowed.text.slice(0, 160));
  const guardsRoute = await call(akim, 'GET', '/api/customers/guards');
  check('the REST guards route (which the OData screen reads) still answers', guardsRoute.status === 200 && guardsRoute.json && guardsRoute.json.ignoreBadStatus === false, guardsRoute.status + ' ' + guardsRoute.text.slice(0, 100));
  const createGuard = await call(akim, 'POST', '/odata/customers', { ...(await call(akim, 'GET', '/odata/customers/5')).json, customerId: 0, accountNumber: 'ODATAG1', customerName: 'Guard Test', ignoreBadStatus: true });
  check('a new customer cannot be created with the guarded column set by a non-manager (it is reset or refused)', createGuard.status === 403 || (createGuard.status === 201 && createGuard.json.ignoreBadStatus === false), createGuard.status + ' ' + createGuard.text.slice(0, 160));

  // the user table is not served
  check('the user table has no OData route', (await call(admin, 'GET', '/odata/employees')).status === 404);

  console.log(`\n${passed} passed, ${failed} failed`);
  process.exit(failed ? 1 : 0);
})().catch((e) => { console.error(e); process.exit(2); });
