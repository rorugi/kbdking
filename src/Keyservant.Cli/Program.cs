using System.Reflection;
using Keyservant.Binary;
using Keyservant.Binary.Packaging;
using Keyservant.Core.Formats;
using Keyservant.Core.Model;
using Keyservant.Core.Templates;
using Keyservant.Core.Validation;
using Keyservant.Platform;

namespace Keyservant.Cli;

internal static partial class Program
{
    public const int Ok = 0, Failed = 1, Usage = 2, Invalid = 3, Declined = 4;

    private static readonly HashSet<string> Flags = new(StringComparer.OrdinalIgnoreCase)
    {
        "help", "h", "version", "no-wow64", "no-arm64", "yes", "y", "json", "all", "custom", "apply", "stickers",
    };

    private static TextWriter Out = Console.Out;
    private static TextWriter Err = Console.Error;

    public static int Main(string[] args)
    {
        // An elevated copy of the CLI writes to a log file that the original process prints.
        var logIndex = Array.IndexOf(args, "--log");
        StreamWriter? log = null;
        if (logIndex >= 0 && logIndex + 1 < args.Length)
        {
            log = new StreamWriter(args[logIndex + 1]) { AutoFlush = true };
            Out = Err = log;
            args = [.. args.Take(logIndex), .. args.Skip(logIndex + 2)];
        }
        try
        {
            return Run(args);
        }
        finally
        {
            log?.Dispose();
        }
    }

    internal static int Run(string[] args, TextWriter? output = null)
    {
        if (output is not null) Out = Err = output;
        if (args.Length == 0 || args[0] is "help" or "--help" or "-h" or "/?")
        {
            PrintHelp();
            return args.Length == 0 ? Usage : Ok;
        }
        if (args[0] is "--version" or "version")
        {
            Out.WriteLine($"keyservant {Assembly.GetExecutingAssembly().GetName().Version?.ToString(3)}");
            return Ok;
        }

        try
        {
            var cmd = new CommandLine(args.Skip(1).ToList(), Flags);
            var result = args[0].ToLowerInvariant() switch
            {
                "templates" => Templates(cmd),
                "new" => New(cmd),
                "validate" => Validate(cmd),
                "convert" => Convert(cmd),
                "build" => Build(cmd),
                "package" => Package(cmd),
                "test" => RunTests(cmd),
                "suggest" => Suggest(cmd),
                "compose" => ComposeCommand(cmd),
                "analyze" => Analyze(cmd),
                "diff" => Diff(cmd),
                "render" => Render(cmd),
                "unicode" => UnicodeSearch(cmd),
                "shortcuts" => Shortcuts(cmd),
                "layers" => Layers(cmd),
                "list" or "enable" or "disable" or "import" or "install" or "uninstall" or "repair" or "keep" or "cleanup" =>
                    OperatingSystem.IsWindows() ? RunWindowsCommand(args[0].ToLowerInvariant(), cmd, args) : NotWindows(),
                _ => throw new UsageException($"Unknown command '{args[0]}'. Run 'keyservant help' for the list of commands."),
            };
            return result;
        }
        catch (UsageException ex)
        {
            Err.WriteLine($"error: {ex.Message}");
            return Usage;
        }
        catch (LayoutBuildException ex)
        {
            Err.WriteLine(ex.Message);
            return Invalid;
        }
        catch (Exception ex) when (ex is LayoutFormatException or LayoutSystemException or NotSupportedException
                                       or InvalidOperationException or ArgumentException or FormatException
                                       or IOException or UnauthorizedAccessException or InvalidDataException
                                       or Keyservant.Binary.Pe.InvalidLayoutDllException)
        {
            Err.WriteLine($"error: {ex.Message}");
            return Failed;
        }
    }

