using HogwartsRP.GameIntegration.Abstractions;
using HogwartsRP.Launcher;

namespace HogwartsRP.Launcher.Tests;

public sealed class CharacterCreationConsoleFlowTests
{
    [Fact]
    public async Task RunAsync_submits_valid_character_and_reports_server_id()
    {
        CharacterSubmission? received = null;
        var input = new StringReader("yes\nRowan Ashford\nRavenclaw\nCoastal upbringing\nCurious fifth-year\nAsh\nPhoenix feather\nSupple\n11.5\n");
        var output = new StringWriter();
        var flow = new CharacterCreationConsoleFlow(input, output, (submission, _) =>
        {
            received = submission;
            return ValueTask.FromResult(new CharacterSubmissionResult(true,
                new RegisteredCharacter(Guid.NewGuid(), "server-account", submission.Name, null, submission.House,
                    submission.Wand, submission.Background, submission.RoleplayProfile)));
        });

        var submitted = await flow.RunAsync();

        Assert.True(submitted);
        Assert.NotNull(received);
        Assert.Equal("Rowan Ashford", received.Name);
        Assert.Equal("Ravenclaw", received.House);
        Assert.Equal(11.5m, received.Wand?.Length);
        Assert.Contains("Character registered: Rowan Ashford", output.ToString());
    }

    [Fact]
    public async Task RunAsync_does_not_submit_invalid_character()
    {
        var submitCalled = false;
        var input = new StringReader("y\nName123\nGryffindor\n\n\n\n\n\n\n");
        var output = new StringWriter();
        var flow = new CharacterCreationConsoleFlow(input, output, (_, _) =>
        {
            submitCalled = true;
            return ValueTask.FromResult(new CharacterSubmissionResult(true));
        });

        var submitted = await flow.RunAsync();

        Assert.False(submitted);
        Assert.False(submitCalled);
        Assert.Contains("Character was not submitted", output.ToString());
    }

    [Fact]
    public async Task RunAsync_skips_when_user_declines()
    {
        var submitCalled = false;
        var flow = new CharacterCreationConsoleFlow(new StringReader("no\n"), new StringWriter(), (_, _) =>
        {
            submitCalled = true;
            return ValueTask.FromResult(new CharacterSubmissionResult(true));
        });

        Assert.False(await flow.RunAsync());
        Assert.False(submitCalled);
    }

    [Fact]
    public async Task RunAsync_reports_server_rejection()
    {
        var output = new StringWriter();
        var input = new StringReader("y\nAda Vale\nHufflepuff\n\n\n\n\n\n\n");
        var flow = new CharacterCreationConsoleFlow(input, output,
            (_, _) => ValueTask.FromResult(new CharacterSubmissionResult(false, Error: "Duplicate character.")));

        Assert.False(await flow.RunAsync());
        Assert.Contains("Duplicate character", output.ToString());
    }
}
