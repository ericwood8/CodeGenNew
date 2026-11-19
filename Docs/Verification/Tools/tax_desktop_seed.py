"""Seeds a scratch copy of Schemas/tax_lines_document.sql for winui3_tax_check.ps1, changes it between the clicks, or prints what the invoice lines hold.

python tax_desktop_seed.py <sqlite file> seed|expire|show

seed adds an invoice, the tax categories VAT20 (a rate of 0.2, from 2024-01-01 to 2024-12-31) and VAT5 (0.05, from 2024-01-01, no end) and one line of the invoice in VAT20 (100, rate 0.99 and tax 1, both
stale on purpose, on 2024-06-01). expire ends VAT20 on 2024-03-01, before the line's day. The days are written as the generated apps write them ("2024-06-01 00:00:00"). The tables must be empty."""
import sqlite3
import sys

path, action = sys.argv[1], sys.argv[2]
connection = sqlite3.connect(path)
if action == "seed":
    connection.execute("INSERT INTO Invoice (Customer, InvoiceDate, Reference, Notes, Currency) VALUES ('Acme', '2024-06-01 00:00:00', 'INV-1', NULL, 'USD')")
    connection.executemany("INSERT INTO TaxCategory (Code, Name, Rate, ValidFrom, ValidTo) VALUES (?, ?, ?, ?, ?)",
                           [("VAT20", "Standard", 0.2, "2024-01-01 00:00:00", "2024-12-31 00:00:00"), ("VAT5", "Reduced", 0.05, "2024-01-01 00:00:00", None)])
    connection.execute("INSERT INTO InvoiceLine (InvoiceId, LineDate, Description, TaxCategoryId, TaxRate, LineTotal, TaxAmount) VALUES (1, '2024-06-01 00:00:00', 'Widgets', 1, 0.99, 100, 1)")
    connection.commit()
elif action == "expire":
    connection.execute("UPDATE TaxCategory SET ValidTo = '2024-03-01 00:00:00' WHERE Code = 'VAT20'")
    connection.commit()
for row in connection.execute("SELECT l.InvoiceId, c.Code, l.TaxRate, l.LineTotal, l.TaxAmount, l.LineDate FROM InvoiceLine l LEFT JOIN TaxCategory c ON c.TaxCategoryId = l.TaxCategoryId ORDER BY l.InvoiceLineId"):
    print(row)
