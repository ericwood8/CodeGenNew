-- The tables of payments.sql for MySQL in snake case, for BankAccountTables, InstallmentTables and PaymentTermsTables (generate with OnlyTables=pay_document,payment_terms,party_bank_account,payment_means,payment_installment and NamingStyle=Pascal).
-- MYSQL_PWD=... mysql -h 127.0.0.1 -u <login> <database> < payments_mysql.sql      Can be run again.
CREATE TABLE IF NOT EXISTS pay_document (
    pay_document_id INT NOT NULL AUTO_INCREMENT PRIMARY KEY,
    number VARCHAR(20) NOT NULL,
    ordered_on DATE NOT NULL,
    total DECIMAL(12, 2) NOT NULL
);
CREATE TABLE IF NOT EXISTS payment_terms (
    payment_terms_id INT NOT NULL AUTO_INCREMENT PRIMARY KEY,
    code VARCHAR(12) NOT NULL UNIQUE,
    name VARCHAR(80) NOT NULL,
    days_to_pay INT NOT NULL,
    early_discount_percent DECIMAL(5, 2) NULL,
    early_discount_days INT NULL,
    late_penalty_percent DECIMAL(5, 2) NULL
);
CREATE TABLE IF NOT EXISTS party_bank_account (
    party_bank_account_id INT NOT NULL AUTO_INCREMENT PRIMARY KEY,
    account_holder VARCHAR(100) NOT NULL,
    iban VARCHAR(34) NULL,
    account_number VARCHAR(20) NULL,
    routing_number VARCHAR(12) NULL,
    bank_name VARCHAR(100) NULL
);
CREATE TABLE IF NOT EXISTS payment_means (
    payment_means_id INT NOT NULL AUTO_INCREMENT PRIMARY KEY,
    pay_document_id INT NOT NULL,
    payment_terms_id INT NULL,
    means_code VARCHAR(14) NOT NULL,
    due_date DATE NULL,
    CONSTRAINT fk_payment_means_document FOREIGN KEY (pay_document_id) REFERENCES pay_document (pay_document_id),
    CONSTRAINT fk_payment_means_terms FOREIGN KEY (payment_terms_id) REFERENCES payment_terms (payment_terms_id)
);
CREATE TABLE IF NOT EXISTS payment_installment (
    payment_installment_id INT NOT NULL AUTO_INCREMENT PRIMARY KEY,
    pay_document_id INT NOT NULL,
    sequence_no INT NOT NULL,
    due_date DATE NOT NULL,
    amount DECIMAL(12, 2) NOT NULL,
    CONSTRAINT fk_payment_installment_document FOREIGN KEY (pay_document_id) REFERENCES pay_document (pay_document_id),
    UNIQUE KEY ux_payment_installment_sequence (pay_document_id, sequence_no)
);
CREATE TABLE IF NOT EXISTS pay_transaction (
    pay_transaction_id INT NOT NULL AUTO_INCREMENT PRIMARY KEY,
    pay_document_id INT NOT NULL,
    kind VARCHAR(12) NOT NULL,
    status VARCHAR(12) NOT NULL,
    amount DECIMAL(12, 2) NOT NULL,
    currency_code CHAR(3) NOT NULL DEFAULT 'USD',
    gateway_id VARCHAR(100) NOT NULL,
    idempotency_key VARCHAR(100) NOT NULL,
    payload TEXT NULL,
    created_on DATETIME NOT NULL DEFAULT CURRENT_TIMESTAMP,
    CONSTRAINT fk_pay_transaction_document FOREIGN KEY (pay_document_id) REFERENCES pay_document (pay_document_id),
    UNIQUE KEY ux_pay_transaction_gateway (gateway_id),
    UNIQUE KEY ux_pay_transaction_key (idempotency_key)
);
