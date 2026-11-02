namespace Dawnforge.Core;

public static class GamePath
{
    public static bool TryResolve(string? input, out string sqpackPath, out string error)
    {
        sqpackPath = "";
        error = "";
        if (string.IsNullOrWhiteSpace(input))
        {
            error = "No game path given.";
            return false;
        }

        var full = Path.GetFullPath(input.Trim()).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        if (!Directory.Exists(full))
        {
            error = $"Game path does not exist: {full}";
            return false;
        }

        string[] candidates =
        [
            Path.Combine(full, "game", "sqpack", "ffxiv"),
            Path.Combine(full, "sqpack", "ffxiv"),
            full,
        ];

        foreach (var candidate in candidates)
        {
            var gameFolder = Path.GetDirectoryName(Path.GetDirectoryName(candidate));
            if (gameFolder == null || !string.Equals(Path.GetFileName(gameFolder), "game", StringComparison.Ordinal))
            {
                continue;
            }
            if (Directory.Exists(candidate)
                && string.Equals(Path.GetFileName(Path.GetDirectoryName(candidate)), "sqpack", StringComparison.Ordinal)
                && string.Equals(Path.GetFileName(candidate), "ffxiv", StringComparison.Ordinal)
                && File.Exists(Path.Combine(gameFolder, "ffxivgame.ver")))
            {
                sqpackPath = candidate;
                return true;
            }
        }

        error = $"Not an FFXIV install (expected game/sqpack/ffxiv and game/ffxivgame.ver): {full}";
        return false;
    }

    public static string? Resolve(string? input) => TryResolve(input, out var path, out _) ? path : null;
}
