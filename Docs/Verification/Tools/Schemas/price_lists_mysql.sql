-- The tables of price_lists.sql for MySQL in snake case, for PriceListTables (generate with OnlyTables=price_item,price_list,price_list_item and NamingStyle=Pascal).
-- MYSQL_PWD=... mysql -h 127.0.0.1 -u <login> <database> < price_lists_mysql.sql      Can be run again.
CREATE TABLE IF NOT EXISTS price_item (
    price_item_id INT NOT NULL AUTO_INCREMENT PRIMARY KEY,
    name VARCHAR(80) NOT NULL,
    sku VARCHAR(40) NULL,
    unit_name VARCHAR(20) NULL,
    notes VARCHAR(200) NULL
);
CREATE TABLE IF NOT EXISTS price_list (
    price_list_id INT NOT NULL AUTO_INCREMENT PRIMARY KEY,
    name VARCHAR(80) NOT NULL,
    currency_code VARCHAR(3) NOT NULL,
    valid_from DATE NOT NULL,
    valid_to DATE NULL,
    customer_group VARCHAR(40) NULL
);
CREATE TABLE IF NOT EXISTS price_list_item (
    price_list_item_id INT NOT NULL AUTO_INCREMENT PRIMARY KEY,
    price_list_id INT NOT NULL,
    item_id INT NOT NULL,
    min_quantity DECIMAL(18, 3) NOT NULL,
    price DECIMAL(18, 4) NOT NULL,
    CONSTRAINT fk_price_list_item_list FOREIGN KEY (price_list_id) REFERENCES price_list (price_list_id),
    CONSTRAINT fk_price_list_item_item FOREIGN KEY (item_id) REFERENCES price_item (price_item_id),
    UNIQUE KEY ux_price_list_item_break (price_list_id, item_id, min_quantity)
);
