"""Seeds a scratch copy of Schemas/document_totals.sql for winui3_allowance_tax_check.ps1, or prints what the allowance rows hold.

python allowance_tax_desktop_seed.py <sqlite file> seed|show

seed empties the tables and adds an invoice (Acme, USD), the tax categories T20 (a rate of 0.2, from 2000-01-01, no end) and OLD (0.1, from 2000-01-01 to 2020-12-31) and one allowance row of the invoice
(amount 20, category T20, rate 0.99 and tax 1, both stale on purpose). The days are written as the generated apps write them ("2000-01-01 00:00:00")."""
import sqlite3
import sys

path, action = sys.argv[1], sys.argv[2]
connection = sqlite3.connect(path)
if action == "seed":
    for table in ("InvoiceAllowanceCharge", "InvoiceLine", "TaxCategory", "Invoice"):
        connection.execute(f"DELETE FROM {table}")
    connection.execute("DELETE FROM sqlite_sequence")
    connection.execute("INSERT INTO Invoice (Customer, InvoiceDate, Currency) VALUES ('Acme', '2024-06-01 00:00:00', 'USD')")
    connection.executemany("INSERT INTO TaxCategory (Code, Name, Rate, ValidFrom, ValidTo) VALUES (?, ?, ?, ?, ?)",
                           [("T20", "Standard", 0.2, "2000-01-01 00:00:00", None), ("OLD", "Ended", 0.1, "2000-01-01 00:00:00", "2020-12-31 00:00:00")])
    connection.execute("INSERT INTO InvoiceAllowanceCharge (InvoiceId, IsCharge, Amount, TaxCategoryId, TaxRate, TaxAmount) VALUES (1, 0, 20, 1, 0.99, 1)")
    connection.commit()
for row in connection.execute("SELECT a.InvoiceId, c.Code, a.IsCharge, a.Amount, a.TaxRate, a.TaxAmount FROM InvoiceAllowanceCharge a LEFT JOIN TaxCategory c ON c.TaxCategoryId = a.TaxCategoryId ORDER BY a.InvoiceAllowanceChargeId"):
    print(row)
for row in connection.execute("SELECT AllowanceTotal, TaxTotal, Payable FROM Invoice"):
    print(row)
