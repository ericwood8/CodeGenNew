-- The tables of stock.sql for MySQL in snake case, for StockTables=StockLevel (generate with OnlyTables=stock_item,stock_level,stock_ledger,stock_reservation and NamingStyle=Pascal).
-- MYSQL_PWD=... mysql -h 127.0.0.1 -u <login> <database> < stock_mysql.sql      Can be run again.
CREATE TABLE IF NOT EXISTS stock_item (
    stock_item_id INT NOT NULL AUTO_INCREMENT PRIMARY KEY,
    name VARCHAR(60) NOT NULL
);
CREATE TABLE IF NOT EXISTS stock_level (
    stock_level_id INT NOT NULL AUTO_INCREMENT PRIMARY KEY,
    item_id INT NOT NULL,
    on_hand INT NOT NULL DEFAULT 0,
    reserved INT NOT NULL DEFAULT 0,
    reorder_level INT NOT NULL DEFAULT 0,
    UNIQUE KEY uq_stock_level_item (item_id),
    CONSTRAINT fk_stock_level_item FOREIGN KEY (item_id) REFERENCES stock_item (stock_item_id),
    CONSTRAINT ck_stock_level_available CHECK (on_hand - reserved >= 0)
);
CREATE TABLE IF NOT EXISTS stock_ledger (
    stock_ledger_id INT NOT NULL AUTO_INCREMENT PRIMARY KEY,
    item_id INT NOT NULL,
    kind VARCHAR(12) NOT NULL,
    quantity INT NOT NULL,
    occurred_on DATETIME NOT NULL DEFAULT CURRENT_TIMESTAMP,
    reference VARCHAR(60) NULL,
    expires_on DATETIME NULL,
    CONSTRAINT fk_stock_ledger_item FOREIGN KEY (item_id) REFERENCES stock_item (stock_item_id),
    CONSTRAINT ck_stock_ledger_kind CHECK (kind IN ('receipt', 'sale', 'return', 'adjustment', 'reservation', 'release'))
);
CREATE TABLE IF NOT EXISTS stock_reservation (
    stock_reservation_id INT NOT NULL AUTO_INCREMENT PRIMARY KEY,
    item_id INT NOT NULL,
    quantity INT NOT NULL,
    status VARCHAR(10) NOT NULL DEFAULT 'held',
    reference VARCHAR(60) NULL,
    created_on DATETIME NOT NULL DEFAULT CURRENT_TIMESTAMP,
    expires_on DATETIME NOT NULL,
    resolved_on DATETIME NULL,
    CONSTRAINT fk_stock_reservation_item FOREIGN KEY (item_id) REFERENCES stock_item (stock_item_id),
    CONSTRAINT ck_stock_reservation_quantity CHECK (quantity > 0),
    CONSTRAINT ck_stock_reservation_status CHECK (status IN ('held', 'committed', 'released', 'expired'))
);
