namespace Keyservant.Cli;

/// <summary>Minimal argument parser: positional arguments, <c>--name value</c> options and <c>--flag</c> switches.</summary>
internal sealed class CommandLine
{
    private readonly Dictionary<string, string?> _options = new(StringComparer.OrdinalIgnoreCase);
    private readonly HashSet<string> _used = new(StringComparer.OrdinalIgnoreCase);

    public CommandLine(IReadOnlyList<string> args, IReadOnlySet<string> flags)
    {
        for (var i = 0; i < args.Count; i++)
        {
            var a = args[i];
            if (a.StartsWith("--", StringComparison.Ordinal) || a.Length == 2 && a[0] == '-' && char.IsLetter(a[1]))
            {
                var name = a.TrimStart('-');
                var eq = name.IndexOf('=', StringComparison.Ordinal);
                if (eq >= 0)
                {
                    _options[name[..eq]] = name[(eq + 1)..];
                }
                else if (flags.Contains(name))
                {
                    _options[name] = null;
                }
                else
                {
                    if (i + 1 >= args.Count) throw new UsageException($"Option {a} needs a value.");
                    _options[name] = args[++i];
                }
            }
            else
            {
                Positional.Add(a);
            }
        }
    }

    public List<string> Positional { get; } = [];

    public string? Option(string name, string? alias = null)
    {
        if (_options.TryGetValue(name, out var v)) { _used.Add(name); return v; }
        if (alias is not null && _options.TryGetValue(alias, out v)) { _used.Add(alias); return v; }
        return null;
    }

    public bool Flag(string name)
    {
        if (!_options.ContainsKey(name)) return false;
        _used.Add(name);
        return true;
    }

    public string Arg(int index, string what) =>
        index < Positional.Count ? Positional[index] : throw new UsageException($"Missing {what}.");

    /// <summary>Fails on options the command did not ask for (typos).</summary>
    public void EnsureNoUnknownOptions()
    {
        var unknown = _options.Keys.Where(k => !_used.Contains(k)).ToList();
        if (unknown.Count > 0) throw new UsageException($"Unknown option: --{unknown[0]}");
    }
}

internal sealed class UsageException(string message) : Exception(message);
