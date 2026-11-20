using HogwartsRP.Server;

namespace HogwartsRP.Server.Tests;

public sealed class GameAdapterProtocolTests
{
    [Fact]
    public void AcceptsSupportedHandshakeWithBoundedCapabilities()
    {
        var valid = GameAdapterProtocol.TryValidateHello(
            1, "0.1.0", "Steam-1.0", "4.0.0",
            ["local_position_read", "camera_rotation_read"],
            out var error);

        Assert.True(valid);
        Assert.Null(error);
    }

    [Theory]
    [InlineData(2)]
    [InlineData(0)]
    public void RejectsUnsupportedProtocolVersions(int version)
    {
        var valid = GameAdapterProtocol.TryValidateHello(
            version, "0.1.0", null, null, null, out var error);

        Assert.False(valid);
        Assert.NotNull(error);
    }

    [Fact]
    public void RejectsMalformedAndDuplicateCapabilities()
    {
        Assert.False(GameAdapterProtocol.TryValidateHello(
            1, "0.1.0", null, null, ["LocalPosition"], out _));
        Assert.False(GameAdapterProtocol.TryValidateHello(
            1, "0.1.0", null, null, ["local_position_read", "local_position_read"], out _));
    }

    [Fact]
    public void RejectsOversizedCapabilityLists()
    {
        var capabilities = Enumerable.Range(0, GameAdapterProtocol.MaximumCapabilities + 1)
            .Select(index => $"capability_{index}")
            .ToArray();

        Assert.False(GameAdapterProtocol.TryValidateHello(
            1, "0.1.0", null, null, capabilities, out _));
    }

    [Theory]
    [InlineData(0, -1, true)]
    [InlineData(1, 0, true)]
    [InlineData(1, 1, false)]
    [InlineData(0, 0, false)]
    [InlineData(-1, -1, false)]
    public void HeartbeatsRequireStrictlyIncreasingNonnegativeSequences(long sequence, long previous, bool expected)
    {
        Assert.Equal(expected, GameAdapterProtocol.IsValidHeartbeatSequence(sequence, previous));
    }

    [Fact]
    public void RejectsMissingOrMalformedAdapterVersion()
    {
        Assert.False(GameAdapterProtocol.TryValidateHello(
            1, null, null, null, null, out _));
        Assert.False(GameAdapterProtocol.TryValidateHello(
            1, "bad version", null, null, null, out _));
    }
}
