"""Writes the rows the desktop click-through (winui3_snapshot_check.ps1) starts with into a scratch copy of Schemas/snapshot.sql, or prints what the order lines hold.

python snapshot_desktop_seed.py <sqlite file> seed|show

seed adds a product (Widget, 10.5), a tax category (Standard, 8.25) and an order (the tables must be empty); show prints the order lines with their copies and totals."""
import sqlite3
import sys

path, action = sys.argv[1], sys.argv[2]
connection = sqlite3.connect(path)
if action == "seed":
    connection.execute("INSERT INTO Product (Name, Price) VALUES ('Widget', 10.5)")
    connection.execute("INSERT INTO TaxCategory (Name, Rate) VALUES ('Standard', 8.25)")
    connection.execute("INSERT INTO SalesOrder (Reference, Customer) VALUES ('SO-1', 'Acme')")
    connection.commit()
if action == "rename":
    connection.execute("UPDATE Product SET Name = 'Gadget', Price = 20.5")
    connection.commit()
for row in connection.execute("SELECT ProductName, UnitPrice, TaxRate, Quantity, LineTotal FROM OrderItem ORDER BY OrderItemId"):
    print(row)
