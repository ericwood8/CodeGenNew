-- MySQL, snake case (generate with NamingStyle=Pascal). The tables of document_totals.sql in an empty database (live_totals_run.py mysql creates them again on every run):
-- mysql -h 127.0.0.1 -u <login> <database> --execute="source document_totals_mysql.sql"
DROP TABLE IF EXISTS invoice_allowance_charge;
DROP TABLE IF EXISTS invoice_line;
DROP TABLE IF EXISTS tax_category;
DROP TABLE IF EXISTS invoice;
CREATE TABLE invoice (
    invoice_id INT NOT NULL AUTO_INCREMENT PRIMARY KEY,
    customer VARCHAR(60) NOT NULL,
    invoice_date DATE NOT NULL DEFAULT '2024-06-01',
    currency VARCHAR(3) NOT NULL DEFAULT 'USD',
    items_total DECIMAL(18, 2) NOT NULL DEFAULT 0,
    allowance_total DECIMAL(18, 2) NOT NULL DEFAULT 0,
    charge_total DECIMAL(18, 2) NOT NULL DEFAULT 0,
    tax_exclusive DECIMAL(18, 2) NOT NULL DEFAULT 0,
    tax_total DECIMAL(18, 2) NOT NULL DEFAULT 0,
    tax_inclusive DECIMAL(18, 2) NOT NULL DEFAULT 0,
    prepaid DECIMAL(18, 2) NOT NULL DEFAULT 0,
    rounding DECIMAL(18, 2) NOT NULL DEFAULT 0,
    payable DECIMAL(18, 2) NOT NULL DEFAULT 0
);
CREATE TABLE tax_category (
    tax_category_id INT NOT NULL AUTO_INCREMENT PRIMARY KEY,
    code VARCHAR(10) NOT NULL,
    name VARCHAR(60) NOT NULL,
    rate DECIMAL(9, 4) NOT NULL,
    exemption_reason VARCHAR(200) NULL,
    valid_from DATE NOT NULL DEFAULT '2000-01-01',
    valid_to DATE NULL,
    UNIQUE KEY ux_tax_category_code (code)
);
CREATE TABLE invoice_line (
    invoice_line_id INT NOT NULL AUTO_INCREMENT PRIMARY KEY,
    invoice_id INT NOT NULL,
    line_date DATE NOT NULL DEFAULT '2024-06-01',
    description VARCHAR(100) NOT NULL,
    tax_category_id INT NULL,
    tax_rate DECIMAL(9, 4) NOT NULL DEFAULT 0,
    line_total DECIMAL(18, 2) NOT NULL,
    tax_amount DECIMAL(18, 2) NOT NULL DEFAULT 0,
    CONSTRAINT fk_invoice_line_invoice FOREIGN KEY (invoice_id) REFERENCES invoice (invoice_id),
    CONSTRAINT fk_invoice_line_category FOREIGN KEY (tax_category_id) REFERENCES tax_category (tax_category_id)
);
CREATE TABLE invoice_allowance_charge (
    invoice_allowance_charge_id INT NOT NULL AUTO_INCREMENT PRIMARY KEY,
    invoice_id INT NOT NULL,
    is_charge TINYINT(1) NOT NULL,
    reason_code VARCHAR(10) NULL,
    base_amount DECIMAL(18, 2) NULL,
    percentage DECIMAL(9, 4) NULL,
    amount DECIMAL(18, 2) NOT NULL,
    tax_category_id INT NULL,
    CONSTRAINT fk_invoice_allowance_charge_invoice FOREIGN KEY (invoice_id) REFERENCES invoice (invoice_id),
    CONSTRAINT fk_invoice_allowance_charge_category FOREIGN KEY (tax_category_id) REFERENCES tax_category (tax_category_id)
);
