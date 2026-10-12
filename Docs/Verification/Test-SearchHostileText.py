"""Feeds hostile text through the generated search routines of one database and checks what comes back.

Usage:  python Test-SearchHostileText.py sqlserver|postgres|mysql [--prove-failure]

It creates a small scratch table in the development database, has the codegen CLI write SP_Search for it, applies the routine, runs the checks and drops the table again.
Passwords come only from the environment: PGPASSWORD (postgres) and MYSQL_PWD (mysql); SQL Server uses the Windows login. Environment overrides:
CODEGEN_EXE, SQLSERVER_NAME, SQLSERVER_DATABASE, PG_HOST, PG_DATABASE, PG_USER, MYSQL_HOST, MYSQL_DATABASE, MYSQL_USER.
--prove-failure removes the escaping from the generated routine first: the wildcard checks must then fail, which shows the checks can.
"""
import os
import re
import shutil
import subprocess
import sys
import tempfile

HERE = os.path.dirname(os.path.abspath(__file__))
CLI = os.environ.get('CODEGEN_EXE', os.path.join(HERE, '..', '..', 'CodeGenNew.Cli', 'bin', 'Debug', 'net10.0', 'codegen.exe'))
PSQL = os.environ.get('PSQL_EXE', 'C:/Program Files/PostgreSQL/18/bin/psql.exe')
MYSQL = os.environ.get('MYSQL_EXE', 'C:/Program Files/MySQL/MySQL Server 8.0/bin/mysql.exe')

ROWS = ['plain', '50% off', 'a_b', 'axb', 'back\\slash', "it's", '[x]', 'x', 'Zed']
SORT_TRICKS = ["Name; DROP TABLE ZzHostileProbe", "x' OR 1=1 --", 'Name DESC', '1', '']
FILTER_CASES = [
    ('a percent sign matches only the value that has one', '%', ['50% off']),
    ('an underscore matches only the value that has one', '_', ['a_b']),
    ('an underscore in a longer value is not a one-letter wildcard', 'a_b', ['a_b']),
    ('a backslash matches only the value that has one', '\\', ['back\\slash']),
    ('a quote is part of the text', "it's", ["it's"]),
    ('a bracket pair is part of the text', '[x]', ['[x]']),
    ('a quote, a semicolon and a comment match nothing', "x'; DROP TABLE ZzHostileProbe; --", []),
    ('an always-true condition matches nothing', "' OR 1=1 --", []),
    ('a UNION attempt matches nothing', "' UNION SELECT 1,2,3 --", []),
]

failures = []
checks = 0


def check(name, ok, detail=''):
    global checks
    checks += 1
    if not ok:
        failures.append(name)
    print(('PASS ' if ok else 'FAIL ') + name + ('' if ok else '   ' + detail))


def run(args, text=None):
    result = subprocess.run(args, capture_output=True, text=True, input=text)
    return result.returncode, (result.stdout or '').strip(), (result.stderr or '').strip()


class SqlServer:
    table = 'ZzHostileProbe'
    name = 'sqlserver'

    def __init__(self):
        self.server = os.environ.get('SQLSERVER_NAME', 'ERICSMINIPC')
        self.database = os.environ.get('SQLSERVER_DATABASE', 'InvoiceSystem')

    def lit(self, s):
        return "N'" + s.replace("'", "''") + "'"

    def exec(self, sql, rows=False, prefix='SET NOCOUNT ON; '):
        code, out, err = run(['sqlcmd', '-S', self.server, '-d', self.database, '-E', '-C', '-b', '-h', '-1', '-W', '-s', '|', '-Q', prefix + sql])
        if code:
            raise RuntimeError(out + err)
        return [line for line in out.splitlines() if line.strip()]

    def create(self):
        self.exec("IF OBJECT_ID('dbo.ZzHostileProbe') IS NOT NULL DROP TABLE dbo.ZzHostileProbe; CREATE TABLE dbo.ZzHostileProbe (ZzHostileProbeId int IDENTITY PRIMARY KEY, Name nvarchar(60) NOT NULL, Note nvarchar(60) NULL);")
        for r in ROWS:
            self.exec('INSERT dbo.ZzHostileProbe (Name) VALUES (' + self.lit(r) + ');')

    def drop(self):
        self.exec("IF OBJECT_ID('dbo.ZzHostileProbe') IS NOT NULL DROP TABLE dbo.ZzHostileProbe; IF OBJECT_ID('dbo.ZzHostileProbe_Search') IS NOT NULL DROP PROCEDURE dbo.ZzHostileProbe_Search; IF OBJECT_ID('dbo.ZzHostileProbe_SearchCount') IS NOT NULL DROP PROCEDURE dbo.ZzHostileProbe_SearchCount;")

    def generate(self, folder):
        code, out, err = run([CLI, '-S', self.server, '-d', self.database, '-t', 'ZzHostileProbe', '-T', 'SP_Search_v1.tt', '-E', '-o', folder])
        if code:
            raise RuntimeError(out + err)
        return os.path.join(folder, 'ZzHostileProbe_Search.sql')

    def weaken(self, text):
        return re.sub(r"REPLACE\(REPLACE\(REPLACE\(REPLACE\((LTRIM\(RTRIM\(@pstrName\)\)), [^)]*\)[^)]*\)[^)]*\)[^)]*\)", r"\1", text).replace(" ESCAPE '\\'", '')

    def apply(self, path):
        text = open(path, encoding='utf-8').read()
        for batch in re.split(r'^\s*GO\s*$', text, flags=re.M):
            if batch.strip():
                self.exec(batch, prefix='')

    def search(self, text, page=1, size=100, sort=None, descending=False):
        sort_part = 'NULL' if sort is None else self.lit(sort)
        arg = 'NULL' if text is None else self.lit(text)
        lines = self.exec(f'EXEC dbo.ZzHostileProbe_Search @pstrName = {arg}, @PageNumber = {page}, @PageSize = {size}, @SortColumn = {sort_part}, @SortDescending = {1 if descending else 0};')
        return [line.split('|', 2)[1] for line in lines]

    def count(self, text):
        arg = 'NULL' if text is None else self.lit(text)
        return int(self.exec(f'EXEC dbo.ZzHostileProbe_SearchCount @pstrName = {arg};')[0])

    def table_rows(self):
        return int(self.exec('SELECT COUNT(*) FROM dbo.ZzHostileProbe;')[0])


