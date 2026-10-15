using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows.Input;

namespace Kbdking.Presentation;

/// <summary>Base class for view models: raises <see cref="PropertyChanged"/> from <see cref="SetProperty{T}"/>.</summary>
public abstract class ObservableObject : INotifyPropertyChanged
{
    public event PropertyChangedEventHandler? PropertyChanged;

    protected bool SetProperty<T>(ref T field, T value, [CallerMemberName] string? name = null)
    {
        if (EqualityComparer<T>.Default.Equals(field, value)) return false;
        field = value;
        OnPropertyChanged(name);
        return true;
    }

    protected void OnPropertyChanged([CallerMemberName] string? name = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}

/// <summary>
/// A command backed by delegates. Exceptions go to <see cref="UnhandledError"/> (set by the app to show a
/// message) instead of crashing the app; without a handler they propagate as usual (e.g. in tests).
/// </summary>
public sealed class RelayCommand(Action<object?> execute, Func<object?, bool>? canExecute = null) : ICommand
{
    public RelayCommand(Action execute, Func<bool>? canExecute = null)
        : this(_ => execute(), canExecute is null ? null : _ => canExecute())
    {
    }

    /// <summary>Receives exceptions thrown by any command's action.</summary>
    public static Action<Exception>? UnhandledError { get; set; }

    public event EventHandler? CanExecuteChanged;

    public bool CanExecute(object? parameter) => canExecute?.Invoke(parameter) ?? true;

    public void Execute(object? parameter)
    {
        if (UnhandledError is not { } handler)
        {
            execute(parameter);
            return;
        }
        try
        {
            execute(parameter);
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            handler(ex);
        }
    }

    public void NotifyCanExecuteChanged() => CanExecuteChanged?.Invoke(this, EventArgs.Empty);
}

/// <summary>An async command that disables itself while running and reports failures to a handler.</summary>
public sealed class AsyncRelayCommand(Func<object?, Task> execute, Func<bool>? canExecute = null, Action<Exception>? onError = null) : ICommand
{
    private bool _running;

    public AsyncRelayCommand(Func<Task> execute, Func<bool>? canExecute = null, Action<Exception>? onError = null)
        : this(_ => execute(), canExecute, onError)
    {
    }

    public event EventHandler? CanExecuteChanged;

    public bool CanExecute(object? parameter) => !_running && (canExecute?.Invoke() ?? true);

    public async void Execute(object? parameter)
    {
        _running = true;
        NotifyCanExecuteChanged();
        try
        {
            await execute(parameter);
        }
        catch (Exception ex) when ((onError ?? RelayCommand.UnhandledError) is not null)
        {
            (onError ?? RelayCommand.UnhandledError)!(ex);
        }
        finally
        {
            _running = false;
            NotifyCanExecuteChanged();
        }
    }

    public void NotifyCanExecuteChanged() => CanExecuteChanged?.Invoke(this, EventArgs.Empty);
}
