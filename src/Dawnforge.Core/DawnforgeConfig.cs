using System.Text.Json;

namespace Dawnforge.Core;

public sealed class DawnforgeConfig
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true,
    };

    public string? GamePath { get; set; }
    public string? OutputFolder { get; set; }

    public static DawnforgeConfig Load(string? path = null)
    {
        path ??= AppPaths.ConfigFile;
        if (!File.Exists(path))
        {
            return new DawnforgeConfig();
        }
        try
        {
            return JsonSerializer.Deserialize<DawnforgeConfig>(File.ReadAllText(path), JsonOptions) ?? new DawnforgeConfig();
        }
        catch (JsonException)
        {
            return new DawnforgeConfig();
        }
    }

    public void Save(string? path = null)
    {
        path ??= AppPaths.ConfigFile;
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path))!);
        File.WriteAllText(path, JsonSerializer.Serialize(this, JsonOptions));
    }
}
