namespace WinTools.Core.Brightness;

public static class MonitorNames
{
    /// <summary>
    /// Appends " (1)", " (2)", … to names that occur more than once (case-insensitive),
    /// so two identical monitor models stay distinguishable.
    /// </summary>
    public static IReadOnlyList<string> Disambiguate(IReadOnlyList<string> names)
    {
        var totals = names
            .GroupBy(n => n, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(g => g.Key, g => g.Count(), StringComparer.OrdinalIgnoreCase);
        var seen = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        var result = new List<string>(names.Count);

        foreach (var name in names)
        {
            if (totals[name] == 1)
            {
                result.Add(name);
                continue;
            }
            var ordinal = seen.GetValueOrDefault(name) + 1;
            seen[name] = ordinal;
            result.Add($"{name} ({ordinal})");
        }
        return result;
    }
}
