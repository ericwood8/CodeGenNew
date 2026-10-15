// Checks WriteRoles and TableWriteRoles on the generated REST and OData routes with real sign-ins, on a SCRATCH copy of InvoiceSystem prepared by Prepare-ODataScoping.js
// (TEST_SEED derives the three test passwords). The API is generated with WriteRoles=Manager, TableWriteRoles=Item:Administrator;CustomerStatus: and runs on PORT (default 5206).
// jdoe is the administrator, jsmith a manager, akim neither.
const crypto = require('crypto');
const { execFileSync } = require('child_process');

const BASE = 'http://localhost:' + (process.env.PORT || '5206');
const seed = process.env.TEST_SEED;
if (!seed) throw new Error('set TEST_SEED');
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
const refused = (r) => r.status === 403 && r.json && r.json.type === '/problems/role-not-allowed';
const ok2xx = (r) => r.status >= 200 && r.status < 300;
const count = (table) => Number(sql(`SELECT COUNT(*) FROM dbo.${table}`)[0]);

(async () => {
  const [admin, akim, jsmith] = [await login('jdoe'), await login('akim'), await login('jsmith')];   // jdoe: administrator, akim: Employee, jsmith: Manager

  const customer = (await call(admin, 'GET', '/api/customers/1')).json;
  const fresh = (n) => { const copy = { ...customer, accountNumber: 'WR' + n + Date.now().toString().slice(-6), customerName: 'Role Test ' + n }; delete copy.customerId; return copy; };
  const before = count('Customer');

  // reads stay open to every signed-in person
  check('no token is still 401', (await call(null, 'GET', '/api/customers')).status === 401);
  check('REST list is open to a person with no write role', (await call(akim, 'GET', '/api/customers')).status === 200);
  check('OData list is open to a person with no write role', (await call(akim, 'GET', '/odata/customers')).status === 200);
  check('REST read by key is open', (await call(akim, 'GET', '/api/customers/1')).status === 200);
  check('the lookup is open', (await call(akim, 'GET', '/api/customers/lookup')).status === 200);

  // REST writes by a person with no write role
  check('REST create is refused (403 role-not-allowed)', refused(await call(akim, 'POST', '/api/customers', fresh(1))));
  check('REST update is refused', refused(await call(akim, 'PUT', '/api/customers/1', { ...customer })));
  check('REST delete is refused', refused(await call(akim, 'DELETE', '/api/customers/1')));
  const clone = await call(akim, 'POST', '/api/customers/1/clone');
  check('REST clone is refused (or the table has no clone route)', refused(clone) || clone.status === 404 || clone.status === 405, clone.status + ' ' + clone.text.slice(0, 100));

  // OData writes by a person with no write role
  check('OData create is refused', refused(await call(akim, 'POST', '/odata/customers', fresh(2))));
  check('OData put is refused', refused(await call(akim, 'PUT', '/odata/customers/1', { ...customer })));
  check('OData patch is refused', refused(await call(akim, 'PATCH', '/odata/customers/1', { customerName: 'Nope' })));
  check('OData delete is refused', refused(await call(akim, 'DELETE', '/odata/customers/1')));
  check('and nothing changed in the table', count('Customer') === before && sql('SELECT CustomerName FROM dbo.Customer WHERE CustomerId = 1')[0] === customer.customerName);

  // a manager writes
  const made = await call(jsmith, 'POST', '/api/customers', fresh(3));
  check('a manager creates through REST (201)', made.status === 201, made.status + ' ' + made.text.slice(0, 160));
  const madeId = made.json && made.json.customerId;
  const patched = await call(jsmith, 'PATCH', `/odata/customers/${madeId}`, { customerName: 'Role Test Patched' });
  check('a manager patches through OData (200)', patched.status === 200, patched.status + ' ' + patched.text.slice(0, 160));
  check('the patch reached the database', sql(`SELECT CustomerName FROM dbo.Customer WHERE CustomerId = ${madeId}`)[0] === 'Role Test Patched');
  const odataMade = await call(jsmith, 'POST', '/odata/customers', fresh(4));
  check('a manager creates through OData (201)', odataMade.status === 201, odataMade.status + ' ' + odataMade.text.slice(0, 160));
  check('a manager deletes through OData (204)', (await call(jsmith, 'DELETE', `/odata/customers/${odataMade.json && odataMade.json.customerId}`)).status === 204);
  check('a manager deletes through REST', ok2xx(await call(jsmith, 'DELETE', `/api/customers/${madeId}`)));
  check('the table is as it was', count('Customer') === before);

  // the administrator always writes
  const adminMade = await call(admin, 'POST', '/api/customers', fresh(5));
  check('the administrator creates through REST', adminMade.status === 201, adminMade.status + ' ' + adminMade.text.slice(0, 160));
  check('and deletes through OData', (await call(admin, 'DELETE', `/odata/customers/${adminMade.json && adminMade.json.customerId}`)).status === 204);

  // a table with its own rule: only the administrator
  const item = (await call(admin, 'GET', '/api/items')).json[0];
  const freshItem = () => { const copy = { ...item }; for (const k of Object.keys(copy)) if (/id$/i.test(k) && k.toLowerCase() === 'itemid') delete copy[k]; copy.itemNumber = 'WR' + Date.now().toString().slice(-7); return copy; };
  check('a manager is refused on a table that lists only the administrator (REST)', refused(await call(jsmith, 'POST', '/api/items', freshItem())));
  check('a manager is refused on a table that lists only the administrator (OData)', refused(await call(jsmith, 'POST', '/odata/items', freshItem())));
  const adminItem = await call(admin, 'POST', '/api/items', freshItem());
  check('the administrator writes it', adminItem.status === 201, adminItem.status + ' ' + adminItem.text.slice(0, 160));
  check('and removes the test row', !adminItem.json || ok2xx(await call(admin, 'DELETE', `/api/items/${adminItem.json.itemId}`)));

  // a table whose entry is empty: any signed-in person writes
  const status = (await call(admin, 'GET', '/api/customerstatus')).json[0];
  const madeStatus = await call(akim, 'POST', '/api/customerstatus', { ...Object.fromEntries(Object.entries(status).map(([k, v]) => [k, typeof v === 'string' ? v.slice(0, 3) + Date.now().toString().slice(-5) : v])), customerStatusId: 0, name: 'RT' + Date.now().toString().slice(-8), isActive: false });
  check('a table with an empty entry lets any signed-in person write it', madeStatus.status === 201 || madeStatus.status === 200, madeStatus.status + ' ' + madeStatus.text.slice(0, 160));
  if (madeStatus.json && madeStatus.json.customerStatusId) await call(akim, 'DELETE', `/api/customerstatus/${madeStatus.json.customerStatusId}`);

  // reads: Item lists only the administrator in TableReadRoles
  const readRefused = (r) => r.status === 403 && r.json && r.json.type === '/problems/role-not-allowed';
  check('the administrator reads Item (REST)', (await call(admin, 'GET', '/api/items')).status === 200);
  check('a manager is refused the Item list (REST)', readRefused(await call(jsmith, 'GET', '/api/items')));
  check('a manager is refused one Item by key (REST)', readRefused(await call(jsmith, 'GET', '/api/items/' + item.itemId)));
  check('a manager is refused the Item search', readRefused(await call(jsmith, 'GET', '/api/items/search?pageNumber=1&pageSize=5')));
  check('a manager is refused the Item list (OData)', readRefused(await call(jsmith, 'GET', '/odata/items')));
  check('a manager is refused one Item by key (OData)', readRefused(await call(jsmith, 'GET', '/odata/items/' + item.itemId)));
  check('the administrator reads Item through OData', (await call(admin, 'GET', '/odata/items')).status === 200);
  check('the drop-down lookup of Item stays open to a manager', (await call(jsmith, 'GET', '/api/items/lookup')).status === 200);
  check('a table with no read rule is still read by every role', (await call(akim, 'GET', '/odata/customers')).status === 200 && (await call(akim, 'GET', '/api/customers')).status === 200);

  console.log(`\n${passed} passed, ${failed} failed`);
  process.exit(failed ? 1 : 0);
})().catch((e) => { console.error(e); process.exit(2); });
