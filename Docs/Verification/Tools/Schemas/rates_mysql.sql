-- The table of rates.sql for MySQL in snake case, for RateTables=ExchangeRate (generate with OnlyTables=ExchangeRate and NamingStyle=Pascal).
-- MYSQL_PWD=... mysql -h 127.0.0.1 -u <login> <database> < rates_mysql.sql      Can be run again.
CREATE TABLE IF NOT EXISTS exchange_rate (
    exchange_rate_id INT NOT NULL AUTO_INCREMENT PRIMARY KEY,
    from_currency VARCHAR(3) NOT NULL,
    to_currency VARCHAR(3) NOT NULL,
    rate DECIMAL(18, 6) NOT NULL,
    rate_date DATE NOT NULL,
    source VARCHAR(60) NULL,
    notes VARCHAR(200) NULL,
    UNIQUE KEY ux_exchange_rate_pair_day (from_currency, to_currency, rate_date)
);
CREATE TABLE IF NOT EXISTS rated_invoice (
    rated_invoice_id INT NOT NULL AUTO_INCREMENT PRIMARY KEY,
    customer_name VARCHAR(60) NOT NULL,
    currency_code VARCHAR(3) NOT NULL,
    fx_rate DECIMAL(18, 6) NOT NULL DEFAULT 1,
    total DECIMAL(18, 2) NOT NULL,
    base_total DECIMAL(18, 2) NOT NULL DEFAULT 0,
    invoice_date DATE NOT NULL,
    notes VARCHAR(200) NULL
);
CREATE TABLE IF NOT EXISTS rate_user (
    rate_user_id INT NOT NULL AUTO_INCREMENT PRIMARY KEY,
    full_name VARCHAR(40) NOT NULL,
    login VARCHAR(30) NOT NULL,
    password_hash VARCHAR(200) NOT NULL DEFAULT 'x',
    is_manager TINYINT(1) NOT NULL DEFAULT 0,
    is_administrator TINYINT(1) NOT NULL DEFAULT 0
);
CREATE TABLE IF NOT EXISTS exchange_rate_stamp (
    exchange_rate_stamp_id INT NOT NULL AUTO_INCREMENT PRIMARY KEY,
    from_currency VARCHAR(3) NOT NULL,
    to_currency VARCHAR(3) NOT NULL,
    rate DECIMAL(18, 6) NOT NULL,
    rate_date DATETIME NOT NULL
);
