-- customer_address for the standing MySQL invoicesystem database (it has the customer table): a type and one default per customer and type, for
-- DefaultRowTables=CustomerAddress:IsDefault:CustomerId+AddressType (generate with --only CustomerAddress,Customer; play it with default_check.py --existing).
-- MYSQL_PWD=... mysql -h 127.0.0.1 -u <login> invoicesystem < default_rows_mysql.sql      Can be run again. MySQL has no partial unique index, so the rule rests on the generated code alone.
CREATE TABLE IF NOT EXISTS customer_address (
    customer_address_id INT NOT NULL AUTO_INCREMENT PRIMARY KEY,
    customer_id INT NOT NULL,
    address_type VARCHAR(10) NOT NULL,
    line1 VARCHAR(200) NOT NULL,
    line2 VARCHAR(200) NULL,
    city VARCHAR(100) NOT NULL,
    region VARCHAR(50) NULL,
    postal_code VARCHAR(25) NULL,
    country VARCHAR(100) NOT NULL,
    is_default TINYINT(1) NOT NULL DEFAULT 0,
    CONSTRAINT fk_customer_address_customer FOREIGN KEY (customer_id) REFERENCES customer (customer_id),
    CONSTRAINT ck_customer_address_type CHECK (address_type IN ('billing', 'shipping'))
);
