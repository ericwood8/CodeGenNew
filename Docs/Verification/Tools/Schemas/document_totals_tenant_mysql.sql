-- MySQL, snake case (generate with NamingStyle=Pascal). document_totals_mysql.sql with tenant-limited invoices, lines and allowance rows (a TenantId on each) and the user tables of an ApiAuth project.
-- The tenant table keeps IsActive in camel case: the generated base class of a name/active table maps Name and IsActive by those names, also in a snake case database.
-- Made again on every run of live_totals_scoped_run.py.
DROP TABLE IF EXISTS invoice_allowance_charge;
DROP TABLE IF EXISTS invoice_line;
DROP TABLE IF EXISTS tax_category;
DROP TABLE IF EXISTS invoice;
DROP TABLE IF EXISTS employee_tenant;
DROP TABLE IF EXISTS employee;
DROP TABLE IF EXISTS tenant;
CREATE TABLE tenant (
    tenant_id INT NOT NULL AUTO_INCREMENT PRIMARY KEY,
    name VARCHAR(80) NOT NULL,
    slug VARCHAR(40) NOT NULL,
    IsActive TINYINT(1) NOT NULL DEFAULT 1,
    created_on DATETIME NOT NULL,
    UNIQUE KEY ux_tenant_slug (slug)
);
CREATE TABLE employee (
    employee_id INT NOT NULL AUTO_INCREMENT PRIMARY KEY,
    full_name VARCHAR(40) NOT NULL,
    login VARCHAR(30) NOT NULL,
    password_hash VARCHAR(200) NOT NULL DEFAULT 'x',
    is_manager TINYINT(1) NOT NULL DEFAULT 0,
    is_administrator TINYINT(1) NOT NULL DEFAULT 0
);
CREATE TABLE employee_tenant (
    employee_tenant_id INT NOT NULL AUTO_INCREMENT PRIMARY KEY,
    employee_id INT NOT NULL,
    tenant_id INT NOT NULL,
    role VARCHAR(30) NULL,
    CONSTRAINT fk_employee_tenant_employee FOREIGN KEY (employee_id) REFERENCES employee (employee_id),
    CONSTRAINT fk_employee_tenant_tenant FOREIGN KEY (tenant_id) REFERENCES tenant (tenant_id)
);
CREATE TABLE invoice (
    invoice_id INT NOT NULL AUTO_INCREMENT PRIMARY KEY,
    tenant_id INT NOT NULL,
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
    tenant_id INT NOT NULL,
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
    tenant_id INT NOT NULL,
    invoice_id INT NOT NULL,
    is_charge TINYINT(1) NOT NULL,
    reason_code VARCHAR(10) NULL,
    base_amount DECIMAL(18, 2) NULL,
    percentage DECIMAL(9, 4) NULL,
    amount DECIMAL(18, 2) NOT NULL,
    tax_category_id INT NULL,
    tax_rate DECIMAL(9, 4) NOT NULL DEFAULT 0,
    tax_amount DECIMAL(18, 2) NOT NULL DEFAULT 0,
    CONSTRAINT fk_invoice_allowance_charge_invoice FOREIGN KEY (invoice_id) REFERENCES invoice (invoice_id),
    CONSTRAINT fk_invoice_allowance_charge_category FOREIGN KEY (tax_category_id) REFERENCES tax_category (tax_category_id)
);
ALTER TABLE invoice ADD CONSTRAINT fk_invoice_tenant FOREIGN KEY (tenant_id) REFERENCES tenant (tenant_id);
ALTER TABLE invoice_line ADD CONSTRAINT fk_invoice_line_tenant FOREIGN KEY (tenant_id) REFERENCES tenant (tenant_id);
ALTER TABLE invoice_allowance_charge ADD CONSTRAINT fk_invoice_allowance_charge_tenant FOREIGN KEY (tenant_id) REFERENCES tenant (tenant_id);
