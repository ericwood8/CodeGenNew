namespace CodeGenNew.Core;

/// <summary> The caption a grid column gets from its already-spaced words ("Item Number" -> "Item #", "Discount Percentage" -> "Discount %"):
/// "Number" and "Num" are written as #, "Percent", "Percentage" and "Pct" as %, so a grid with many columns stays narrow. Only whole words
/// are replaced; forms keep the full words. Also: a trailing "Name", "Description" or "Short Descr" is dropped ("Item Description" -> "Item",
/// "Customer Name" -> "Customer"), "XXXXX Status" becomes "Status", and "Alternate" is abbreviated "Alt." ("Alt. Name" keeps its Name).
/// Also Min., Max., Bal., Amt., Mthly., Avg., Admin., Whse., Qty., Acct., Addr., Desc., Pmt., Ref., Dept., Mgr., Tel., Doc., Info., Prev., Orig., Est., Eff., Exp., Alloc., Adj. and Beg. for the whole words, and "Per" between two words is "/" ("Price Per Month" -> "Price/Month"). </summary>
public static class GridCaption
{
    /// <summary> The caption for a column's grid header: <see cref="From"/>, plus the rules that depend on the column's type. A date column drops the word "Date"
    /// ("Date Added" -> "Added", "Invoice Date" -> "Invoice"; the data shows it is a date) unless that would leave nothing; a yes/no column loses a leading "Is" and ends in "?" ("Is Closed" -> "Closed?"). </summary>
    public static string For(ColumnModel column, string spacedWords)
    {
        string words = spacedWords;
        if (column.IsDateColumn)
        {
            var kept = words.Split(' ', StringSplitOptions.RemoveEmptyEntries).Where(w => !w.EqualsIgnoreCase("Date")).ToArray();
            if (kept.Length > 0)
                words = string.Join(' ', kept);
        }
        string caption = From(words);
        if (column.IsBooleanColumn)
        {
            // "Is Taxable" -> "Taxable?": the question mark already says it is a yes/no, so a leading "Is" is dropped.
            if (caption.StartsWithIgnoreCase("Is ") && caption.Length > 3)
                caption = caption[3..];
            if (!caption.EndsWith('?'))
                caption = caption.TrimEnd('.') + "?";   // "Admin." -> "Admin?": the question mark ends the abbreviation
        }
        return caption;
    }

    /// <summary> The whole words a grid caption shortens, singular and plural. </summary>
    private static readonly Dictionary<string, string> Short = new(StringComparer.OrdinalIgnoreCase)
    {
        ["Minimum"] = "Min.", ["Maximum"] = "Max.", ["Balance"] = "Bal.", ["Balances"] = "Bals.", ["Amount"] = "Amt.", ["Amounts"] = "Amts.",
        ["Monthly"] = "Mthly.", ["Average"] = "Avg.", ["Admin"] = "Admin.", ["Administrator"] = "Admin.", ["Administrators"] = "Admins.",
        ["Warehouse"] = "Whse.", ["Warehouses"] = "Whses.",
        ["Quantity"] = "Qty.", ["Quantities"] = "Qtys.", ["Account"] = "Acct.", ["Accounts"] = "Accts.", ["Address"] = "Addr.", ["Addresses"] = "Addrs.",
        ["Payment"] = "Pmt.", ["Payments"] = "Pmts.", ["Reference"] = "Ref.", ["Department"] = "Dept.", ["Departments"] = "Depts.",
        ["Manager"] = "Mgr.", ["Managers"] = "Mgrs.", ["Telephone"] = "Tel.", ["Document"] = "Doc.", ["Documents"] = "Docs.", ["Information"] = "Info.",
        ["Previous"] = "Prev.", ["Original"] = "Orig.", ["Estimated"] = "Est.", ["Effective"] = "Eff.", ["Expiration"] = "Exp.", ["Allocation"] = "Alloc.",
        ["Adjustment"] = "Adj.", ["Beginning"] = "Beg.",
        // the short forms a column name already carries get the same period, so "Total Amt" and "Total Amount" read alike
        ["Amt"] = "Amt.", ["Qty"] = "Qty.", ["Bal"] = "Bal.", ["Avg"] = "Avg.", ["Min"] = "Min.", ["Max"] = "Max.", ["Whse"] = "Whse.", ["Addr"] = "Addr.",
        ["Acct"] = "Acct.", ["Dept"] = "Dept.", ["Mgr"] = "Mgr.", ["Pmt"] = "Pmt.", ["Ref"] = "Ref.", ["Doc"] = "Doc.", ["Tel"] = "Tel."
    };

    /// <summary> The short form of a word that ends in digits ("Address1" -> "Addr1"; the period would sit in the middle), else null. </summary>
    private static string? ShortWithDigits(string word)
    {
        int i = word.Length;
        while (i > 1 && char.IsDigit(word[i - 1]))
            i--;
        return i < word.Length && Short.TryGetValue(word[..i], out string? s) ? s.TrimEnd('.') + word[i..] : null;
    }

    public static string From(string spacedWords)
    {
        var words = spacedWords.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        bool abbreviated = false;
        for (int i = 0; i < words.Length; i++)
        {
            if (words[i].EqualsIgnoreCase("Alternate"))
            {
                words[i] = "Alt.";
                abbreviated = true;
            }
            else if (words[i].EqualsIgnoreCase("Description") && i < words.Length - 1)
                words[i] = "Desc.";   // a last "Description" is dropped below instead
            else if (Short.TryGetValue(words[i], out string? shortWord))
                words[i] = shortWord;
            else if (ShortWithDigits(words[i]) is { } withDigits)
                words[i] = withDigits;
            else if (words[i].EqualsIgnoreCase("Per") && i > 0 && i < words.Length - 1)
                words[i] = "/";
            else
            if (words[i].EqualsIgnoreCase("Number") || words[i].EqualsIgnoreCase("Num"))
                words[i] = "#";
            else if (words[i].EqualsIgnoreCase("Percentage") || words[i].EqualsIgnoreCase("Percent")
                     || words[i].EqualsIgnoreCase("Pct"))
                words[i] = "%";
        }
        var list = words.ToList();
        if (!abbreviated && list.Count > 1)
        {
            string last = list[^1];
            if (last.EqualsIgnoreCase("Name") || last.EqualsIgnoreCase("Description")
                || last.EqualsIgnoreCase("Descr"))
            {
                // "Item Description" -> "Item", "Customer Name" -> "Customer", "Item Short Descr" -> "Item": the column's own words are enough.
                list.RemoveAt(list.Count - 1);
                if (list.Count > 1 && list[^1].EqualsIgnoreCase("Short"))
                    list.RemoveAt(list.Count - 1);
            }
            else if (last.EqualsIgnoreCase("Status"))
            {
                // "Item Status" -> "Status", "Customer Status" -> "Status".
                list = ["Status"];
            }
        }
        // "Price / Month" is written "Price/Month": the slash takes no spaces.
        return string.Join(' ', list).Replace(" / ", "/");
    }
}