    private static void PrintHelp()
    {
        Out.WriteLine("""
            Keyservant - create, edit and install Windows keyboard layouts

            Layout files
              keyservant templates                         List built-in starting points
              keyservant new <template> -o <file>          Create a .keyservant file from a template
                         [--name <name>] [--dll <name>] [--locale <tag>]
              keyservant validate <file>                   Check a layout for problems (including its tests)
              keyservant test <file>                       Run the layout's tests
              keyservant convert <input> <output>          Convert between .keyservant, .klc, .dll, .keylayout,
                                                           .xkb (+ .XCompose) and .c
              keyservant diff <a> <b> [--svg <out.svg>]    Show what changed between two layouts
              keyservant build <file> [-o <dir>]           Build the layout DLLs (x64, WOW64, ARM64)
                         [--no-wow64] [--no-arm64]
              keyservant render <file> -o <out.svg>        Draw the keyboard, or stickers to print
                         [--stickers] [--shape iso|ansi|jis|abnt]

            Characters and typing
              keyservant suggest <file> --chars "āīū…"     Propose where to put characters
                         [--text <sample.txt>] [--apply] [-o <out>]
              keyservant compose import <file> <.XCompose> Add compose sequences [--key "E0 5D"]
              keyservant compose export <file> <out>       Write the sequences as .XCompose
              keyservant compose list <file>               List the sequences
              keyservant analyze <file> <text.txt>         Typing effort and a heatmap for a text
                         [--heatmap <out.svg>] [--compare <other layout>]
              keyservant unicode <words or code>           Search characters by name, e.g. "a macron"
              keyservant shortcuts <file> [mode] [-o out]  Show or set where Ctrl+C/V/Z are:
                                                           letters | qwerty | qwertz | azerty
              keyservant layers <file>                     List layer keys
              keyservant layers <file> add <key> <A|B|C>   Make a key a layer key, e.g. add 3A A (Caps Lock),
                                                           add 56 B (the < > key), add E05D A (Menu key)
              keyservant layers <file> remove <key>        Turn a layer key back into a normal key

            Packages
              keyservant package <file> -o <setup.exe>     Standalone installer [--stub <path>]
              keyservant package <file> --msi <out.msi>    MSI for Group Policy / Intune (needs WiX: dotnet tool install -g wix)
              keyservant package <file> --wix <dir>        WiX source only
              keyservant package <file> --intune <dir>     Intune Win32 app folder [--stub <path>]

            This PC (Windows)
              keyservant list [--custom]                   List installed layouts
              keyservant enable <klid>                     Add a layout to your Win+Space switcher
              keyservant disable <klid>                    Remove a layout from your switcher
              keyservant import <klid|dll> -o <file>       Save an installed layout as a .keyservant file
              keyservant install <file>                    Install a layout (asks for admin rights)
                         [--temporary <minutes>]           ...as a test install that reverts itself
              keyservant keep <klid>                       Keep a test install permanently
              keyservant cleanup                           Remove expired test installs
              keyservant uninstall <klid> [--yes]          Uninstall a custom layout (asks for admin rights)
              keyservant repair                            Restore layouts lost in Windows updates

            Exit codes: 0 ok, 1 failed, 2 usage error, 3 layout has errors or failing tests, 4 admin prompt declined.
            """);
    }

    private static int NotWindows()
    {
        Err.WriteLine("error: this command only works on Windows.");
        return Failed;
    }

    [System.Runtime.Versioning.SupportedOSPlatform("windows")]
    private static int RunWindowsCommand(string name, CommandLine cmd, string[] args) => name switch
    {
        "list" => List(cmd),
        "enable" => Toggle(cmd, enable: true),
        "disable" => Toggle(cmd, enable: false),
        "import" => Import(cmd),
        "install" => Install(cmd, args),
        "uninstall" => Uninstall(cmd, args),
        "repair" => Repair(cmd, args),
        "keep" => Keep(cmd, args),
        "cleanup" => Cleanup(cmd, args),
        _ => throw new UsageException($"Unknown command '{name}'."),
    };

    private static LayoutFormatRegistry Formats { get; } = LayoutDllFormat.CreateRegistry();

