-- The tables of tax_lines.sql for MySQL under other names, snake case (generate with NamingStyle=Pascal and OnlyTables=LevyCategory,LevyLine), for
-- TaxLines=LevyLine:TaxCategoryId:TaxRate:LineTotal:TaxAmount:LevyCategory:Rate:ValidFrom:ValidTo:LineDate:InvoiceId:Code
-- MYSQL_PWD=... mysql -h 127.0.0.1 -u <login> <database> < tax_lines_mysql.sql      Can be run again.
CREATE TABLE IF NOT EXISTS levy_category (
    tax_category_id INT NOT NULL AUTO_INCREMENT PRIMARY KEY,
    code VARCHAR(10) NOT NULL,
    name VARCHAR(60) NOT NULL,
    rate DECIMAL(9, 4) NOT NULL,
    exemption_reason VARCHAR(200) NULL,
    valid_from DATE NOT NULL DEFAULT '2000-01-01',
    valid_to DATE NULL,
    UNIQUE KEY ux_levy_category_code (code)
);
CREATE TABLE IF NOT EXISTS levy_line (
    invoice_line_id INT NOT NULL AUTO_INCREMENT PRIMARY KEY,
    invoice_id INT NOT NULL DEFAULT 1,
    line_date DATE NOT NULL DEFAULT '2024-06-01',
    description VARCHAR(100) NOT NULL,
    tax_category_id INT NULL,
    tax_rate DECIMAL(9, 4) NOT NULL DEFAULT 0,
    line_total DECIMAL(18, 2) NOT NULL,
    tax_amount DECIMAL(18, 2) NOT NULL DEFAULT 0,
    CONSTRAINT fk_levy_line_category FOREIGN KEY (tax_category_id) REFERENCES levy_category (tax_category_id)
);
