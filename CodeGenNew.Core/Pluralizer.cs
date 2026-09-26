namespace CodeGenNew.Core;

/// <summary> Best-effort English pluralization for turning a table's stem name into a route segment or a
/// list-holding variable/property name (e.g. "Movie" -> "Movies", "Holiday" -> "Holidays"). Shared by every
/// template that builds one of those from a table name, so the same rule -- and the same known limits -- apply
/// everywhere instead of being copied (and drifting) per template.
///
/// A word ending in "ss"/"x"/"z"/"ch"/"sh" (Address, ARClass, Box, Church) is always singular in English --
/// regular pluralization always adds "es" and never reproduces one of those endings a second time -- so it
/// always gets "es". A bare trailing "s" that isn't one of those endings is treated as already plural
/// (e.g. "Movies", "SettingsSales") and left alone.
///
/// Two things this deliberately does NOT attempt, because no suffix rule can derive them from spelling alone:
/// a genuinely singular word that happens to end in a bare "s" (Bus, Status, Campus) is indistinguishable from
/// an already-plural name and is left unpluralized; and true irregular plurals (child/children, ox/oxen,
/// wolf/wolves), invariant plurals (sheep, deer, fish) and foreign/Latin-derived plurals (cactus/cacti,
/// analysis/analyses) aren't in scope at all -- those need the generated name corrected by hand, the same way
/// a completely wrong route or lookup would. </summary>
public static class Pluralizer
{
    public static string Pluralize(this string word)
    {
        string lower = word.ToLowerInvariant();
        bool needsEs = lower.EndsWith("ss") || lower.EndsWith("x") || lower.EndsWith("z") || lower.EndsWith("ch") || lower.EndsWith("sh");
        return needsEs ? word + "es" : lower.EndsWith("s") ? word : word + "s";
    }
}