    private static KeyboardLayout Load(string path, bool showReport = true)
    {
        if (!File.Exists(path)) throw new IOException($"File not found: {path}");
        var report = new ConversionReport();
        var layout = Formats.ReadFile(path, report);
        if (showReport) PrintReport(report);
        return layout;
    }

    private static void PrintReport(ConversionReport report)
    {
        foreach (var w in report.Warnings) Err.WriteLine($"warning: {w}");
    }

    private static void PrintValidation(ValidationResult result, bool includeInfo = false)
    {
        foreach (var i in result.Issues.Where(i => includeInfo || i.Severity != Severity.Info))
        {
            (i.Severity == Severity.Error ? Err : Out).WriteLine(i);
        }
    }

    // ---------------------------------------------------------------- layout file commands

    private static int Templates(CommandLine cmd)
    {
        cmd.EnsureNoUnknownOptions();
        foreach (var t in LayoutTemplates.All) Out.WriteLine($"{t.Id,-8} {t.DisplayName}");
        return Ok;
    }

    private static int New(CommandLine cmd)
    {
        var template = cmd.Arg(0, "template (see 'keyservant templates')");
        var output = cmd.Option("output", "o") ?? throw new UsageException("Missing -o <file>.");
        var layout = LayoutTemplates.Create(template);
        layout.Metadata.Name = cmd.Option("name") ?? layout.Metadata.Name;
        layout.Metadata.DllName = cmd.Option("dll") ?? layout.Metadata.DllName;
        layout.Metadata.Locale = cmd.Option("locale") ?? layout.Metadata.Locale;
        cmd.EnsureNoUnknownOptions();
        if (File.Exists(output)) throw new IOException($"{output} already exists.");
        File.WriteAllText(output, KeyservantFormat.Serialize(layout));
        Out.WriteLine($"Created {output} from {template}.");
        return Ok;
    }

    private static int Validate(CommandLine cmd)
    {
        var path = cmd.Arg(0, "layout file");
        cmd.EnsureNoUnknownOptions();
        var result = LayoutValidator.Validate(Load(path));
        PrintValidation(result, includeInfo: true);
        var errors = result.Errors.Count();
        var warnings = result.Warnings.Count();
        Out.WriteLine(errors == 0 ? $"OK ({warnings} warning{(warnings == 1 ? "" : "s")})." : $"{errors} error{(errors == 1 ? "" : "s")}, {warnings} warning{(warnings == 1 ? "" : "s")}.");
        return errors == 0 ? Ok : Invalid;
    }

    private static int Convert(CommandLine cmd)
    {
        var input = cmd.Arg(0, "input file");
        var output = cmd.Arg(1, "output file");
        cmd.EnsureNoUnknownOptions();
        var layout = Load(input);
        var report = new ConversionReport();
        Formats.WriteFile(layout, output, report);
        if (output.EndsWith(".xkb", StringComparison.OrdinalIgnoreCase))
        {
            var companion = Path.ChangeExtension(output, ".XCompose");
            File.WriteAllText(companion, XkbSymbolsFormat.WriteXCompose(layout));
            Out.WriteLine($"Wrote {companion} (dead-key compositions; add its lines to ~/.XCompose).");
        }
        PrintReport(report);
        Out.WriteLine($"Wrote {output}{(report.IsLossless ? "" : $" ({report.Warnings.Count} warning{(report.Warnings.Count == 1 ? "" : "s")})")}.");
        return Ok;
    }

