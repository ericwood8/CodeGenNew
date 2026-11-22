namespace CodeGenNew.Core.Totals;

/// <summary> The rounding steps and the decimal places of <see cref="DocumentTotalsPlan"/> split into the rules of one currency and the rule of every other currency (<see cref="DocumentTotalsWords.AnyCurrency"/>),
/// which the writers of the three stacks turn into a lookup. </summary>
public static class DocumentTotalsCurrencyRules
{
    public static (IReadOnlyList<DocumentTotalsRound> Specific, DocumentTotalsRound? Any) Split(IEnumerable<DocumentTotalsRound>? rounds)
    {
        var all = (rounds ?? []).ToList();
        return (all.Where(r => r.Currency != DocumentTotalsWords.AnyCurrency).ToList(), all.FirstOrDefault(r => r.Currency == DocumentTotalsWords.AnyCurrency));
    }

    public static (IReadOnlyList<DocumentTotalsPlaces> Specific, DocumentTotalsPlaces? Any) Split(IEnumerable<DocumentTotalsPlaces>? places)
    {
        var all = (places ?? []).ToList();
        return (all.Where(r => r.Currency != DocumentTotalsWords.AnyCurrency).ToList(), all.FirstOrDefault(r => r.Currency == DocumentTotalsWords.AnyCurrency));
    }
}
