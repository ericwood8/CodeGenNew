import base64
import hashlib
import os
import sqlite3
import sys

db = sys.argv[1]
password = 'secret-pass-1'


def hash_password(text):
    salt = os.urandom(16)
    digest = hashlib.pbkdf2_hmac('sha256', text.encode(), salt, 600000, 32)
    return 'pbkdf2-sha256$600000$' + base64.b64encode(salt).decode() + '$' + base64.b64encode(digest).decode()


c = sqlite3.connect(db)
c.execute('delete from EmployeeTenant')
c.execute('delete from Employee')
people = [(1, 'Ada Lovelace', 'ada', 0, 0), (2, 'Grace Hopper', 'grace', 0, 0), (3, 'Alan Turing', 'alan', 0, 0)]
for pid, name, login, manager, admin in people:
    c.execute('insert into Employee (EmployeeId, FullName, Login, PasswordHash, IsManager, IsAdministrator) values (?,?,?,?,?,?)', (pid, name, login, hash_password(password), manager, admin))
c.execute("insert into EmployeeTenant (EmployeeId, TenantId, Role) values (1, 1, 'Administrator')")
c.execute("insert into EmployeeTenant (EmployeeId, TenantId, Role) values (1, 2, 'Administrator')")
c.execute("insert into EmployeeTenant (EmployeeId, TenantId, Role) values (2, 1, NULL)")
c.commit()
print('seeded')
