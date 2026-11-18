using System.Diagnostics;

namespace HogwartsRP.Launcher;

public enum GamePlatform { Steam, Epic, Unknown }
public sealed record GameInstallation(string ExecutablePath, string InstallDirectory, GamePlatform Platform);
public sealed record ServerSelection(string Id, string DisplayName, Uri Endpoint);
public enum LaunchState { Idle, Detecting, Ready, Launching, LaunchRequested, Running, Failed }
public sealed record LaunchStatus(LaunchState State, string Message, GameInstallation? Installation = null, ServerSelection? Server = null);

public interface IGameInstallationDetector { ValueTask<GameInstallation?> DetectAsync(CancellationToken cancellationToken = default); }
public interface IPlatformGameLauncher { ValueTask LaunchAsync(GameInstallation installation, CancellationToken cancellationToken = default); }
public interface ICharacterCreationBridge
{
    ValueTask<bool> TryOpenCharacterCreationAsync(ServerSelection server, CancellationToken cancellationToken = default);
}

/// <summary>Searches conventional Windows installation roots for the executable, but does not execute game code itself.</summary>
public sealed class WindowsGameInstallationDetector(IEnumerable<string>? additionalRoots = null) : IGameInstallationDetector
{
    private static readonly string[] DefaultRoots = [
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86), "Steam", "steamapps", "common", "Hogwarts Legacy"),
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "Epic Games", "HogwartsLegacy")];
    public ValueTask<GameInstallation?> DetectAsync(CancellationToken cancellationToken = default)
    {
        foreach (var root in (additionalRoots ?? []).Concat(DefaultRoots).Where(Directory.Exists))
        {
            cancellationToken.ThrowIfCancellationRequested();
            var exe = Path.GetFullPath(Path.Combine(root, "Phoenix", "Binaries", "Win64", "HogwartsLegacy.exe"));
            var basePath = Path.GetFullPath(root);
            if (!exe.StartsWith(basePath + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase) || !File.Exists(exe)) continue;
            var platform = root.Contains("Steam", StringComparison.OrdinalIgnoreCase) ? GamePlatform.Steam : root.Contains("Epic", StringComparison.OrdinalIgnoreCase) ? GamePlatform.Epic : GamePlatform.Unknown;
            if (platform != GamePlatform.Unknown) return ValueTask.FromResult<GameInstallation?>(new(exe, basePath, platform));
        }
        return ValueTask.FromResult<GameInstallation?>(null);
    }
}

/// <summary>Starts the Steam app through its official URI, allowing Steam to perform ownership checks and authentication.</summary>
public sealed class SteamGameLauncher : IPlatformGameLauncher
{
    public ValueTask LaunchAsync(GameInstallation installation, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (installation.Platform != GamePlatform.Steam) throw new NotSupportedException("This platform adapter supports Steam installations only.");
        ValidateInstallation(installation);
        _ = Process.Start(new ProcessStartInfo("steam://run/990080") { UseShellExecute = true }) ?? throw new InvalidOperationException("Steam did not accept the launch request.");
        return ValueTask.CompletedTask;
    }
    private static void ValidateInstallation(GameInstallation game)
    {
        var root = Path.GetFullPath(game.InstallDirectory); var exe = Path.GetFullPath(game.ExecutablePath);
        if (!exe.StartsWith(root + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase) || !File.Exists(exe))
            throw new InvalidOperationException("The selected game executable is missing or outside its installation directory.");
    }
}

public sealed class LauncherService(IGameInstallationDetector detector, IPlatformGameLauncher platformLauncher, ICharacterCreationBridge? characterBridge = null)
{
    private readonly SemaphoreSlim _gate = new(1, 1);
    public LaunchStatus Status { get; private set; } = new(LaunchState.Idle, "Choose a server to begin.");
    public event Action<LaunchStatus>? StatusChanged;
    public async ValueTask<LaunchStatus> PlayAsync(ServerSelection server, CancellationToken cancellationToken = default)
    {
        if (Status.State is LaunchState.Launching or LaunchState.LaunchRequested or LaunchState.Running) return Status;
        if (!Uri.TryCreate(server.Endpoint.ToString(), UriKind.Absolute, out var uri) ||
            !((uri.Scheme is "https" or "wss") || (uri.IsLoopback && (uri.Scheme is "http" or "ws"))))
            return Publish(new(LaunchState.Failed, "Server endpoint must use HTTPS/WSS, or HTTP/WS on loopback for local development.", Server: server));
        if (!await _gate.WaitAsync(0, cancellationToken)) return Status;
        try
        {
            Publish(new(LaunchState.Detecting, "Checking for Hogwarts Legacy installation…", Server: server));
            var game = await detector.DetectAsync(cancellationToken);
            if (game is null) return Publish(new(LaunchState.Failed, "Hogwarts Legacy was not found. Install the game through a supported PC platform.", Server: server));
            try
            {
                Publish(new(LaunchState.Launching, "Asking the game platform to launch Hogwarts Legacy…", game, server));
                await platformLauncher.LaunchAsync(game, cancellationToken);
                var message = "Platform launch request accepted. Game startup is not yet confirmed; direct character-creation entry is unavailable.";
                if (characterBridge is not null)
                {
                    try { if (await characterBridge.TryOpenCharacterCreationAsync(server, cancellationToken)) message = "Platform launch request accepted. Character-creation integration reported an entry request."; }
                    catch { message = "Platform launch request accepted; character-creation integration failed. Use normal game startup."; }
                }
                Publish(new(LaunchState.LaunchRequested, message, game, server));
            }
            catch (Exception ex) { return Publish(new(LaunchState.Failed, $"Launch failed: {ex.Message}", game, server)); }
            return Status;
        }
        finally { _gate.Release(); }
    }
    private LaunchStatus Publish(LaunchStatus state) { Status = state; StatusChanged?.Invoke(state); return state; }
}
