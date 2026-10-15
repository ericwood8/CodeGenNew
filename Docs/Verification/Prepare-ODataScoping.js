// Prepares the SCRATCH copy of InvoiceSystem for Test-ODataScoping.js (see there): test passwords for three people (derived from TEST_SEED, hashed like the API does) and invoices handed to customers 5 and 7.
const crypto = require('crypto');
const { execFileSync } = require('child_process');

const seed = process.env.TEST_SEED;
if (!seed) throw new Error('set TEST_SEED');
const people = { jdoe: 1, akim: 5, jsmith: 7 };
const password = (name) => 'T!' + crypto.createHash('sha256').update(seed + name).digest('hex').slice(0, 16);
const hash = (pw) => {
  const salt = crypto.randomBytes(16);
  const derived = crypto.pbkdf2Sync(pw, salt, 600000, 32, 'sha256');
  return `pbkdf2-sha256$600000$${salt.toString('base64')}$${derived.toString('base64')}`;
};
const statements = Object.entries(people).map(([name, id]) => `UPDATE dbo.Employee SET PasswordHash = N'${hash(password(name))}' WHERE EmployeeId = ${id};`);
statements.push(
  // two invoices each for customers 5 and 7 (from customers 3 and 4); customer 5 is in Bad Standing for the gate
  'UPDATE dbo.SalesInvoice SET CustomerId = 5 WHERE CustomerId = 3;',
  'UPDATE dbo.SalesInvoice SET CustomerId = 7 WHERE CustomerId = 4;',
  'UPDATE dbo.Customer SET CustomerStatusId = 2, IgnoreBadStatus = 0 WHERE CustomerId = 5;',
  'UPDATE dbo.Customer SET CustomerStatusId = 1, IgnoreBadStatus = 0 WHERE CustomerId = 7;'
);
execFileSync('sqlcmd', ['-S', 'ERICSMINIPC', '-d', 'InvoiceSystem_scratch', '-E', '-I', '-b', '-Q', statements.join(' ')], { stdio: 'inherit' });
console.log('prepared');