    private static int Build(CommandLine cmd)
    {
        var path = cmd.Arg(0, "layout file");
        var outDir = cmd.Option("output", "o") ?? Path.Combine(Path.GetDirectoryName(Path.GetFullPath(path))!, "build");
        var noWow = cmd.Flag("no-wow64");
        var noArm = cmd.Flag("no-arm64");
        cmd.EnsureNoUnknownOptions();

        var layout = Load(path);
        var result = LayoutDllBuilder.Build(layout, new LayoutBuildOptions { IncludeWow64 = !noWow, IncludeArm64 = !noArm });
        PrintValidation(result.Validation);
        Directory.CreateDirectory(Path.Combine(outDir, "x64"));
        File.WriteAllBytes(Path.Combine(outDir, "x64", result.FileName), result.X64);
        Out.WriteLine($"Wrote {Path.Combine(outDir, "x64", result.FileName)}");
        if (result.Wow64 is not null)
        {
            Directory.CreateDirectory(Path.Combine(outDir, "wow64"));
            File.WriteAllBytes(Path.Combine(outDir, "wow64", result.FileName), result.Wow64);
            Out.WriteLine($"Wrote {Path.Combine(outDir, "wow64", result.FileName)}");
        }
        if (result.Arm64 is not null)
        {
            Directory.CreateDirectory(Path.Combine(outDir, "arm64"));
            File.WriteAllBytes(Path.Combine(outDir, "arm64", result.FileName), result.Arm64);
            Out.WriteLine($"Wrote {Path.Combine(outDir, "arm64", result.FileName)}");
        }
        return Ok;
    }

    private static int Package(CommandLine cmd)
    {
        var path = cmd.Arg(0, "layout file");
        var output = cmd.Option("output", "o");
        var msi = cmd.Option("msi");
        var wix = cmd.Option("wix");
        var intune = cmd.Option("intune");
        var stub = cmd.Option("stub") ?? Path.Combine(AppContext.BaseDirectory, "Keyservant.Stub.exe");
        cmd.EnsureNoUnknownOptions();
        if (output is null && msi is null && wix is null && intune is null)
            throw new UsageException("Say what to create: -o <setup.exe>, --msi <file>, --wix <dir> or --intune <dir>.");

        var layout = Load(path);
        var result = LayoutDllBuilder.Build(layout);
        PrintValidation(result.Validation);
        var package = LayoutPackage.Create(layout, result);

        if (output is not null || intune is not null)
        {
            if (!File.Exists(stub)) throw new IOException($"Installer stub not found at {stub}. Pass --stub <path>.");
        }
        if (output is not null)
        {
            package.WriteInstaller(stub, output);
            Out.WriteLine($"Wrote {output}. Run it on any Windows PC to install \"{layout.Metadata.Name}\".");
        }
        if (intune is not null)
        {
            EnterprisePackaging.WriteIntuneFolder(package, stub, intune);
            Out.WriteLine($"Wrote the Intune app folder {intune}. See README.txt in it for the next steps.");
        }
        if (wix is not null || msi is not null)
        {
            var dir = wix ?? Path.Combine(Path.GetTempPath(), $"keyservant-wix-{Guid.NewGuid():N}");
            var wxs = EnterprisePackaging.WriteWixProject(package, dir);
            Out.WriteLine($"Layout ID in the MSI: {EnterprisePackaging.MsiKlid(package)}");
            if (wix is not null) Out.WriteLine($"Wrote {wxs}.");
            if (msi is not null)
            {
                var code = RunTool("wix", ["build", wxs, "-arch", "x64", "-o", Path.GetFullPath(msi)], dir);
                if (wix is null) Directory.Delete(dir, recursive: true);
                if (code != 0) return Failed;
                Out.WriteLine($"Wrote {msi}.");
            }
        }
        return Ok;
    }

    private static int RunTool(string tool, IEnumerable<string> args, string workingDirectory)
    {
        var psi = new System.Diagnostics.ProcessStartInfo(tool) { WorkingDirectory = workingDirectory, RedirectStandardOutput = true, RedirectStandardError = true };
        foreach (var a in args) psi.ArgumentList.Add(a);
        try
        {
            using var p = System.Diagnostics.Process.Start(psi)!;
            var output = p.StandardOutput.ReadToEnd() + p.StandardError.ReadToEnd();
            p.WaitForExit();
            if (p.ExitCode != 0) Err.WriteLine(output.Trim());
            return p.ExitCode;
        }
        catch (System.ComponentModel.Win32Exception)
        {
            Err.WriteLine($"error: '{tool}' was not found. Install the WiX toolset with: dotnet tool install --global wix --version 5.*");
            return Failed;
        }
    }

