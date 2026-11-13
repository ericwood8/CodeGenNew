-- The tables of snapshot.sql for the standing MySQL invoicesystem database (it has none of these names), for
-- SnapshotColumns=OrderItem:ProductName<-Product.Name+UnitPrice<-Product.Price+TaxRate<-TaxCategory.Rate:LineTotal=Quantity*UnitPrice (generate with --only Product,TaxCategory,SalesOrder,OrderItem).
-- MYSQL_PWD=... mysql -h 127.0.0.1 -u <login> invoicesystem < snapshot_mysql.sql      Can be run again.
CREATE TABLE IF NOT EXISTS product (
    product_id INT NOT NULL AUTO_INCREMENT PRIMARY KEY,
    name VARCHAR(100) NOT NULL,
    price DECIMAL(10, 2) NOT NULL,
    sku VARCHAR(30) NULL,
    description VARCHAR(200) NULL,
    unit VARCHAR(20) NULL
);
CREATE TABLE IF NOT EXISTS tax_category (
    tax_category_id INT NOT NULL AUTO_INCREMENT PRIMARY KEY,
    name VARCHAR(60) NOT NULL,
    rate DECIMAL(5, 2) NOT NULL,
    code VARCHAR(20) NULL,
    notes VARCHAR(100) NULL,
    region VARCHAR(40) NULL
);
CREATE TABLE IF NOT EXISTS sales_order (
    sales_order_id INT NOT NULL AUTO_INCREMENT PRIMARY KEY,
    reference VARCHAR(40) NOT NULL,
    customer VARCHAR(100) NOT NULL,
    note VARCHAR(200) NULL,
    channel VARCHAR(30) NULL,
    remarks VARCHAR(100) NULL
);
CREATE TABLE IF NOT EXISTS order_item (
    order_item_id INT NOT NULL AUTO_INCREMENT PRIMARY KEY,
    sales_order_id INT NOT NULL,
    product_id INT NOT NULL,
    tax_category_id INT NULL,
    product_name VARCHAR(100) NOT NULL,
    unit_price DECIMAL(10, 2) NOT NULL,
    tax_rate DECIMAL(5, 2) NULL,
    quantity INT NOT NULL,
    line_total DECIMAL(12, 2) NOT NULL,
    CONSTRAINT fk_order_item_sales_order FOREIGN KEY (sales_order_id) REFERENCES sales_order (sales_order_id),
    CONSTRAINT fk_order_item_product FOREIGN KEY (product_id) REFERENCES product (product_id),
    CONSTRAINT fk_order_item_tax_category FOREIGN KEY (tax_category_id) REFERENCES tax_category (tax_category_id)
);
