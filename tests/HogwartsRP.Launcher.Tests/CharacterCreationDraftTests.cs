using HogwartsRP.Launcher;

namespace HogwartsRP.Launcher.Tests;

public sealed class CharacterCreationDraftTests
{
    [Fact]
    public void TryBuildSubmission_trims_fields_and_normalizes_house()
    {
        var draft = new CharacterCreationDraft
        {
            Name = "  Rowan Ashford  ",
            House = "ravenclaw",
            Background = "  Raised near the coast. ",
            Biography = "  A curious fifth-year. ",
            WandWood = "  Ash ",
            WandCore = "Phoenix feather",
            WandLength = 11.5m
        };

        Assert.True(draft.TryBuildSubmission(out var submission, out var errors));
        Assert.Empty(errors);
        Assert.NotNull(submission);
        Assert.Equal("Rowan Ashford", submission.Name);
        Assert.Equal("Ravenclaw", submission.House);
        Assert.Equal("Ash", submission.Wand?.Wood);
        Assert.Equal("A curious fifth-year.", submission.RoleplayProfile?.Biography);
        Assert.Null(submission.AppearanceJson);
    }

    [Theory]
    [InlineData("", "Gryffindor")]
    [InlineData("A Name", "Beauxbatons")]
    [InlineData("Name123", "Slytherin")]
    public void TryBuildSubmission_rejects_invalid_name_or_house(string name, string house)
    {
        var draft = new CharacterCreationDraft { Name = name, House = house };

        Assert.False(draft.TryBuildSubmission(out var submission, out var errors));
        Assert.Null(submission);
        Assert.NotEmpty(errors);
    }

    [Fact]
    public void TryBuildSubmission_rejects_out_of_range_wand_length()
    {
        var draft = new CharacterCreationDraft
        {
            Name = "Ada Vale",
            House = "Hufflepuff",
            WandLength = 17m
        };

        Assert.False(draft.TryBuildSubmission(out _, out var errors));
        Assert.Contains(errors, error => error.Contains("Wand length", StringComparison.Ordinal));
    }

    [Fact]
    public void TryBuildSubmission_leaves_optional_wand_and_biography_empty()
    {
        var draft = new CharacterCreationDraft { Name = "Morgan Reed", House = "Gryffindor" };

        Assert.True(draft.TryBuildSubmission(out var submission, out _));
        Assert.Null(submission!.Wand);
        Assert.Null(submission.RoleplayProfile);
    }
}