class Postgres(SqlServer):
    name = 'postgres'

    def __init__(self):
        self.host = os.environ.get('PG_HOST', 'localhost')
        self.database = os.environ.get('PG_DATABASE', 'InvoiceSystem')
        self.user = os.environ.get('PG_USER', 'ClaudeCode')

    def lit(self, s):
        return "'" + s.replace("'", "''") + "'"

    def exec(self, sql, rows=False):
        code, out, err = run([PSQL, '-h', self.host, '-U', self.user, '-d', self.database, '-v', 'ON_ERROR_STOP=1', '-At', '-F', '|', '-c', sql])
        if code:
            raise RuntimeError(out + err)
        return [line for line in out.splitlines() if line.strip() and not line.startswith(('NOTICE', 'CREATE', 'DROP', 'INSERT'))]

    def create(self):
        self.exec('DROP TABLE IF EXISTS public."ZzHostileProbe"; CREATE TABLE public."ZzHostileProbe" ("ZzHostileProbeId" serial PRIMARY KEY, "Name" varchar(60) NOT NULL, "Note" varchar(60) NULL);')
        for r in ROWS:
            self.exec('INSERT INTO public."ZzHostileProbe" ("Name") VALUES (' + self.lit(r) + ');')

    def drop(self):
        self.exec('DROP FUNCTION IF EXISTS public."ZzHostileProbe_Search"(varchar, integer, integer, text, boolean); DROP FUNCTION IF EXISTS public."ZzHostileProbe_SearchCount"(varchar); DROP TABLE IF EXISTS public."ZzHostileProbe";')

    def generate(self, folder):
        code, out, err = run([CLI, '-S', self.host + ':5432', '-d', self.database, '-t', 'ZzHostileProbe', '-T', 'SP_Search_v1.tt', '-U', self.user, '-P', os.environ['PGPASSWORD'], '--provider', 'PostgreSql', '-s', 'public', '-o', folder])
        if code:
            raise RuntimeError(out + err)
        return os.path.join(folder, 'ZzHostileProbe_Search.sql')

    def weaken(self, text):
        return re.sub(r"REPLACE\(REPLACE\(REPLACE\((btrim\(\"pstrName\"\)), [^)]*\)[^)]*\)[^)]*\)", r"\1", text).replace(" ESCAPE '\\'", '')

    def apply(self, path):
        code, out, err = run([PSQL, '-h', self.host, '-U', self.user, '-d', self.database, '-v', 'ON_ERROR_STOP=1', '-q', '-f', path])
        if code:
            raise RuntimeError(out + err)

    def search(self, text, page=1, size=100, sort=None, descending=False):
        arg = 'NULL' if text is None else self.lit(text)
        sort_part = 'NULL' if sort is None else self.lit(sort)
        return self.exec(f'SELECT "Name" FROM public."ZzHostileProbe_Search"("pstrName" => {arg}, "PageNumber" => {page}, "PageSize" => {size}, "SortColumn" => {sort_part}, "SortDescending" => {"true" if descending else "false"});')

    def count(self, text):
        arg = 'NULL' if text is None else self.lit(text)
        return int(self.exec(f'SELECT public."ZzHostileProbe_SearchCount"("pstrName" => {arg});')[0])

    def table_rows(self):
        return int(self.exec('SELECT COUNT(*) FROM public."ZzHostileProbe";')[0])


