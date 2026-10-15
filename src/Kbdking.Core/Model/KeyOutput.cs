namespace Kbdking.Core.Model;

/// <summary>
/// What a key produces in one shift state: text (one UTF-16 unit, or several for a ligature)
/// or a dead key that waits for the next keystroke.
/// </summary>
public sealed record KeyOutput
{
    private KeyOutput(string text, bool isDead)
    {
        Text = text;
        IsDead = isDead;
    }

    /// <summary>The produced text, or for a dead key the accent character.</summary>
    public string Text { get; }

    public bool IsDead { get; }

    /// <summary>
    /// True when the key produces more than one UTF-16 unit. Windows stores these in the ligature table;
    /// a character outside the Basic Multilingual Plane (a surrogate pair) is always a ligature.
    /// </summary>
    public bool IsLigature => !IsDead && Text.Length > 1;

    public char Accent => IsDead ? Text[0] : throw new InvalidOperationException("Not a dead key.");

    public static KeyOutput Of(string text)
    {
        ArgumentException.ThrowIfNullOrEmpty(text);
        return new KeyOutput(text, false);
    }

    public static KeyOutput Of(char c) => new(c.ToString(), false);

    public static KeyOutput Dead(char accent) => new(accent.ToString(), true);

    public override string ToString() => IsDead ? $"dead({Text})" : Text;
}
