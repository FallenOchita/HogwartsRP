using HogwartsRP.GameIntegration.Abstractions;
using HogwartsRP.Launcher;

namespace HogwartsRP.Resources.Tests;

public class LauncherAndCharacterTests
{
    private sealed class Detector(GameInstallation? value) : IGameInstallationDetector { public ValueTask<GameInstallation?> DetectAsync(CancellationToken cancellationToken = default) => ValueTask.FromResult(value); }
    private sealed class TestLauncher(bool fail = false) : IPlatformGameLauncher
    { public int Calls; public ValueTask LaunchAsync(GameInstallation installation, CancellationToken cancellationToken = default) { Calls++; return fail ? ValueTask.FromException(new IOException("platform unavailable")) : ValueTask.CompletedTask; } }
    private sealed class Repository : ICharacterRepository
    { public RegisteredCharacter? Saved; public ValueTask<RegisteredCharacter> RegisterAsync(RegisteredCharacter character, CancellationToken cancellationToken = default) { Saved = character; return ValueTask.FromResult(character); } }

    private static ServerSelection Server() => new("local", "Local", new Uri("wss://localhost:7777"));
    [Fact]
    public async Task LauncherAcceptsRequestAndReportsRunning()
    {
        var launcher = new TestLauncher(); var service = new LauncherService(new Detector(new("C:\\Games\\Hogwarts Legacy\\Phoenix\\Binaries\\Win64\\HogwartsLegacy.exe", "C:\\Games\\Hogwarts Legacy", GamePlatform.Steam)), launcher);
        var status = await service.PlayAsync(Server()); Assert.Equal(LaunchState.LaunchRequested, status.State); await service.PlayAsync(Server()); Assert.Equal(1, launcher.Calls);
    }
    [Fact]
    public async Task LauncherHandlesMissingInstallAndFailure()
    {
        var missing = await new LauncherService(new Detector(null), new TestLauncher()).PlayAsync(Server()); Assert.Equal(LaunchState.Failed, missing.State);
        var bad = await new LauncherService(new Detector(new("C:\\x\\game.exe", "C:\\x", GamePlatform.Steam)), new TestLauncher(true)).PlayAsync(Server()); Assert.Equal(LaunchState.Failed, bad.State);
    }
    [Fact]
    public async Task InstallationDetectorValidatesExpectedExecutable()
    {
        var root = Path.Combine(Path.GetTempPath(), "Steam-" + Guid.NewGuid().ToString("N")); var exe = Path.Combine(root, "Phoenix", "Binaries", "Win64", "HogwartsLegacy.exe");
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(exe)!);
            Assert.Null(await new WindowsGameInstallationDetector([root]).DetectAsync());
            await File.WriteAllBytesAsync(exe, []);
            var found = await new WindowsGameInstallationDetector([root]).DetectAsync();
            Assert.NotNull(found); Assert.Equal(Path.GetFullPath(exe), found.ExecutablePath);
        }
        finally { if (Directory.Exists(root)) Directory.Delete(root, true); }
    }
    [Fact]
    public async Task CharacterOwnershipComesFromAuthenticatedSessionAndInvalidDataIsRejected()
    {
        var repo = new Repository(); var service = new CharacterSubmissionService(repo);
        var result = await service.SubmitAsync("session-player", new("Ada", "{}", "ravenclaw", new("oak", "phoenix", 11, "supple"), "Scholar", new("Healer", "A careful student.")));
        Assert.True(result.Success, result.Error); Assert.Equal("session-player", result.Character!.OwnerPlayerId);
        var invalid = await service.SubmitAsync("session-player", new("", "[]", "NoHouse", null, null, null)); Assert.False(invalid.Success);
    }
}
