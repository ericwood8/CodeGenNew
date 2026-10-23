# PY_Model_v1

`app/models/<table>.py`: the SQLAlchemy 2.0 class of a table. See `PY_Routes_v1.md` for the whole Python stack and its limits.

A PostgreSQL array, interval, range or built-in shape column is a `str` field of the `PgText` column type (written in `app/db.py`): the select casts the column to text and the insert and update cast the text to the column's type, a blank being NULL. The validator checks the notation with `matches_pattern`.
