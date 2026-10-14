using System.Globalization;
using System.Text;
using Keyservant.Core.Model;

namespace Keyservant.Core.Analysis;

/// <summary>How often a character occurs in the sample text and how it is typed.</summary>
public sealed record CharacterUsage(string Text, int Count, TypingRecipe? Recipe)
{
    public bool IsTypeable => Recipe is not null;
    public double Effort => Recipe?.Effort ?? double.PositiveInfinity;
    public int Strokes => Recipe?.Strokes.Count ?? 0;
}

/// <summary>Result of analysing a text with a layout.</summary>
public sealed class TextAnalysis
{
    public required IReadOnlyList<CharacterUsage> Characters { get; init; }

    /// <summary>Number of key presses (strokes) per virtual key, for the heatmap.</summary>
    public required IReadOnlyDictionary<byte, int> KeyPresses { get; init; }

    public int TotalCharacters => Characters.Sum(c => c.Count);

    public int TypeableCharacters => Characters.Where(c => c.IsTypeable).Sum(c => c.Count);

    public double TotalEffort => Characters.Where(c => c.IsTypeable).Sum(c => c.Count * c.Effort);

    /// <summary>Average effort per typeable character; lower is better. Comparable between layouts on the same text.</summary>
    public double EffortPerCharacter => TypeableCharacters == 0 ? 0 : TotalEffort / TypeableCharacters;

    public IEnumerable<CharacterUsage> Untypeable => Characters.Where(c => !c.IsTypeable);

    /// <summary>
    /// Frequent characters that are expensive to type (dead keys, AltGr, far keys): the best candidates for
    /// a direct, easier position.
    /// </summary>
    public IEnumerable<CharacterUsage> ExpensiveCharacters(int count = 10) =>
        Characters.Where(c => c.IsTypeable && (c.Strokes > 1 || c.Effort > 2.0))
                  .OrderByDescending(c => c.Count * c.Effort)
                  .Take(count);

    public string Summary()
    {
        var sb = new StringBuilder();
        sb.Append(CultureInfo.InvariantCulture, $"{TotalCharacters} characters, {Characters.Count} distinct.\n");
        sb.Append(CultureInfo.InvariantCulture, $"Effort per character: {EffortPerCharacter:0.00} (lower is better).\n");
        var untypeable = Untypeable.ToList();
        if (untypeable.Count > 0)
            sb.Append(CultureInfo.InvariantCulture, $"Not typeable ({untypeable.Sum(c => c.Count)} occurrences): {string.Join(" ", untypeable.Select(c => c.Text))}\n");
        var expensive = ExpensiveCharacters().ToList();
        if (expensive.Count > 0)
        {
            sb.Append("Frequent but costly to type:\n");
            foreach (var c in expensive)
                sb.Append(CultureInfo.InvariantCulture, $"  {c.Text}  {c.Count}×, {c.Strokes} stroke(s), effort {c.Effort:0.0}\n");
        }
        return sb.ToString();
    }
}

/// <summary>
/// Measures how easy a text is to type with a layout: character frequencies, the cheapest way to type each
/// character, total effort, key-press counts for a heatmap and characters the layout can't type.
/// </summary>
public static class TextAnalyzer
{
    public static TextAnalysis Analyze(KeyboardLayout layout, string text, EffortModel? effort = null)
    {
        var effective = Compose.ComposeCompiler.Compile(layout).Layout;
        var finder = new KeystrokeFinder(effective, effort);

        var counts = new Dictionary<string, int>(StringComparer.Ordinal);
        var normalized = text.Normalize(NormalizationForm.FormC);
        var e = StringInfo.GetTextElementEnumerator(normalized);
        while (e.MoveNext())
        {
            var element = (string)e.Current;
            if (element is "\r" or "\n" or "\r\n") continue;
            counts[element] = counts.GetValueOrDefault(element) + 1;
        }

        var usages = new List<CharacterUsage>();
        var presses = new Dictionary<byte, int>();
        foreach (var (element, count) in counts.OrderByDescending(p => p.Value).ThenBy(p => p.Key, StringComparer.Ordinal))
        {
            var recipe = finder.Find(element) ?? (element == "\t" ? finder.Find("\t") : null);
            usages.Add(new CharacterUsage(element, count, recipe));
            if (recipe is null) continue;
            foreach (var s in recipe.Strokes) presses[s.VirtualKey] = presses.GetValueOrDefault(s.VirtualKey) + count;
        }
        return new TextAnalysis { Characters = usages, KeyPresses = presses };
    }
}
