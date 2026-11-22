using ContextGuru.Core.Template;

namespace ContextGuru.Tests;

public class HandoffTemplateTests
{
    const string Valid = "## Goal\nG\n## Done\nD\n## Decisions\nX\n## Open tasks\nO\n## Next steps\nN\n";

    static string[] Messages(string summary, string body) =>
        [.. HandoffTemplate.Validate(summary, body).Select(e => e.Message)];

    [Fact] public void Validate_ValidBody_ReturnsNoErrors() => Assert.Empty(HandoffTemplate.Validate("s", Valid));

    [Fact]
    public void Validate_MissingDecisions_NamesIt()
    {
        var e = Assert.Single(HandoffTemplate.Validate("s", Valid.Replace("## Decisions\nX\n", "")));
        Assert.Equal("Missing required heading: \"## Decisions\". Required headings, in order: Goal, Done, Decisions, Open tasks, Next steps.", e.Message);
    }

    [Fact]
    public void Validate_DuplicateGoal_Reports() =>
        Assert.Contains(HandoffTemplate.Validate("s", Valid + "## Goal\nagain\n"), e => e.Message == "Heading \"## Goal\" appears more than once.");

    // A repeated heading that comes after later sections breaks the order too, and the retry needs both facts.
    [Fact]
    public void Validate_DuplicateGoalAtEnd_ReportsDuplicateAndOrder() =>
        Assert.Equal(
            [
                "Heading \"## Goal\" appears more than once.",
                "Heading \"## Goal\" is out of order. Required order: Goal, Done, Decisions, Open tasks, Next steps.",
            ],
            Messages("s", Valid + "## Goal\nagain\n"));

    [Fact]
    public void Validate_DuplicateInPlace_ReportsOnlyDuplicate() =>
        Assert.Equal(["Heading \"## Goal\" appears more than once."], Messages("s", "## Goal\nG\n## Goal\nagain\n" + Valid.Replace("## Goal\nG\n", "")));

    [Fact]
    public void Validate_DoneBeforeGoal_IsOutOfOrder() =>
        Assert.Contains(HandoffTemplate.Validate("s", "## Done\nD\n## Goal\nG\n## Decisions\nX\n## Open tasks\nO\n## Next steps\nN\n"),
            e => e.Message == "Heading \"## Done\" is out of order. Required order: Goal, Done, Decisions, Open tasks, Next steps.");

    [Theory]
    [InlineData("```")]
    [InlineData("~~~")]
    public void Validate_HeadingInsideFence_IsIgnored(string fence) =>
        Assert.Contains(HandoffTemplate.Validate("s", Valid.Replace("## Done\nD\n", $"{fence}\n## Done\n{fence}\n")),
            e => e.Message.StartsWith("Missing required heading: \"## Done\"", StringComparison.Ordinal));

    [Fact]
    public void Validate_UnclosedFence_IgnoresRest() =>
        Assert.Contains(HandoffTemplate.Validate("s", "## Goal\nG\n```\n## Done\nD\n## Decisions\nX\n## Open tasks\nO\n## Next steps\nN\n"),
            e => e.Message.StartsWith("Missing required heading: \"## Done\"", StringComparison.Ordinal));

    [Fact]
    public void Validate_ExtraLevelTwoHeading_IsAllowed() =>
        Assert.Empty(HandoffTemplate.Validate("s", Valid + "## Notes\nextra\n"));

    [Fact]
    public void Validate_LevelThreeHeading_DoesNotCount() =>
        Assert.Contains(HandoffTemplate.Validate("s", Valid.Replace("## Done\n", "### Done\n")),
            e => e.Message.StartsWith("Missing required heading: \"## Done\"", StringComparison.Ordinal));

    [Fact]
    public void Validate_PaddedMixedCaseHeading_IsAccepted() =>
        Assert.Empty(HandoffTemplate.Validate("s", Valid.Replace("## Goal", "##  goal  ")));

    [Fact]
    public void Validate_CrLfBody_IsAccepted() =>
        Assert.Empty(HandoffTemplate.Validate("s", Valid.Replace("\n", "\r\n")));

    [Fact] public void Validate_EmptySummary_Reports() => Assert.Equal(["Summary is empty."], Messages("", Valid));

    [Fact] public void Validate_Summary200Chars_IsOk() => Assert.Empty(HandoffTemplate.Validate(new string('a', 200), Valid));

    [Fact]
    public void Validate_Summary201Chars_Reports() =>
        Assert.Equal(["Summary is 201 characters; the maximum is 200."], Messages(new string('a', 201), Valid));

