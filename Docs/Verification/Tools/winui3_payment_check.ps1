param([Parameter(Mandatory)][string]$Exe, [Parameter(Mandatory)][string]$SqliteFile)
# Clicks through the Add dialogs of the payment tables in a generated WinUI3 app over a scratch copy of Schemas/payments.sql (BankAccountTables=PartyBankAccount,
# InstallmentTables=PaymentInstallment:PayDocumentId:PayDocument:Total, PaymentTermsTables=PaymentMeans:PayDocumentId:PayDocument:OrderedOn), with UI Automation:
#   python payment_desktop_seed.py <scratch sqlite file>
#   powershell -File winui3_payment_check.ps1 -Exe <app.exe> -SqliteFile <scratch sqlite file>
# Checks that the bank account dialog refuses a bad IBAN and stores a good one without spaces and in upper case, that the installment dialog refuses an installment that takes the schedule over the total of the
# document, and that the payment means dialog works out the due date of a row that has terms and none (the document date plus the days to pay) and refuses a due date before the document.
Import-Module C:\Github\CodeGenNew\Docs\Verification\UiAutomation.psm1 -Force -WarningAction Ignore
$ErrorActionPreference = 'Stop'

function Names { (List-Names | Select-Object -First 140) -join ' | ' }
function Rows($sql) { (python -c "import sqlite3,sys; print(sqlite3.connect(sys.argv[1]).execute(sys.argv[2]).fetchall())" $SqliteFile $sql) }
function Dialog { @(Find-Like 'Add *' | Where-Object { $_.Current.ControlType.ProgrammaticName -eq 'ControlType.Window' })[0] }
function Fill($label, $value) {
    $box = @(Find-Type 'Edit' (Dialog) | Where-Object { $_.Current.Name -like $label })[0]
    $box.SetFocus(); Start-Sleep -Milliseconds 300
    Set-Text $box $value
}
function Choose($label, $downs) {   # the drop-down's items are view model rows, so the choice is made with the Down key
    $combo = @(Find-Type 'ComboBox' (Dialog) | Where-Object { $_.Current.Name -like $label })[0]
    $combo.SetFocus(); Start-Sleep -Milliseconds 400
    for ($i = 0; $i -lt $downs; $i++) { Send-Keys '{DOWN}'; Start-Sleep -Milliseconds 400 }
}
function Save { (Find-Name 'Save').SetFocus(); Start-Sleep -Milliseconds 500; Invoke-El (Find-Name 'Save'); Start-Sleep -Seconds 3 }   # a text box writes its value back when it loses focus
function Open-Add($menu) { Select-El (Find-Name $menu); Start-Sleep -Seconds 4; Invoke-El (Find-Name 'Add New'); Start-Sleep -Seconds 3 }

Start-App -Path $Exe | Out-Null
try {
    Start-Sleep -Seconds 5

    Open-Add 'Party Bank Account'
    Fill 'Account Holder*' 'Holder'
    Fill 'Iban' 'DE89370400440532013001'
    Save
    $page = Names
    Assert-That (($page -match 'Window: Add Party Bank Account') -and ($page -match 'The IBAN is not valid')) "a bad IBAN is refused in words and the dialog stays open ($page)"
    Fill 'Iban' 'de89 3704 0044 0532 0130 00'
    Save
    Assert-That ((Names) -notmatch 'Window: Add Party Bank Account') 'a good IBAN saves'
    Assert-That ((Rows 'select Iban from PartyBankAccount') -match 'DE89370400440532013000') "the IBAN is stored without spaces and in upper case ($(Rows 'select Iban from PartyBankAccount'))"

    Open-Add 'Party Bank Account'
    Fill 'Account Holder*' 'Nobody'
    Save
    $page = Names
    Assert-That ($page -match 'Give an IBAN, or an account number with a routing number') "a row with neither form is refused ($page)"
    Invoke-El (Find-Name 'Cancel'); Start-Sleep -Seconds 1

    Open-Add 'Payment Means'
    Choose 'Pay Document*' 1
    Choose 'Payment Terms*' 2
    Fill 'Means Code*' 'transfer'
    Save
    Assert-That ((Names) -notmatch 'Window: Add Payment Means') "a row with terms and no due date saves ($(Names))"
    Assert-That ((Rows 'select DueDate from PaymentMeans') -match '2027-03-31') "the due date is the document date plus the days to pay ($(Rows 'select DueDate from PaymentMeans'))"
} finally { Stop-App }
if ($script:Failed) { exit 1 }
