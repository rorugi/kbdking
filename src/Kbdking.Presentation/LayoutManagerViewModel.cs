using System.Collections.ObjectModel;
using System.Windows.Input;
using Kbdking.Platform;

namespace Kbdking.Presentation;

/// <summary>One row in the layout manager.</summary>
public sealed class LayoutItemViewModel : ObservableObject
{
    private readonly LayoutManagerViewModel _owner;
    private bool _isEnabled;

    internal LayoutItemViewModel(LayoutManagerViewModel owner, InstalledLayout layout)
    {
        _owner = owner;
        Layout = layout;
        _isEnabled = layout.IsEnabled;
        UninstallCommand = new AsyncRelayCommand(() => _owner.UninstallAsync(this), () => layout.IsCustom, _owner.ReportError);
        EditCopyCommand = new RelayCommand(() => _owner.EditCopy(this));
        KeepCommand = new AsyncRelayCommand(() => _owner.KeepAsync(this), () => layout.TemporaryUntil is not null, _owner.ReportError);
    }

    /// <summary>A test install that Windows removes again at <see cref="InstalledLayout.TemporaryUntil"/>.</summary>
    public bool IsTemporary => Layout.TemporaryUntil is not null;

    public string TemporaryText => Layout.TemporaryUntil is { } until
        ? until <= DateTimeOffset.Now ? "Test install: expired, will be removed shortly." : $"Test install: removed automatically at {until.ToLocalTime():t}."
        : "";

    public ICommand KeepCommand { get; }

    public InstalledLayout Layout { get; private set; }

    public string Name => Layout.Name;
    public string Klid => Layout.Klid;
    public string LayoutFile => Layout.LayoutFile;
    public bool IsCustom => Layout.IsCustom;
    public string Kind => Layout.IsCustom ? (Layout.TemporaryUntil is null ? "Custom" : "Custom (test)") : "Windows";
    public string? Problem => Layout.Problem;
    public bool HasProblem => Layout.Problem is not null;

    /// <summary>Bound to the toggle: adds or removes the layout from the switcher immediately.</summary>
    public bool IsEnabled
    {
        get => _isEnabled;
        set
        {
            if (_isEnabled == value) return;
            if (_owner.TrySetEnabled(this, value))
            {
                _isEnabled = value;
                Layout = Layout with { IsEnabled = value };
            }
            OnPropertyChanged();
        }
    }

    public ICommand UninstallCommand { get; }

    public ICommand EditCopyCommand { get; }
}

/// <summary>The start screen: every layout on the PC, with add/remove toggles, install, uninstall and repair.</summary>
public sealed class LayoutManagerViewModel : ObservableObject
{
    private readonly ILayoutSystem _system;
    private readonly IPrivilegedOperations _privileged;
    private readonly IDialogService _dialogs;
    private readonly Action<string> _openInEditor;
    private List<LayoutItemViewModel> _all = [];
    private string _filter = "";
    private bool _customOnly;
    private string? _status;

    public LayoutManagerViewModel(ILayoutSystem system, IPrivilegedOperations privileged, IDialogService dialogs, Action<string> openInEditor)
    {
        _system = system;
        _privileged = privileged;
        _dialogs = dialogs;
        _openInEditor = openInEditor;
        RefreshCommand = new RelayCommand(Refresh);
        InstallFromFileCommand = new AsyncRelayCommand(InstallFromFileAsync, onError: ReportError);
        RepairCommand = new AsyncRelayCommand(RepairAsync, onError: ReportError);
    }

    public ObservableCollection<LayoutItemViewModel> Layouts { get; } = [];

    public string Filter
    {
        get => _filter;
        set { if (SetProperty(ref _filter, value)) ApplyFilter(); }
    }

    public bool CustomOnly
    {
        get => _customOnly;
        set { if (SetProperty(ref _customOnly, value)) ApplyFilter(); }
    }

    public string? Status
    {
        get => _status;
        private set => SetProperty(ref _status, value);
    }

    public ICommand RefreshCommand { get; }
    public ICommand InstallFromFileCommand { get; }
    public ICommand RepairCommand { get; }

    public void Refresh()
    {
        try
        {
            // Layouts in the switcher first, then the rest; each group by name.
            _all = [.. _system.GetLayouts().Select(l => new LayoutItemViewModel(this, l))
                .OrderByDescending(l => l.IsEnabled).ThenBy(l => l.Name, StringComparer.CurrentCultureIgnoreCase)];
            ApplyFilter();
            var enabled = _all.Count(l => l.IsEnabled);
            Status = $"{_all.Count} layouts, {enabled} in your switcher, {_all.Count(l => l.IsCustom)} custom.";
        }
        catch (Exception ex) when (ex is LayoutSystemException or UnauthorizedAccessException or PlatformNotSupportedException)
        {
            ReportError(ex);
        }
    }

    private void ApplyFilter()
    {
        Layouts.Clear();
        foreach (var l in _all)
        {
            if (_customOnly && !l.IsCustom) continue;
            if (_filter.Length > 0 && !l.Name.Contains(_filter, StringComparison.CurrentCultureIgnoreCase)
                && !l.Klid.Contains(_filter, StringComparison.OrdinalIgnoreCase)) continue;
            Layouts.Add(l);
        }
    }

    internal bool TrySetEnabled(LayoutItemViewModel item, bool enabled)
    {
        try
        {
            if (enabled) _system.Enable(item.Layout);
            else _system.Disable(item.Layout);
            Status = enabled ? $"Added {item.Name} to your switcher." : $"Removed {item.Name} from your switcher.";
            return true;
        }
        catch (LayoutSystemException ex)
        {
            ReportError(ex);
            return false;
        }
    }

    internal async Task UninstallAsync(LayoutItemViewModel item)
    {
        var items = _system.DescribeUninstall(item.Klid);
        var ok = await _dialogs.ConfirmAsync($"Uninstall {item.Name}?",
            "This removes:" + Environment.NewLine + string.Join(Environment.NewLine, items.Select(i => "• " + i)), "Uninstall");
        if (!ok) return;
        await Run(() => _privileged.UninstallAsync(item.Klid), $"Uninstalled {item.Name}.");
    }

    internal async Task KeepAsync(LayoutItemViewModel item) =>
        await Run(() => _privileged.KeepAsync(item.Klid), $"{item.Name} is now installed permanently.");

    internal void EditCopy(LayoutItemViewModel item)
    {
        var dll = Path.Combine(Environment.SystemDirectory, item.LayoutFile);
        _openInEditor(dll);
    }

    private async Task InstallFromFileAsync()
    {
        var file = await _dialogs.PickOpenFileAsync([".kbdking", ".klc"]);
        if (file is null) return;
        await Run(() => _privileged.InstallAsync(file), $"Installed {Path.GetFileNameWithoutExtension(file)}.");
    }

    private async Task RepairAsync() => await Run(_privileged.RepairAsync, null);

    private async Task Run(Func<Task<OperationResult>> operation, string? success)
    {
        Status = "Waiting for administrator approval...";
        var result = await operation();
        if (result.Cancelled) Status = "Cancelled.";
        else if (!result.Succeeded) await _dialogs.ShowMessageAsync("That didn't work", result.Output);
        else Status = success ?? result.Output.Trim();
        Refresh();
    }

    internal void ReportError(Exception ex)
    {
        Status = ex.Message;
        _ = _dialogs.ShowMessageAsync("Something went wrong", ex.Message);
    }
}