    [Fact]
    public void Validate_SummaryWithNewline_Reports() =>
        Assert.Equal(["Summary must be one line."], Messages("a\nb", Valid));

    // The spec forbids CR and LF in the summary. Only the length is counted on trimmed text.
    [Theory]
    [InlineData("ready\r\n")]
    [InlineData("\nready")]
    public void Validate_SummaryWithEdgeNewline_Reports(string summary) =>
        Assert.Equal(["Summary must be one line."], Messages(summary, Valid));

    // A heading is a line that starts with "## ". Leading spaces are not trimmed away.
    [Fact]
    public void Validate_IndentedFirstHeading_IsNotAHeading() =>
        Assert.Contains(Messages("s", "  " + Valid), m => m.StartsWith("Missing required heading: \"## Goal\"", StringComparison.Ordinal));

    [Fact]
    public void Validate_MultiLineSummaryOver200_ReportsBothProblems() =>
        Assert.Equal(["Summary must be one line.", "Summary is 301 characters; the maximum is 200."], Messages(new string('a', 150) + "\n" + new string('b', 150), Valid));

    // The spec says Validate returns every problem, so an empty body also lacks all five headings.
    [Theory]
    [InlineData("")]
    [InlineData("  \n ")]
    public void Validate_EmptyBody_ReportsEmptyAndEveryMissingHeading(string body)
    {
        var messages = Messages("s", body);
        Assert.Equal("Body is empty.", messages[0]);
        Assert.Equal(["Goal", "Done", "Decisions", "Open tasks", "Next steps"],
            messages.Skip(1).Select(m => m.Split('"')[1][3..]));
    }

    [Fact]
    public void Validate_Body20000Chars_HasNoLengthError() =>
        Assert.DoesNotContain(Messages("s", Valid + new string('a', 20_000 - Valid.Length)), m => m.StartsWith("Body is", StringComparison.Ordinal));

    [Fact]
    public void Validate_Body20001Chars_Reports() =>
        Assert.Contains("Body is 20001 characters; the maximum is 20000.", Messages("s", Valid + new string('a', 20_001 - Valid.Length)));

    [Theory]
    [InlineData("none")]
    [InlineData("n/a")]
    [InlineData("- none")]
    [InlineData("* N/A.")]
    [InlineData("None.")]
    [InlineData("")]
    public void NextStepsHasContent_Placeholder_IsFalse(string next) =>
        Assert.False(HandoffTemplate.NextStepsHasContent(Valid.Replace("## Next steps\nN\n", $"## Next steps\n{next}\n")));

    // The spec treats the whole trimmed section as the placeholder, with at most one leading marker.
    [Theory]
    [InlineData("none\nn/a")]
    [InlineData("- none\n- n/a")]
    [InlineData("-- none")]
    [InlineData("none\n\nsoon")]
    public void NextStepsHasContent_NotASinglePlaceholder_IsTrue(string next) =>
        Assert.True(HandoffTemplate.NextStepsHasContent(Valid.Replace("## Next steps\nN\n", $"## Next steps\n{next}\n")));

    [Fact]
    public void NextStepsHasContent_PlaceholderBeforeAnotherHeading_IsFalse() =>
        Assert.False(HandoffTemplate.NextStepsHasContent(Valid.Replace("## Next steps\nN\n", "## Next steps\n- none\n## Notes\nextra\n")));

    [Fact]
    public void NextStepsHasContent_RealStep_IsTrue() =>
        Assert.True(HandoffTemplate.NextStepsHasContent(Valid.Replace("## Next steps\nN\n", "## Next steps\nRun the tests\n")));

    [Fact] public void NormalizeName_TrimsAndLowers() => Assert.Equal("auth-refactor", HandoffTemplate.NormalizeName("  Auth-Refactor "));

    [Theory]
    [InlineData("a", true)]
    [InlineData("0-x_y", true)]
    [InlineData("-x", false)]
    [InlineData("a b", false)]
    [InlineData("café", false)]
    [InlineData("", false)]
    public void IsValidName_Cases(string name, bool expected) => Assert.Equal(expected, HandoffTemplate.IsValidName(name));

    [Fact] public void IsValidName_64Chars_IsTrue() => Assert.True(HandoffTemplate.IsValidName(new string('a', 64)));

    [Fact] public void IsValidName_65Chars_IsFalse() => Assert.False(HandoffTemplate.IsValidName(new string('a', 65)));

    [Fact]
    public void BadNameMessage_MatchesSpec() =>
        Assert.Equal("Name \"A b\" is not valid. Use lower-case letters, digits, \"-\" and \"_\", starting with a letter or digit, up to 64 characters.",
            HandoffTemplate.BadNameMessage("A b"));
}
