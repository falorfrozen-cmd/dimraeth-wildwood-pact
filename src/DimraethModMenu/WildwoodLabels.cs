using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;

namespace DimraethModMenu;

internal static class WildwoodLabels
{
    static readonly Dictionary<string, string> Names = new(StringComparer.Ordinal);
    static readonly Regex Acronyms = new("([A-Z]+)([A-Z][a-z])");
    static readonly Regex Words = new("([a-z0-9])([A-Z])");

    // Display names only; enum values and picker indices remain untouched.
    internal static string Friendly(string value)
    {
        if (string.IsNullOrEmpty(value)) return "";
        if (Names.TryGetValue(value, out string name)) return name;
        name = Words.Replace(Acronyms.Replace(value.Replace('_', ' '), "$1 $2"), "$1 $2").Trim();
        Names[value] = name;
        return name;
    }

    internal static int ActionKind(string label)
    {
        if (label.Contains("risky", StringComparison.OrdinalIgnoreCase)
            || label.StartsWith("Remove all", StringComparison.Ordinal)
            || label.StartsWith("Load checkpoint", StringComparison.Ordinal)
            || label == "Activate all quests" || label == "Skip tutorial" || label == "Complete Codex") return 2;
        if (label.StartsWith("Give selected", StringComparison.Ordinal)
            || label.StartsWith("Grant ", StringComparison.Ordinal)
            || label.StartsWith("Unlock all", StringComparison.Ordinal)
            || label == "Create checkpoint" || label == "Spawn monster near me"
            || label.StartsWith("Apply weather", StringComparison.Ordinal)) return 1;
        return 0;
    }
}
