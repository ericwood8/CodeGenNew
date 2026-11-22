"""Seeds a scratch copy of Schemas/allowance_charges_document.sql for winui3_allowance_check.ps1, or prints what the rows hold.

python allowance_desktop_seed.py <sqlite file> seed|show

seed adds one invoice (Acme) and three rows on it: an allowance of 10 percent on a base of 200 (20), an allowance of 5 and a charge of 60, so the document's totals are allowances 25, charges 60 and net 35.
The tables must be empty."""
import sqlite3
import sys

path, action = sys.argv[1], sys.argv[2]
connection = sqlite3.connect(path)
if action == "seed":
    connection.execute("INSERT INTO Invoice (Customer) VALUES ('Acme')")
    connection.executemany("INSERT INTO InvoiceAllowanceCharge (InvoiceId, IsCharge, BaseAmount, Percentage, Amount) VALUES (1, ?, ?, ?, ?)", [(0, 200, 10, 20), (0, 0, 0, 5), (1, 0, 0, 60)])
    connection.commit()
for row in connection.execute("SELECT InvoiceAllowanceChargeId, IsCharge, BaseAmount, Percentage, Amount FROM InvoiceAllowanceCharge ORDER BY InvoiceAllowanceChargeId"):
    print(row)
