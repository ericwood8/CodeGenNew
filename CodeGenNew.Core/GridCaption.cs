namespace CodeGenNew.Core;

/// <summary> The caption a grid column gets from its already-spaced words ("Item Number" -> "Item #", "Discount Percentage" -> "Discount %"):
/// "Number" and "Num" are written as #, "Percent", "Percentage" and "Pct" as %, so a grid with many columns stays narrow. Only whole words
/// are replaced; forms keep the full words. Also: a trailing "Name", "Description" or "Short Descr" is dropped ("Item Description" -> "Item",
/// "Customer Name" -> "Customer"), "XXXXX Status" becomes "Status", and "Alternate" is abbreviated "Alt." ("Alt. Name" keeps its Name). </summary>
public static class GridCaption
{
    /// <summary> The caption for a column's grid header: <see cref="From"/>, plus the rules that depend on the column's type. A date column drops the word "Date"
    /// ("Date Added" -> "Added", "Invoice Date" -> "Invoice"; the data shows it is a date) unless that would leave nothing; a yes/no column loses a leading "Is" and ends in "?" ("Is Closed" -> "Closed?"). </summary>
    public static string For(ColumnModel column, string spacedWords)
    {
        string words = spacedWords;
        if (column.SqlType is System.Data.SqlDbType.Date or System.Data.SqlDbType.DateTime or System.Data.SqlDbType.DateTime2 or System.Data.SqlDbType.SmallDateTime)
        {
            var kept = words.Split(' ', StringSplitOptions.RemoveEmptyEntries).Where(w => !w.Equals("Date", StringComparison.OrdinalIgnoreCase)).ToArray();
            if (kept.Length > 0)
                words = string.Join(' ', kept);
        }
        string caption = From(words);
        if (column.SqlType == System.Data.SqlDbType.Bit)
        {
            // "Is Taxable" -> "Taxable?": the question mark already says it is a yes/no, so a leading "Is" is dropped.
            if (caption.StartsWith("Is ", StringComparison.OrdinalIgnoreCase) && caption.Length > 3)
                caption = caption[3..];
            if (!caption.EndsWith('?'))
                caption += "?";
        }
        return caption;
    }

    public static string From(string spacedWords)
    {
        var words = spacedWords.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        bool abbreviated = false;
        for (int i = 0; i < words.Length; i++)
        {
            if (words[i].Equals("Alternate", StringComparison.OrdinalIgnoreCase))
            {
                words[i] = "Alt.";
                abbreviated = true;
            }
            else
            if (words[i].Equals("Number", StringComparison.OrdinalIgnoreCase) || words[i].Equals("Num", StringComparison.OrdinalIgnoreCase))
                words[i] = "#";
            else if (words[i].Equals("Percentage", StringComparison.OrdinalIgnoreCase) || words[i].Equals("Percent", StringComparison.OrdinalIgnoreCase)
                     || words[i].Equals("Pct", StringComparison.OrdinalIgnoreCase))
                words[i] = "%";
        }
        var list = words.ToList();
        if (!abbreviated && list.Count > 1)
        {
            string last = list[^1];
            if (last.Equals("Name", StringComparison.OrdinalIgnoreCase) || last.Equals("Description", StringComparison.OrdinalIgnoreCase)
                || last.Equals("Descr", StringComparison.OrdinalIgnoreCase))
            {
                // "Item Description" -> "Item", "Customer Name" -> "Customer", "Item Short Descr" -> "Item": the column's own words are enough.
                list.RemoveAt(list.Count - 1);
                if (list.Count > 1 && list[^1].Equals("Short", StringComparison.OrdinalIgnoreCase))
                    list.RemoveAt(list.Count - 1);
            }
            else if (last.Equals("Status", StringComparison.OrdinalIgnoreCase))
            {
                // "Item Status" -> "Status", "Customer Status" -> "Status".
                list = ["Status"];
            }
        }
        return string.Join(' ', list);
    }
}
