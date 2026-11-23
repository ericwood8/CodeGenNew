"""Seeds a scratch copy of Schemas/payments.sql for winui3_payment_check.ps1: one document (D1, ordered 2027-03-01, total 300) and payment terms Net 30 (30 days to pay), and no bank accounts, payment means or installments.

python payment_desktop_seed.py <scratch sqlite file>
"""
import sqlite3
import sys

with sqlite3.connect(sys.argv[1]) as db:
    for table in ("PaymentInstallment", "PaymentMeans", "PartyBankAccount", "PaymentTerms", "PayDocument"):
        db.execute(f"DELETE FROM {table}")
    db.execute("INSERT INTO PayDocument (PayDocumentId, Number, OrderedOn, Total) VALUES (1, 'D1', '2027-03-01 00:00:00', 300)")
    db.execute("INSERT INTO PaymentTerms (PaymentTermsId, Code, Name, DaysToPay) VALUES (1, 'NET30', 'Net 30', 30)")
print("seeded")
