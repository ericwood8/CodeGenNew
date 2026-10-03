# CS_Enum_v1

The full design notes that used to head the template. The template keeps a short summary.

```text
Generates: <TableName>.cs   (see OutputName in CS_Enum_v1.tt.config)

Turns a small lookup table (ClearanceType, SY_Role ...) into a C# enum whose members are the table's ROWS, so code can
say SY_RequestStatusType.Pending instead of a magic 1. It reads the table's data (NeedsRowData=true, section 5.3):
    Id  Name                 ->   public enum ClearanceType { PublicTrust = 1, Secret = 2, TopSecret = 3 }
    1   Public Trust
    ...
  - the enum's name is the table's name;
  - each member's VALUE is the primary key (a single int/smallint/tinyint/bigint column; the enum's underlying type
    follows the key's type, and is left off for int);
  - each member's NAME is the row's name column - the column called "Name", else "<Table>Name" (Company has
    CompanyName, SY_Role has RoleName), else the best display column from SpecialLogicColumns.config, else any text
    column ending in "Name" - with the spaces and punctuation taken out and each word
    starting with a capital: "Paid time off" -> PaidTimeOff, "Family and Medical Leave Act" ->
    FamilyAndMedicalLeaveAct. A name that would not be a legal C# identifier (starts with a digit, or
    repeats an earlier member) is made legal (_2Fast, Name_3). Every member starts with a capital, so a C# keyword never clashes.
  Rows come out in key order. An EMPTY table is refused (an enum with no members is useless): load the data, then generate.

Only what the DATA says is generated. A member the developers abbreviated by hand (OvertimeType.RegularOT for
"Regular overtime") comes out as RegularOvertime, so regenerating over a hand-tuned enum renames it.
```
