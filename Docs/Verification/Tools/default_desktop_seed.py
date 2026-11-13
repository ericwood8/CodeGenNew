"""Writes the rows the desktop click-through (winui3_default_check.ps1) starts with into a scratch copy of Schemas/default_rows.sql, or prints what the table holds.

python default_desktop_seed.py <sqlite file> seed|show

seed adds a customer with a default billing address, a second billing address and a default shipping address (the table must be empty); show prints the addresses with their marks."""
import sqlite3
import sys

path, action = sys.argv[1], sys.argv[2]
connection = sqlite3.connect(path)
if action == "seed":
    connection.execute("INSERT INTO Customer (Name, City) VALUES ('Home', 'Springfield')")
    owner = connection.execute("SELECT MAX(CustomerId) FROM Customer").fetchone()[0]
    for kind, line, default in (("billing", "A Main St", 1), ("billing", "B Side St", 0), ("shipping", "C Ship Ln", 1)):
        connection.execute("INSERT INTO CustomerAddress (CustomerId, AddressType, Line1, City, Country, IsDefault) VALUES (?, ?, ?, 'Springfield', 'US', ?)", (owner, kind, line, default))
    connection.commit()
for row in connection.execute("SELECT AddressType, Line1, IsDefault FROM CustomerAddress ORDER BY CustomerAddressId"):
    print(row)
