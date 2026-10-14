namespace Keyservant.Platform;

/// <summary>A keyboard layout registered on this PC.</summary>
public sealed record InstalledLayout
{
    /// <summary>Keyboard layout ID (KLID), e.g. <c>00000407</c> or <c>A0000C07</c>.</summary>
    public required string Klid { get; init; }

    /// <summary>The layout DLL file name, e.g. <c>KBDGR.DLL</c>.</summary>
    public required string LayoutFile { get; init; }

    /// <summary>Human-readable name.</summary>
    public required string Name { get; init; }

    /// <summary>"Layout Id" registry value for variants that share a language, e.g. <c>00C1</c>.</summary>
    public string? LayoutId { get; init; }

    /// <summary>True for layouts installed by Keyservant (custom layouts); false for Windows' own layouts.</summary>
    public bool IsCustom { get; init; }

    /// <summary>True if the layout appears in the current user's Win+Space switcher.</summary>
    public bool IsEnabled { get; init; }

    /// <summary>A problem found while listing, such as a missing DLL; null when the layout is healthy.</summary>
    public string? Problem { get; init; }

    /// <summary>For a test install: when it is reverted automatically. Null for normal installs.</summary>
    public DateTimeOffset? TemporaryUntil { get; init; }

    /// <summary>The language this layout is offered under. Custom layouts carry it in the KLID's low word.</summary>
    public ushort LanguageId => ushort.TryParse(Klid.AsSpan(4), System.Globalization.NumberStyles.HexNumber, null, out var id) ? id : (ushort)0;

    /// <summary>The "LANGID:KLID" string the Windows input APIs use.</summary>
    public string InputTip(ushort? languageId = null) => $"{languageId ?? LanguageId:X4}:{Klid.ToUpperInvariant()}";
}

/// <summary>Outcome of a repair run.</summary>
public sealed class RepairReport
{
    public List<string> Fixed { get; } = [];
    public List<string> Problems { get; } = [];

    public bool IsHealthy => Fixed.Count == 0 && Problems.Count == 0;
}

/// <summary>An operation on the system layouts failed; the message is suitable for users.</summary>
public sealed class LayoutSystemException : Exception
{
    public LayoutSystemException(string message) : base(message) { }
    public LayoutSystemException(string message, Exception inner) : base(message, inner) { }
}
