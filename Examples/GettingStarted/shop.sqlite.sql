-- A small shop: three tables, one foreign key chain, a few CHECK constraints the generator turns into limits on the forms.
-- sqlite3 shop.db < shop.sqlite.sql      (the sqlite3 command-line tool, or any SQLite editor that runs a script)
CREATE TABLE customer (
    customer_id  INTEGER PRIMARY KEY,
    name         VARCHAR(50) NOT NULL,
    email        VARCHAR(100) NULL,
    credit_limit DECIMAL(10,2) NULL CHECK (credit_limit >= 0),
    status       VARCHAR(10) NOT NULL DEFAULT 'Active' CHECK (status IN ('Active', 'On hold', 'Closed')),
    is_taxable   BOOLEAN NOT NULL DEFAULT 0
);

CREATE TABLE product (
    product_id INTEGER PRIMARY KEY,
    name       VARCHAR(80) NOT NULL,
    price      DECIMAL(10,2) NOT NULL CHECK (price > 0),
    rating     INTEGER NULL CHECK (rating BETWEEN 1 AND 5)
);

CREATE TABLE sales_order (
    sales_order_id INTEGER PRIMARY KEY,
    customer_id    INTEGER NOT NULL REFERENCES customer (customer_id),
    product_id     INTEGER NOT NULL REFERENCES product (product_id),
    order_date     DATE NOT NULL,
    quantity       INTEGER NOT NULL CHECK (quantity >= 1)
);

INSERT INTO customer (name, email, credit_limit) VALUES ('Acme Ltd', 'orders@acme.example', 5000), ('Globex', NULL, 0);
INSERT INTO product (name, price, rating) VALUES ('Widget', 9.99, 4), ('Gadget', 24.50, NULL);
INSERT INTO sales_order (customer_id, product_id, order_date, quantity) VALUES (1, 1, '2026-01-15', 10), (2, 2, '2026-02-01', 1);
