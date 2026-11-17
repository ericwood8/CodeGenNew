"""Seeds a scratch copy of Schemas/rate_documents.sql for winui3_rate_document_check.ps1, or prints what the invoices hold.

python rate_document_desktop_seed.py <sqlite file> seed|show

seed adds the rates EUR to USD 1.1 (from 2024-01-01), GBP to USD 1.3 (from 2024-01-01), USD to EUR 0.5 (from 2024-03-01, newer than the first EUR to USD rate) and one invoice in EUR (100, rate 99 and a
converted total of 1, both stale on purpose). The days are written as the generated apps write them ("2024-01-01 00:00:00"). The tables must be empty."""
import sqlite3
import sys

path, action = sys.argv[1], sys.argv[2]
connection = sqlite3.connect(path)
if action == "seed":
    connection.executemany("INSERT INTO ExchangeRate (FromCurrency, ToCurrency, Rate, RateDate) VALUES (?, ?, ?, ?)",
                           [("EUR", "USD", 1.1, "2024-01-01 00:00:00"), ("GBP", "USD", 1.3, "2024-01-01 00:00:00"), ("USD", "EUR", 0.5, "2024-03-01 00:00:00")])
    connection.execute("INSERT INTO SalesInvoice (CustomerName, CurrencyCode, FxRate, Total, BaseTotal, InvoiceDate) VALUES ('Acme', 'EUR', 99, 100, 1, '2024-01-15 00:00:00')")
    connection.commit()
for row in connection.execute("SELECT CustomerName, CurrencyCode, FxRate, Total, BaseTotal, InvoiceDate FROM SalesInvoice ORDER BY SalesInvoiceId"):
    print(row)
