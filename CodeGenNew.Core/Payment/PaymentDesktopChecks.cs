using CodeGenNew.Core.Settings;

namespace CodeGenNew.Core.Payment;

/// <summary> The rules a WinUI3 dialog runs before it saves a row of a bank account table (<c>BankAccountTables</c>) or of a payment means table (<c>PaymentTermsTables</c>): the same IBAN and routing number checks as
/// <see cref="PaymentWriter.AccountCode"/> and the same due date as <see cref="PaymentWriter.DueCode"/>, since the dialog writes through its own context and not through the API. Each text is the body of a block
/// (eight spaces in) in a method that has <c>entity</c> and returns <c>false</c> with <c>ErrorMessage</c> set to refuse the save; empty when the table is not listed. </summary>
public static class PaymentDesktopChecks
{
    private static string Refuse(string message) => $"            ErrorMessage = \"{message}\";\n            return false;\n";

    private static string Store(ColumnModel column, string expression) => column.IsNullable ? expression : expression + " ?? \"\"";

    public static string AccountCheck(TableModel table, ProjectSettings project)
    {
        if (PaymentPlan.BankFor(table, project) is not { } s)
            return "";
        var o = new System.Text.StringBuilder();
        o.Append("        {\n");
        o.Append("            var ibanLengths = \"" + PaymentPlan.IbanLengths + "\".Split(' ').ToDictionary(x => x[..2], x => int.Parse(x[2..]));\n");
        o.Append("""
            bool IbanValid(string value)
            {
                if (value.Length < 5 || !ibanLengths.TryGetValue(value[..2], out int length) || value.Length != length || !value.All(char.IsAsciiLetterOrDigit) || !char.IsAsciiLetter(value[0]) || !char.IsAsciiLetter(value[1]) || !char.IsAsciiDigit(value[2]) || !char.IsAsciiDigit(value[3]))
                    return false;
                int remainder = 0;
                foreach (char c in value[4..] + value[..4])
                    foreach (char digit in char.IsAsciiDigit(c) ? c.ToString() : (c - 'A' + 10).ToString())
                        remainder = (remainder * 10 + (digit - '0')) % 97;
                return remainder == 1;
            }
            bool RoutingValid(string value)
            {
                if (value.Length != 9 || !value.All(char.IsAsciiDigit))
                    return false;
                int[] d = value.Select(c => c - '0').ToArray();
                return (3 * (d[0] + d[3] + d[6]) + 7 * (d[1] + d[4] + d[7]) + (d[2] + d[5] + d[8])) % 10 == 0;
            }

""");
        if (s.Iban is { } iban)
        {
            string name = iban.CSharpName();
            o.Append($"            string? iban = entity.{name} is null ? null : new string(entity.{name}.Where(c => c != ' ' && c != '-').ToArray()).ToUpperInvariant();\n");
            o.Append("            if (string.IsNullOrEmpty(iban))\n                iban = null;\n");
            o.Append($"            entity.{name} = {Store(iban, "iban")};\n");
            o.Append("            if (iban is not null && !IbanValid(iban))\n            {\n" + Refuse(PaymentWords.IbanBad) + "            }\n");
        }
        else
            o.Append("            string? iban = null;\n");
        if (s.Account is { } account && s.Routing is { } routing)
        {
            string a = account.CSharpName(), r = routing.CSharpName();
            o.Append($"            string? account = string.IsNullOrWhiteSpace(entity.{a}) ? null : entity.{a}.Trim();\n            string? routing = string.IsNullOrWhiteSpace(entity.{r}) ? null : entity.{r}.Trim();\n");
            o.Append($"            entity.{a} = {Store(account, "account")};\n            entity.{r} = {Store(routing, "routing")};\n");
            o.Append("            if (routing is not null && !RoutingValid(routing))\n            {\n" + Refuse(PaymentWords.RoutingBad) + "            }\n");
            o.Append("            if (account is not null && routing is not null && (account.Length is < 4 or > 17 || !account.All(char.IsAsciiDigit)))\n            {\n" + Refuse(PaymentWords.AccountNumberBad) + "            }\n");
            o.Append("            if (iban is null && (account is null || routing is null))\n            {\n" + Refuse(PaymentWords.AccountMissing) + "            }\n");
        }
        else
            o.Append("            if (iban is null)\n            {\n" + Refuse(PaymentWords.AccountMissing) + "            }\n");
        o.Append("        }\n");
        return o.ToString();
    }

    public static string DueCheck(TableModel table, ProjectSettings project, string context)
    {
        if (PaymentPlan.DueFor(table, project) is not { } s)
            return "";
        string doc = s.Document.CSharpName(), date = s.DocumentDate.CSharpName(), due = s.Due.CSharpName(), terms = s.Terms.CSharpName(), days = s.Days.CSharpName();
        string documents = s.DocumentTable.TableName, termsTable = s.TermsTable.TableName, documentKey = s.DocumentKey.CSharpName(), termsKey = s.TermsKey.CSharpName();
        var o = new System.Text.StringBuilder();
        o.Append("        {\n");
        o.Append($"            var pdDocument = await {context}.Set<{documents}>().AsNoTracking().Where(d => d.{documentKey} == entity.{doc}).Select(d => new {{ d.{date} }}).FirstOrDefaultAsync();\n");
        o.Append($"            if (pdDocument is not null{(s.DocumentDate.IsNullable ? " && pdDocument." + date + " is not null" : "")})\n            {{\n");
        o.Append($"                DateTime pdStart = pdDocument.{date}{(s.DocumentDate.IsNullable ? ".Value" : "")}.Date;\n");
        o.Append($"                if (entity.{due} is null)\n                {{\n");
        o.Append($"                    var pdTermsKey = entity.{terms};\n");
        o.Append("                    if (pdTermsKey is not null)\n                    {\n");
        o.Append($"                        var pdFound = await {context}.Set<{termsTable}>().AsNoTracking().Where(t => t.{termsKey} == pdTermsKey).Select(t => new {{ t.{days} }}).FirstOrDefaultAsync();\n");
        o.Append($"                        if (pdFound is null)\n                        {{\n                            ErrorMessage = \"{PaymentWords.TermsMissing}\";\n                            return false;\n                        }}\n");
        o.Append($"                        entity.{due} = pdStart.AddDays(pdFound.{days});\n");
        o.Append("                    }\n                }\n");
        o.Append($"                else if (entity.{due}.Value.Date < pdStart)\n                {{\n                    ErrorMessage = \"{PaymentWords.DueBeforeDocument}\";\n                    return false;\n                }}\n");
        o.Append("            }\n        }\n");
        return o.ToString();
    }
}
