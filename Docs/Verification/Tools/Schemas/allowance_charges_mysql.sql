-- MySQL. Allowances and charges on a SalesInvoice for AllowanceCharges=InvoiceAllowanceCharge:IsCharge:BaseAmount:Percentage:Amount:SalesInvoiceId (the invoicesystem copy, snake case; generate with NamingStyle=Pascal).
CREATE TABLE IF NOT EXISTS invoice_allowance_charge (
    invoice_allowance_charge_id INT NOT NULL AUTO_INCREMENT PRIMARY KEY,
    sales_invoice_id INT NOT NULL,
    is_charge TINYINT(1) NOT NULL DEFAULT 0,
    reason_code VARCHAR(10) NULL,
    base_amount DECIMAL(18, 2) NULL,
    percentage DECIMAL(9, 4) NULL,
    amount DECIMAL(18, 2) NOT NULL,
    CONSTRAINT fk_invoice_allowance_charge_invoice FOREIGN KEY (sales_invoice_id) REFERENCES sales_invoice (sales_invoice_id)
);
