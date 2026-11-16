"""Another writer for the race of a desktop dialog: takes the write lock of a SQLite file, holds it for a while and then adds an identifier pair for the first party and commits.

python party_rival_writer.py <sqlite file> <scheme> <value> <seconds>

A dialog that checks the pair while this runs still sees the pair free (readers are not blocked by the lock) and its save then waits for the lock, so the pair is taken between its check and its
save and only the unique index on the pair can refuse it. winui3_party_race_check.ps1 starts it just before it presses Save."""
import sqlite3
import sys
import time

path, scheme, value, seconds = sys.argv[1], sys.argv[2], sys.argv[3], float(sys.argv[4])
connection = sqlite3.connect(path, isolation_level=None, timeout=30)
connection.execute("BEGIN IMMEDIATE")
print("locked", flush=True)
time.sleep(seconds)
connection.execute("INSERT INTO PartyIdentifier (PartyId, Scheme, Value) VALUES ((SELECT MIN(PartyId) FROM Party), ?, ?)", (scheme, value))
connection.execute("COMMIT")
print("committed", flush=True)
