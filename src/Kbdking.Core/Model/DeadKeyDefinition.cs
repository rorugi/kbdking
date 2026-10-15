namespace Kbdking.Core.Model;

/// <summary>
/// One composition: dead key + <paramref name="Base"/> → <paramref name="Result"/>.
/// When <paramref name="Chained"/> is true the result is itself a dead key (kbd.h: DKF_DEAD),
/// so a further keystroke is composed using the dead key whose accent is <paramref name="Result"/>.
/// </summary>
public readonly record struct DeadKeyEntry(char Base, char Result, bool Chained = false);

/// <summary>All compositions of one dead key (accent).</summary>
public sealed class DeadKeyDefinition
{
    public DeadKeyDefinition(char accent) => Accent = accent;

    /// <summary>The dead character, e.g. U+0302 or U+005E for a circumflex.</summary>
    public char Accent { get; set; }

    /// <summary>Name shown by Windows (kbd.h: pKeyNamesDead), e.g. "CIRCUMFLEX".</summary>
    public string? Name { get; set; }

    public List<DeadKeyEntry> Entries { get; } = [];

    public DeadKeyEntry? Find(char baseChar)
    {
        foreach (var e in Entries)
        {
            if (e.Base == baseChar) return e;
        }
        return null;
    }

    /// <summary>Adds or replaces the composition for <paramref name="baseChar"/>.</summary>
    public void Set(char baseChar, char result, bool chained = false)
    {
        var i = Entries.FindIndex(e => e.Base == baseChar);
        var entry = new DeadKeyEntry(baseChar, result, chained);
        if (i >= 0) Entries[i] = entry;
        else Entries.Add(entry);
    }

    public DeadKeyDefinition Clone()
    {
        var d = new DeadKeyDefinition(Accent) { Name = Name };
        d.Entries.AddRange(Entries);
        return d;
    }
}