    // ---------------------------------------------------------------- this PC

    [System.Runtime.Versioning.SupportedOSPlatform("windows")]
    private static int List(CommandLine cmd)
    {
        var customOnly = cmd.Flag("custom");
        cmd.EnsureNoUnknownOptions();
        var layouts = new WindowsLayoutSystem().GetLayouts().Where(l => !customOnly || l.IsCustom).ToList();
        Out.WriteLine($"{"KLID",-9} {"On",-3} {"Type",-7} {"File",-14} Name");
        foreach (var l in layouts)
        {
            Out.WriteLine($"{l.Klid,-9} {(l.IsEnabled ? "yes" : ""),-3} {(l.IsCustom ? "custom" : "windows"),-7} {l.LayoutFile,-14} {l.Name}");
            if (l.Problem is not null) Out.WriteLine($"          ! {l.Problem}");
        }
        return Ok;
    }

    [System.Runtime.Versioning.SupportedOSPlatform("windows")]
    private static int Toggle(CommandLine cmd, bool enable)
    {
        var klid = cmd.Arg(0, "layout ID (KLID)");
        cmd.EnsureNoUnknownOptions();
        var system = new WindowsLayoutSystem();
        var layout = system.GetLayouts().FirstOrDefault(l => l.Klid.Equals(klid, StringComparison.OrdinalIgnoreCase))
            ?? throw new LayoutSystemException($"No layout with ID {klid}. Run 'keyservant list'.");
        if (enable) system.Enable(layout);
        else system.Disable(layout);
        Out.WriteLine($"{(enable ? "Added" : "Removed")} {layout.Name} {(enable ? "to" : "from")} your switcher.");
        return Ok;
    }

    [System.Runtime.Versioning.SupportedOSPlatform("windows")]
    private static int Import(CommandLine cmd)
    {
        var source = cmd.Arg(0, "layout ID or DLL path");
        var output = cmd.Option("output", "o") ?? throw new UsageException("Missing -o <file>.");
        cmd.EnsureNoUnknownOptions();

        string dllPath;
        string? name = null;
        if (File.Exists(source))
        {
            dllPath = source;
        }
        else
        {
            var layout = new WindowsLayoutSystem().GetLayouts().FirstOrDefault(l => l.Klid.Equals(source, StringComparison.OrdinalIgnoreCase))
                ?? throw new LayoutSystemException($"No file or layout ID '{source}'.");
            dllPath = Path.Combine(Environment.SystemDirectory, layout.LayoutFile);
            name = layout.Name;
        }
        var report = new ConversionReport();
        var imported = LayoutDllReader.Read(dllPath, report);
        PrintReport(report);
        if (name is not null) imported.Metadata.Name = name + " (copy)";
        imported.Metadata.DllName = "kbdks";
        imported.Metadata.LayoutId = null;
        File.WriteAllText(output, KeyservantFormat.Serialize(imported));
        Out.WriteLine($"Saved {output}. Change 'dllName' before installing, so it doesn't clash with other layouts.");
        return Ok;
    }

