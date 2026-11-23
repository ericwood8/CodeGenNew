"""Seeds a scratch copy of Schemas/stock.sql for winui3_stock_check.ps1: item 1 with 10 on hand, 4 reserved and a reorder level of 8 (a receipt of 10 and a hold of 4 in the ledger).

python stock_desktop_seed.py <scratch sqlite file>
"""
import sqlite3
import sys

with sqlite3.connect(sys.argv[1]) as db:
    for table in ("StockReservation", "StockLedger", "StockLevel", "StockItem"):
        db.execute(f"DELETE FROM {table}")
    db.execute("INSERT INTO StockItem (StockItemId, Name) VALUES (1, 'Widget')")
    db.execute("INSERT INTO StockLevel (ItemId, OnHand, Reserved, ReorderLevel) VALUES (1, 10, 4, 8)")
    db.execute("INSERT INTO StockLedger (ItemId, Kind, Quantity, OccurredOn) VALUES (1, 'receipt', 10, '2027-03-02 09:00:00')")
    db.execute("INSERT INTO StockLedger (ItemId, Kind, Quantity, OccurredOn, ExpiresOn) VALUES (1, 'reservation', 4, '2027-03-02 09:05:00', '2099-01-01 00:00:00')")
    db.execute("INSERT INTO StockReservation (ItemId, Quantity, Status, CreatedOn, ExpiresOn) VALUES (1, 4, 'held', '2027-03-02 09:05:00', '2099-01-01 00:00:00')")
print("seeded")
