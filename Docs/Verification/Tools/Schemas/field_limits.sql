CREATE TABLE Payslip (
    PayslipId INTEGER PRIMARY KEY AUTOINCREMENT,
    Employee VARCHAR(40) NOT NULL,
    MonthNumber INTEGER NOT NULL,
    PayYear INTEGER NOT NULL,
    Bonus DECIMAL(18,2) NOT NULL,
    Hours INTEGER NOT NULL,
    BornOn DATE NOT NULL
);

INSERT INTO Payslip (Employee, MonthNumber, PayYear, Bonus, Hours, BornOn) VALUES ('Ada', 3, 2024, 120.50, 160, '1990-05-04');
INSERT INTO Payslip (Employee, MonthNumber, PayYear, Bonus, Hours, BornOn) VALUES ('Old', 14, 2024, -5.00, 100, '1980-01-02');
