using System.Collections.ObjectModel;
using Keyservant.Core.Unicode;

namespace Keyservant.Presentation;

/// <summary>A text field that the Unicode picker can fill.</summary>
public interface ICharacterTarget
{
    /// <summary>Puts the picked character into the field (replacing its content).</summary>
    void PickCharacter(string text);
}

/// <summary>
/// Finds characters by name ("a macron", "dot below"), by code (U+1E43, 1e43) or by pasting them.
/// Works offline: the names come from the Unicode data built into Keyservant.
/// </summary>
public sealed class UnicodePickerViewModel : ObservableObject
{
    private string _query = "";
    private UnicodeCharInfo? _selected;

    public string Query
    {
        get => _query;
        set
        {
            if (SetProperty(ref _query, value)) Search();
        }
    }

    public ObservableCollection<UnicodeCharInfo> Results { get; } = [];

    public UnicodeCharInfo? Selected { get => _selected; set => SetProperty(ref _selected, value); }

    public string UnicodeVersion => $"Unicode {UnicodeCatalog.Version}";

    /// <summary>The field the next pick goes to, set by the view when it opens the picker.</summary>
    public ICharacterTarget? Target { get; set; }

    public string ResultSummary => _query.Trim().Length == 0 ? "Type a name, a code or a character." : Results.Count == 0 ? "Nothing found." : $"{Results.Count} characters";

    /// <summary>Sends <paramref name="info"/> (or the selected result) to <see cref="Target"/>. Returns false if there is nothing to send.</summary>
    public bool Pick(UnicodeCharInfo? info = null)
    {
        info ??= _selected ?? Results.FirstOrDefault();
        if (info is null || Target is null) return false;
        Target.PickCharacter(info.Text);
        return true;
    }

    private void Search()
    {
        Results.Clear();
        if (_query.Trim().Length > 0)
        {
            foreach (var r in UnicodeCatalog.Search(_query, 200)) Results.Add(r);
        }
        Selected = Results.FirstOrDefault();
        OnPropertyChanged(nameof(ResultSummary));
    }
}
