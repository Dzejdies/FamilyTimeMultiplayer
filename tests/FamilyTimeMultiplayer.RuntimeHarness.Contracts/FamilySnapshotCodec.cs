using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace FamilyTimeMultiplayer.RuntimeHarness.Contracts;

public static class FamilySnapshotCodec
{
    private const string SemanticPrefix = "family.semantic.";

    public static string Write(IReadOnlyDictionary<string, string> facts)
    {
        return string.Join("\n", facts.Where(pair => pair.Key.StartsWith(SemanticPrefix, StringComparison.Ordinal))
            .OrderBy(pair => pair.Key, StringComparer.Ordinal)
            .Select(pair => pair.Key + "\t" + Convert.ToBase64String(Encoding.UTF8.GetBytes(pair.Value))));
    }

    public static IReadOnlyDictionary<string, string> Read(string content)
    {
        var facts = new SortedDictionary<string, string>(StringComparer.Ordinal);
        foreach (string line in content.Split(new[] { '\n' }, StringSplitOptions.RemoveEmptyEntries))
        {
            int separator = line.IndexOf('\t');
            if (separator <= 0 || !line.Substring(0, separator).StartsWith(SemanticPrefix, StringComparison.Ordinal))
                throw new FormatException("Family snapshot contains an invalid semantic fact.");
            string key = line.Substring(0, separator);
            string value = Encoding.UTF8.GetString(Convert.FromBase64String(line.Substring(separator + 1)));
            if (!facts.TryAdd(key, value)) throw new FormatException($"Family snapshot repeats '{key}'.");
        }
        if (facts.Count == 0) throw new FormatException("Family snapshot has no semantic facts.");
        return facts;
    }

    public static IReadOnlyList<string> Compare(IReadOnlyDictionary<string, string> expected, IReadOnlyDictionary<string, string> actual)
    {
        IReadOnlyDictionary<string, string> expectedSemantic = SemanticFacts(expected);
        IReadOnlyDictionary<string, string> actualSemantic = SemanticFacts(actual);
        return expectedSemantic.Keys.Union(actualSemantic.Keys, StringComparer.Ordinal).OrderBy(key => key, StringComparer.Ordinal)
            .Where(key => !expectedSemantic.TryGetValue(key, out string? oldValue) || !actualSemantic.TryGetValue(key, out string? newValue) || oldValue != newValue)
            .Select(key => $"{key}: expected='{(expectedSemantic.TryGetValue(key, out string? oldValue) ? oldValue : "<missing>")}', actual='{(actualSemantic.TryGetValue(key, out string? newValue) ? newValue : "<missing>")}'")
            .ToArray();
    }

    private static IReadOnlyDictionary<string, string> SemanticFacts(IReadOnlyDictionary<string, string> facts) =>
        facts.Where(pair => pair.Key.StartsWith(SemanticPrefix, StringComparison.Ordinal))
            .ToDictionary(pair => pair.Key, pair => pair.Value, StringComparer.Ordinal);
}
