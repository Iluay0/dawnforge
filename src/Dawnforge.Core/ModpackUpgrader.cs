using System.Diagnostics;
using xivModdingFramework.Cache;
using xivModdingFramework.General.Enums;
using xivModdingFramework.Helpers;
using xivModdingFramework.Mods.FileTypes;
using xivModdingFramework.Textures.Enums;
using FrameworkUpgrader = xivModdingFramework.Mods.ModpackUpgrader;

namespace Dawnforge.Core;

public sealed class ModpackUpgrader : IDisposable
{
    private static readonly SemaphoreSlim Gate = new(1, 1);

    private readonly CallbackTraceListener? _listener;
    private string? _tempDirectory;
    private bool _disposed;

    public ModpackUpgrader(Action<string>? log = null)
    {
        if (log != null)
        {
            _listener = new CallbackTraceListener(log);
            Trace.Listeners.Add(_listener);
        }
    }

    public bool IsInitialised { get; private set; }

    public async Task Initialise(string gamePath)
    {
        if (!GamePath.TryResolve(gamePath, out var sqpackPath, out var error))
        {
            throw new DirectoryNotFoundException(error);
        }

        NativeLibraries.Register();
        Directory.CreateDirectory(AppPaths.CacheDirectory);
        XivCache.CacheDirectory = AppPaths.CacheDirectory;

        _tempDirectory = IOUtil.GetUniqueSubfolder(Path.GetTempPath(), "dawnforge-");
        XivCache.FrameworkSettings.TempDirectory = _tempDirectory;
        XivCache.FrameworkSettings.DefaultTextureFormat = XivTexFormat.BC7;
        AppDomain.CurrentDomain.ProcessExit += OnProcessExit;

        await Task.Run(() => XivCache.SetGameInfo(new DirectoryInfo(sqpackPath), XivLanguage.English, false));
        IsInitialised = true;
    }

    public async Task<UpgradeResult> Upgrade(
        string inputPath,
        string outputPmpPath,
        IProgress<(int current, int total, string message)>? progress = null,
        CancellationToken? cancellationToken = null)
    {
        if (!IsInitialised)
        {
            throw new InvalidOperationException("Initialise must be called before Upgrade.");
        }

        var token = cancellationToken ?? CancellationToken.None;
        await Gate.WaitAsync(token);
        try
        {
            return await Task.Run(() => UpgradeCore(inputPath, outputPmpPath, progress, token));
        }
        finally
        {
            Gate.Release();
        }
    }

    private static async Task<UpgradeResult> UpgradeCore(
        string inputPath,
        string outputPmpPath,
        IProgress<(int current, int total, string message)>? progress,
        CancellationToken token)
    {
        inputPath = Path.GetFullPath(inputPath);
        outputPmpPath = Path.GetFullPath(outputPmpPath);

        if (!File.Exists(inputPath))
        {
            return new UpgradeResult(UpgradeStatus.Failed, $"Input file not found: {inputPath}");
        }
        if (!SupportedFormats.IsSupported(inputPath))
        {
            return new UpgradeResult(UpgradeStatus.Failed, $"Unsupported file type: {Path.GetExtension(inputPath)}");
        }
        if (!string.Equals(Path.GetExtension(outputPmpPath), ".pmp", StringComparison.OrdinalIgnoreCase))
        {
            return new UpgradeResult(UpgradeStatus.Failed, "Output path must end in .pmp");
        }

        var isPmp = TTMP.GetModpackType(inputPath) == TTMP.EModpackType.Pmp;
        var outputDirectory = Path.GetDirectoryName(outputPmpPath)!;
        var stagingPath = Path.Combine(outputDirectory, $".{Path.GetFileNameWithoutExtension(outputPmpPath)}.{Guid.NewGuid():N}.pmp");

        try
        {
            token.ThrowIfCancellationRequested();
            progress?.Report((0, 0, "Loading modpack..."));
            var (data, anyChanges) = await FrameworkUpgrader.UpgradeModpack(inputPath, true, progress);
            if (data == null)
            {
                return new UpgradeResult(UpgradeStatus.Failed, "Could not read the modpack.");
            }

            token.ThrowIfCancellationRequested();
            progress?.Report((0, 0, "Writing modpack..."));
            Directory.CreateDirectory(outputDirectory);
            await data.WriteModpack(stagingPath, true);
            File.Move(stagingPath, outputPmpPath, true);

            return anyChanges
                ? new UpgradeResult(UpgradeStatus.Changed)
                : new UpgradeResult(UpgradeStatus.Unchanged, "No upgrade needed; written unchanged.");
        }
        catch (NotImplementedException) when (isPmp)
        {
            return new UpgradeResult(UpgradeStatus.AlreadyDawntrail, "Already in Dawntrail format (PMP file version 4 or newer); nothing to upgrade.");
        }
        catch (OperationCanceledException)
        {
            return new UpgradeResult(UpgradeStatus.Failed, "Cancelled.");
        }
        catch (Exception ex)
        {
            return new UpgradeResult(UpgradeStatus.Failed, ex.Message);
        }
        finally
        {
            try
            {
                if (File.Exists(stagingPath))
                {
                    File.Delete(stagingPath);
                }
                IOUtil.ClearTempFolder();
            }
            catch (IOException)
            {
            }
        }
    }

    private void OnProcessExit(object? sender, EventArgs e) => CleanupTemp();

    private void CleanupTemp()
    {
        if (_tempDirectory == null)
        {
            return;
        }
        try
        {
            if (Directory.Exists(_tempDirectory))
            {
                Directory.Delete(_tempDirectory, true);
            }
        }
        catch (IOException)
        {
        }
        catch (UnauthorizedAccessException)
        {
        }
        _tempDirectory = null;
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }
        _disposed = true;
        AppDomain.CurrentDomain.ProcessExit -= OnProcessExit;
        CleanupTemp();
        if (_listener != null)
        {
            Trace.Listeners.Remove(_listener);
            _listener.Dispose();
        }
    }
}
