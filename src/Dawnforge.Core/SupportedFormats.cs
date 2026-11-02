namespace Dawnforge.Core;

public static class SupportedFormats
{
    public static IReadOnlyList<string> Extensions { get; } = [".ttmp", ".ttmp2", ".pmp"];

    public static bool IsSupported(string path) =>
        Extensions.Any(e => string.Equals(Path.GetExtension(path), e, StringComparison.OrdinalIgnoreCase));
}
