"""Seeds a scratch copy of Schemas/rates.sql for winui3_rate_check.ps1: two USD to EUR rates, 1.5 from 2024-01-01 and 1.7 from 2024-03-01. The days are written as the generated apps write them ("2024-01-01 00:00:00"): SQLite compares dates as text, so a bare date sorts before the same day with a time.

python rate_desktop_seed.py <scratch sqlite file>
"""
import sqlite3
import sys

with sqlite3.connect(sys.argv[1]) as db:
    db.execute("DELETE FROM ExchangeRate")
    db.executemany("INSERT INTO ExchangeRate (FromCurrency, ToCurrency, Rate, RateDate) VALUES (?, ?, ?, ?)", [("USD", "EUR", 1.5, "2024-01-01 00:00:00"), ("USD", "EUR", 1.7, "2024-03-01 00:00:00")])
print("seeded")
