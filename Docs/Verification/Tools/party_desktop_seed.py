"""Writes the rows the desktop click-through (winui3_party_check.ps1) starts with into a scratch copy of Schemas/parties.sql, or prints what the identifier table holds.

python party_desktop_seed.py <sqlite file> seed|show

seed empties the two tables and adds three parties: Acme holds VAT GB123 and DUNS 111, Bolt holds DUNS GB123 (the same value under another scheme) and Core holds VAT DE999; show prints the identifiers."""
import sqlite3
import sys

path, action = sys.argv[1], sys.argv[2]
connection = sqlite3.connect(path)
if action == "seed":
    connection.execute("DELETE FROM PartyIdentifier")
    connection.execute("DELETE FROM Party")
    for name, held in (("Acme", (("VAT", "GB123"), ("DUNS", "111"))), ("Bolt", (("DUNS", "GB123"),)), ("Core", (("VAT", "DE999"),))):
        connection.execute("INSERT INTO Party (LegalName, City) VALUES (?, 'Springfield')", (name,))
        party = connection.execute("SELECT MAX(PartyId) FROM Party").fetchone()[0]
        for scheme, value in held:
            connection.execute("INSERT INTO PartyIdentifier (PartyId, Scheme, Value) VALUES (?, ?, ?)", (party, scheme, value))
    connection.commit()
for row in connection.execute("SELECT p.LegalName, i.Scheme, i.Value FROM PartyIdentifier i JOIN Party p ON p.PartyId = i.PartyId ORDER BY i.PartyIdentifierId"):
    print(row)
