"""Seeds a scratch copy of Schemas/payments.sql for winui3_ledger_check.ps1: document 1 (total 300) with an authorize of 300, a capture of 200, a refund of 50 and a pending capture of 40, and document 2 (total 100) captured in full.

python ledger_desktop_seed.py <scratch sqlite file>
"""
import sqlite3
import sys

ROWS = [
    (1, "authorize", "succeeded", 300, "g1", "k1"),
    (1, "capture", "succeeded", 200, "g2", "k2"),
    (1, "refund", "succeeded", -50, "g3", "k3"),
    (1, "capture", "pending", 40, "g4", "k4"),
    (2, "capture", "succeeded", 100, "g5", "k5"),
]

with sqlite3.connect(sys.argv[1]) as db:
    for table in ("PayTransaction", "PaymentInstallment", "PaymentMeans", "PartyBankAccount", "PaymentTerms", "PayDocument"):
        db.execute(f"DELETE FROM {table}")
    db.execute("INSERT INTO PayDocument (PayDocumentId, Number, OrderedOn, Total) VALUES (1, 'D1', '2027-03-01 00:00:00', 300)")
    db.execute("INSERT INTO PayDocument (PayDocumentId, Number, OrderedOn, Total) VALUES (2, 'D2', '2027-03-01 00:00:00', 100)")
    for document, kind, status, amount, gateway, key in ROWS:
        db.execute("INSERT INTO PayTransaction (PayDocumentId, Kind, Status, Amount, GatewayId, IdempotencyKey, CreatedOn) VALUES (?, ?, ?, ?, ?, ?, '2027-03-02 09:00:00')", (document, kind, status, amount, gateway, key))
print("seeded")
