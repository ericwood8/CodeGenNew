-- The tables of parties.sql for MySQL in snake case, for PartyTables=Party:PartyIdentifier (generate with OnlyTables=Party,PartyIdentifier).
-- MYSQL_PWD=... mysql -h 127.0.0.1 -u <login> <database> < parties_mysql.sql      Can be run again.
CREATE TABLE IF NOT EXISTS party (
    party_id INT NOT NULL AUTO_INCREMENT PRIMARY KEY,
    legal_name VARCHAR(100) NOT NULL,
    trading_name VARCHAR(100) NULL,
    city VARCHAR(60) NULL,
    phone VARCHAR(30) NULL,
    notes VARCHAR(200) NULL
);
CREATE TABLE IF NOT EXISTS party_identifier (
    party_identifier_id INT NOT NULL AUTO_INCREMENT PRIMARY KEY,
    party_id INT NOT NULL,
    scheme VARCHAR(20) NOT NULL,
    value VARCHAR(50) NOT NULL,
    issued_by VARCHAR(60) NULL,
    notes VARCHAR(200) NULL,
    CONSTRAINT fk_party_identifier_party FOREIGN KEY (party_id) REFERENCES party (party_id),
    CONSTRAINT ux_party_identifier_pair UNIQUE (scheme, value)
);