class MySql(SqlServer):
    name = 'mysql'

    def __init__(self):
        self.host = os.environ.get('MYSQL_HOST', '127.0.0.1')
        self.database = os.environ.get('MYSQL_DATABASE', 'invoicesystem')
        self.user = os.environ.get('MYSQL_USER', 'ClaudeCode')

    def lit(self, s):
        return "'" + s.replace('\\', '\\\\').replace("'", "''") + "'"

    def exec(self, sql, rows=False):
        code, out, err = run([MYSQL, '-h', self.host, '-u', self.user, self.database, '-N', '-B', '-r', '-e', sql])
        if code:
            raise RuntimeError(out + err)
        return [line for line in out.splitlines() if line.strip()]

    def create(self):
        self.exec('DROP TABLE IF EXISTS zz_hostile_probe; CREATE TABLE zz_hostile_probe (zz_hostile_probe_id int AUTO_INCREMENT PRIMARY KEY, name varchar(60) NOT NULL, note varchar(60) NULL);')
        for r in ROWS:
            self.exec('INSERT INTO zz_hostile_probe (name) VALUES (' + self.lit(r) + ');')

    def drop(self):
        self.exec('DROP PROCEDURE IF EXISTS zz_hostile_probe_Search; DROP PROCEDURE IF EXISTS zz_hostile_probe_SearchCount; DROP TABLE IF EXISTS zz_hostile_probe;')

    def generate(self, folder):
        code, out, err = run([CLI, '-S', self.host + ':3306', '-d', self.database, '-t', 'zz_hostile_probe', '-T', 'SP_Search_v1.tt', '-U', self.user, '-P', os.environ['MYSQL_PWD'], '--provider', 'MySql', '-s', self.database, '-o', folder])
        if code:
            raise RuntimeError(out + err)
        return os.path.join(folder, 'zz_hostile_probe_Search.sql')

    def weaken(self, text):
        return re.sub(r"REPLACE\(REPLACE\(REPLACE\((TRIM\(`pstrName`\)), [^)]*\)[^)]*\)[^)]*\)", r"\1", text).replace(" ESCAPE '\\\\'", '')

    def apply(self, path):
        code, out, err = run([MYSQL, '-h', self.host, '-u', self.user, self.database], text=open(path, encoding='utf-8').read())
        if code:
            raise RuntimeError(out + err)

    def search(self, text, page=1, size=100, sort=None, descending=False):
        arg = 'NULL' if text is None else self.lit(text)
        sort_part = 'NULL' if sort is None else self.lit(sort)
        return [line.split('\t')[1] for line in self.exec(f'CALL zz_hostile_probe_Search({arg}, {page}, {size}, {sort_part}, {1 if descending else 0});')]

    def count(self, text):
        arg = 'NULL' if text is None else self.lit(text)
        return int(self.exec(f'CALL zz_hostile_probe_SearchCount({arg});')[0])

    def table_rows(self):
        return int(self.exec('SELECT COUNT(*) FROM zz_hostile_probe;')[0])


def main():
    provider = {'sqlserver': SqlServer, 'postgres': Postgres, 'mysql': MySql}[sys.argv[1]]()
    folder = tempfile.mkdtemp(prefix='hostile_probe_')
    try:
        provider.drop()
        provider.create()
        path = provider.generate(folder)
        if '--prove-failure' in sys.argv:
            text = open(path, encoding='utf-8').read()
            weakened = provider.weaken(text)
            check('the escaping was found and removed for the failure proof', weakened != text)
            open(path, 'w', encoding='utf-8').write(weakened)
        provider.apply(path)

        everything = sorted(provider.search(None))
        check('no filter returns every row', everything == sorted(ROWS), str(everything))
        check('the count with no filter is every row', provider.count(None) == len(ROWS))
        for name, text, expected in FILTER_CASES:
            got = sorted(provider.search(text))
            check(name, got == sorted(expected), f'expected {expected}, got {got}')
            check(name + ' (count)', provider.count(text) == len(expected), f'expected {len(expected)}')
        check('the table survived every hostile value', provider.table_rows() == len(ROWS))

        ascending = provider.search(None, sort='Name')
        descending = provider.search(None, sort='Name', descending=True)
        check('a known sort name sorts, ascending and descending', ascending == descending[::-1] and ascending != descending, f'{ascending} / {descending}')
        default = provider.search(None)
        for trick in SORT_TRICKS:
            check(f'an unknown sort name gives the default order and no error: {trick!r}', provider.search(None, sort=trick) == default)
        check('the table survived every hostile sort name', provider.table_rows() == len(ROWS))

        first = provider.search(None, page=1, size=4)
        last = provider.search(None, page=3, size=4)
        check('paging: a page of four, then the last page of one', len(first) == 4 and len(last) == 1 and not set(first) & set(last))
        check('paging: a page past the end is empty', provider.search(None, page=9, size=4) == [])
    finally:
        provider.drop()
        shutil.rmtree(folder, ignore_errors=True)
    print(f'{checks - len(failures)} of {checks} passed')
    sys.exit(1 if failures else 0)


main()
