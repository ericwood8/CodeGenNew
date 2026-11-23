"""Seeds a scratch copy of Schemas/price_lists.sql for winui3_price_check.ps1: one item, a list with no group (USD, 2024-01-01 to 2030-12-31) with breaks at 1, 10 and 100 (10, 9 and 8) and a GOLD list with a
break at 1 (9.5). The days are written as the generated apps write them ("2024-01-01 00:00:00"): SQLite compares dates as text, so a bare date sorts before the same day with a time.

python price_desktop_seed.py <scratch sqlite file>
"""
import sqlite3
import sys

with sqlite3.connect(sys.argv[1]) as db:
    for table in ("PriceListItem", "PriceList", "PriceItem"):
        db.execute(f"DELETE FROM {table}")
    db.execute("INSERT INTO PriceItem (PriceItemId, Name) VALUES (1, 'Widget')")
    db.executemany("INSERT INTO PriceList (PriceListId, Name, CurrencyCode, ValidFrom, ValidTo, CustomerGroup) VALUES (?, ?, 'USD', '2024-01-01 00:00:00', '2030-12-31 00:00:00', ?)", [(1, "Std", None), (2, "Gold", "GOLD")])
    db.executemany("INSERT INTO PriceListItem (PriceListId, ItemId, MinQuantity, Price) VALUES (?, 1, ?, ?)", [(1, 1, 10), (1, 10, 9), (1, 100, 8), (2, 1, 9.5)])
print("seeded")