    [System.Runtime.Versioning.SupportedOSPlatform("windows")]
    private static int Install(CommandLine cmd, string[] rawArgs)
    {
        var path = cmd.Arg(0, "layout file");
        var temporary = cmd.Option("temporary");
        cmd.EnsureNoUnknownOptions();
        int? minutes = temporary is null ? null
            : int.TryParse(temporary, out var m) && m is > 0 and <= 7 * 24 * 60 ? m
            : throw new UsageException("--temporary needs a number of minutes (1 to 10080).");
        if (!Elevation.IsAdministrator) return Relaunch(rawArgs, Path.GetFullPath(path));

        var layout = Load(path);
        var result = LayoutDllBuilder.Build(layout);
        PrintValidation(result.Validation);
        var system = new WindowsLayoutSystem();
        var package = LayoutPackage.Create(layout, result);
        var installed = minutes is { } min
            ? system.InstallTemporary(package, TimeSpan.FromMinutes(min), Environment.ProcessPath ?? "keyservant.exe")
            : system.Install(package);
        Out.WriteLine($"Installed {installed.Name} as {installed.Klid} and added it to your switcher (Win+Space).");
        if (installed.TemporaryUntil is { } until)
            Out.WriteLine($"This is a test install: it is removed at {until.ToLocalTime():t}. Run 'keyservant keep {installed.Klid}' to keep it.");

        // Remember the KLID so the next install updates this layout instead of adding another one.
        if (layout.Metadata.LayoutId is null && path.EndsWith(".keyservant", StringComparison.OrdinalIgnoreCase))
        {
            layout.Metadata.LayoutId = installed.Klid;
            File.WriteAllText(path, KeyservantFormat.Serialize(layout));
            Out.WriteLine($"Saved layout ID {installed.Klid} in {path}.");
        }
        return Ok;
    }

    [System.Runtime.Versioning.SupportedOSPlatform("windows")]
    private static int Uninstall(CommandLine cmd, string[] rawArgs)
    {
        var klid = cmd.Arg(0, "layout ID (KLID)");
        var yes = cmd.Flag("yes") || cmd.Flag("y");
        cmd.EnsureNoUnknownOptions();
        var system = new WindowsLayoutSystem();
        if (!yes)
        {
            Out.WriteLine("This will remove:");
            foreach (var item in system.DescribeUninstall(klid)) Out.WriteLine($"  {item}");
            Out.Write("Continue? [y/N] ");
            if (!string.Equals(Console.ReadLine()?.Trim(), "y", StringComparison.OrdinalIgnoreCase)) return Ok;
        }
        if (!Elevation.IsAdministrator) return Relaunch([.. rawArgs, "--yes"], null);
        system.Uninstall(klid);
        Out.WriteLine($"Uninstalled {klid}. Files that are in use will be deleted at the next restart.");
        return Ok;
    }

    [System.Runtime.Versioning.SupportedOSPlatform("windows")]
    private static int Repair(CommandLine cmd, string[] rawArgs)
    {
        cmd.EnsureNoUnknownOptions();
        if (!Elevation.IsAdministrator) return Relaunch(rawArgs, null);
        var report = new WindowsLayoutSystem().Repair();
        foreach (var f in report.Fixed) Out.WriteLine(f);
        foreach (var p in report.Problems) Err.WriteLine($"problem: {p}");
        if (report.IsHealthy) Out.WriteLine("All custom layouts are healthy.");
        return report.Problems.Count == 0 ? Ok : Failed;
    }

    /// <summary>Runs this command again with administrator rights and prints its output.</summary>
    [System.Runtime.Versioning.SupportedOSPlatform("windows")]
    private static int Relaunch(string[] rawArgs, string? fullPath)
    {
        var args = rawArgs.ToArray();
        if (fullPath is not null) args[1] = fullPath; // the elevated process may start in another directory
        var log = Path.Combine(Path.GetTempPath(), $"keyservant-{Guid.NewGuid():N}.log");
        var exe = Environment.ProcessPath ?? throw new LayoutSystemException("Can't find the keyservant executable.");
        Out.WriteLine("Asking for administrator rights...");
        var code = Elevation.RunElevated(exe, [.. args, "--log", log]);
        if (File.Exists(log))
        {
            Out.Write(File.ReadAllText(log));
            File.Delete(log);
        }
        if (code is null)
        {
            Err.WriteLine("Cancelled: administrator rights were not granted.");
            return Declined;
        }
        return code.Value;
    }
}
