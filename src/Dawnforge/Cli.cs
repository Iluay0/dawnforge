using System.Diagnostics;
using Dawnforge.Core;

namespace Dawnforge;

internal static class Cli
{
    private static readonly Lock ConsoleLock = new();

    public static void PrintUsage(TextWriter writer)
    {
        writer.WriteLine("Usage: dawnforge <input...> [-o <dir>] [--game <path>] [--overwrite]");
        writer.WriteLine();
        writer.WriteLine("Upgrades .ttmp, .ttmp2 and .pmp modpacks to Dawntrail and writes <input name>.pmp.");
        writer.WriteLine();
        writer.WriteLine("  -o, --output <dir>  Output folder (default: outputFolder from the config)");
        writer.WriteLine("  --game <path>       FFXIV install folder or its game folder (default: gamePath from the config)");
        writer.WriteLine("  --overwrite         Replace existing output files");
        writer.WriteLine("  -h, --help          Show this help");
        writer.WriteLine();
        writer.WriteLine($"Config: {AppPaths.ConfigFile}");
    }

    public static async Task<int> Run(string[] args)
    {
        var inputs = new List<string>();
        string? output = null;
        string? game = null;
        var overwrite = false;

        for (var i = 0; i < args.Length; i++)
        {
            var arg = args[i];
            switch (arg)
            {
                case "-h":
                case "--help":
                    PrintUsage(Console.Out);
                    return 0;
                case "-o":
                case "--output":
                    if (++i >= args.Length)
                    {
                        return UsageError($"{arg} needs a folder.");
                    }
                    output = args[i];
                    break;
                case "--game":
                    if (++i >= args.Length)
                    {
                        return UsageError("--game needs a path.");
                    }
                    game = args[i];
                    break;
                case "--overwrite":
                    overwrite = true;
                    break;
                case "--":
                    inputs.AddRange(args.Skip(i + 1));
                    i = args.Length;
                    break;
                default:
                    if (arg.StartsWith('-') && arg.Length > 1)
                    {
                        return UsageError($"Unknown option: {arg}");
                    }
                    inputs.Add(arg);
                    break;
            }
        }

        if (inputs.Count == 0)
        {
            return UsageError("No input files given.");
        }

        var config = DawnforgeConfig.Load();
        game ??= config.GamePath;
        output ??= config.OutputFolder;

        if (string.IsNullOrWhiteSpace(game))
        {
            return UsageError("No game path: pass --game or set gamePath in the config.");
        }
        if (string.IsNullOrWhiteSpace(output))
        {
            return UsageError("No output folder: pass -o or set outputFolder in the config.");
        }
        if (!GamePath.TryResolve(game, out _, out var gameError))
        {
            Error(gameError);
            return 1;
        }

        var outputDirectory = Path.GetFullPath(output);
        var failed = 0;
        var jobs = new List<(string Input, string Output)>();
        var claimed = new HashSet<string>(OperatingSystem.IsWindows() ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal);

        foreach (var raw in inputs)
        {
            var input = Path.GetFullPath(raw);
            if (!SupportedFormats.IsSupported(input))
            {
                Warn($"Skipping unsupported file: {input}");
                continue;
            }
            if (!File.Exists(input))
            {
                Error($"Input not found: {input}");
                failed++;
                continue;
            }

            var target = Path.Combine(outputDirectory, Path.GetFileNameWithoutExtension(input) + ".pmp");
            if (string.Equals(target, input, OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal))
            {
                Error($"Output would replace the input file, skipping: {input}");
                failed++;
                continue;
            }
            if (!claimed.Add(target))
            {
                Error($"Another input already writes {target}, skipping: {input}");
                failed++;
                continue;
            }
            if (File.Exists(target) && !overwrite)
            {
                Error($"Output exists (use --overwrite): {target}");
                failed++;
                continue;
            }
            jobs.Add((input, target));
        }

        if (jobs.Count == 0)
        {
            return failed > 0 ? 1 : 0;
        }

        using var upgrader = new ModpackUpgrader(line => WriteLine($"    {line}"));
        var initTimer = Stopwatch.StartNew();
        WriteLine("Loading game data (first run builds the cache, this can take a while)...");
        try
        {
            await upgrader.Initialise(game);
        }
        catch (Exception ex)
        {
            Error($"Could not initialise: {ex.Message}");
            return 1;
        }
        WriteLine($"Game data ready in {initTimer.Elapsed.TotalSeconds:0.0}s (cache: {AppPaths.CacheDirectory})");

        for (var i = 0; i < jobs.Count; i++)
        {
            var (input, target) = jobs[i];
            var prefix = $"[{i + 1}/{jobs.Count}] {Path.GetFileName(input)}";
            WriteLine($"{prefix}: upgrading -> {target}");
            var timer = Stopwatch.StartNew();
            var result = await upgrader.Upgrade(input, target, new ConsoleProgress(prefix));
            var elapsed = $"{timer.Elapsed.TotalSeconds:0.0}s";
            switch (result.Status)
            {
                case UpgradeStatus.Changed:
                    WriteLine($"{prefix}: upgraded ({elapsed})");
                    break;
                case UpgradeStatus.Unchanged:
                    WriteLine($"{prefix}: {result.Message} ({elapsed})");
                    break;
                case UpgradeStatus.AlreadyDawntrail:
                    Warn($"{prefix}: {result.Message}");
                    break;
                default:
                    Error($"{prefix}: failed: {result.Message}");
                    failed++;
                    break;
            }
        }

        return failed > 0 ? 1 : 0;
    }

    private static int UsageError(string message)
    {
        Error(message);
        Console.Error.WriteLine("Run 'dawnforge --help' for usage.");
        return 2;
    }

    private static void WriteLine(string message)
    {
        lock (ConsoleLock)
        {
            Console.Out.WriteLine(message);
        }
    }

    private static void Warn(string message)
    {
        lock (ConsoleLock)
        {
            Console.Out.WriteLine($"warning: {message}");
        }
    }

    private static void Error(string message)
    {
        lock (ConsoleLock)
        {
            Console.Error.WriteLine($"error: {message}");
        }
    }

    private sealed class ConsoleProgress(string prefix) : IProgress<(int current, int total, string message)>
    {
        private string? _last;

        public void Report((int current, int total, string message) value)
        {
            var line = value.total > 0
                ? $"{prefix}: {value.message} ({value.current + 1}/{value.total})"
                : $"{prefix}: {value.message}";
            if (line == _last)
            {
                return;
            }
            _last = line;
            WriteLine(line);
        }
    }
}
