using Keyservant.Core.Formats;
using Keyservant.Core.Model;

namespace Keyservant.Core.Editing;

/// <summary>
/// Undo/redo for layout editing, based on snapshots of the serialized layout (a few tens of KB each).
/// Consecutive changes in the same group (e.g. typing into one text field) are merged into one step.
/// </summary>
public sealed class LayoutHistory
{
    private readonly List<string> _states = [];
    private readonly int _capacity;
    private readonly Func<DateTime> _clock;
    private int _index;
    private string? _lastGroup;
    private DateTime _lastTime;

    public LayoutHistory(KeyboardLayout initial, int capacity = 200, Func<DateTime>? clock = null)
    {
        _capacity = Math.Max(2, capacity);
        _clock = clock ?? (() => DateTime.UtcNow);
        Reset(initial);
    }

    /// <summary>Merge window for changes in the same group.</summary>
    public TimeSpan MergeWindow { get; init; } = TimeSpan.FromSeconds(1.5);

    public bool CanUndo => _index > 0;

    public bool CanRedo => _index < _states.Count - 1;

    public event EventHandler? Changed;

    /// <summary>Starts over with a new layout (after opening a file).</summary>
    public void Reset(KeyboardLayout layout)
    {
        _states.Clear();
        _states.Add(KeyservantFormat.Serialize(layout));
        _index = 0;
        _lastGroup = null;
        Changed?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>Records the layout after a change.</summary>
    /// <param name="group">Changes with the same group within <see cref="MergeWindow"/> become one undo step.</param>
    public void Record(KeyboardLayout layout, string? group = null)
    {
        var snapshot = KeyservantFormat.Serialize(layout);
        if (snapshot == _states[_index]) return;
        var now = _clock();
        _states.RemoveRange(_index + 1, _states.Count - _index - 1);
        if (group is not null && group == _lastGroup && now - _lastTime < MergeWindow && _index > 0)
        {
            _states[_index] = snapshot;
            // Typing and deleting the same text within one step leaves nothing to undo.
            if (snapshot == _states[_index - 1])
            {
                _states.RemoveAt(_index);
                _index--;
                group = null; // the next change starts a new step instead of merging into the previous one
            }
        }
        else
        {
            _states.Add(snapshot);
            _index++;
            if (_states.Count > _capacity)
            {
                _states.RemoveAt(0);
                _index--;
            }
        }
        _lastGroup = group;
        _lastTime = now;
        Changed?.Invoke(this, EventArgs.Empty);
    }

    public KeyboardLayout Undo()
    {
        if (!CanUndo) throw new InvalidOperationException("Nothing to undo.");
        _index--;
        _lastGroup = null;
        Changed?.Invoke(this, EventArgs.Empty);
        return KeyservantFormat.Parse(_states[_index]);
    }

    public KeyboardLayout Redo()
    {
        if (!CanRedo) throw new InvalidOperationException("Nothing to redo.");
        _index++;
        _lastGroup = null;
        Changed?.Invoke(this, EventArgs.Empty);
        return KeyservantFormat.Parse(_states[_index]);
    }
}
