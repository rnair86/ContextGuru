using ContextGuru.Install;

namespace ContextGuru.Tests;

public sealed class TemplateTests
{
    private const string Marker = "<!-- contextguru:managed v1 -->";

    [Theory]
    [InlineData("save-context", "Leave out secrets and file contents.")]
    [InlineData("get-context", "Follow the `agent_instructions` in the result.")]
    [InlineData("list-contexts", "Show name, summary, updated time and source agent as a table.")]
    [InlineData("remove-context", "Only after they confirm")]
    public void EmbeddedTemplateStartsWithMarkerAndContainsApprovedText(string command, string sentence)
    {
        var template = TemplateRenderer.ReadTemplate(command).ReplaceLineEndings("\n");

        Assert.StartsWith(Marker + "\n", template);
        Assert.Contains(sentence, template);
    }

    [Theory]
    [InlineData("claude", ".claude.json", ConfigFormat.Json, ".claude/commands", ".md", "$ARGUMENTS")]
    [InlineData("codex", ".codex/config.toml", ConfigFormat.Toml, ".agents/skills", ".md", "the text after the skill name")]
    [InlineData("cursor", ".cursor/mcp.json", ConfigFormat.Json, ".cursor/commands", ".md", "the text after the command name")]
    public void TargetUsesSpikeLocationsUnderBaseFolder(string agent, string configPath, ConfigFormat format,
        string commandFolder, string extension, string args)
    {
        using var temp = new TempDir();
        var target = AgentTargets.For(agent, temp.Path);

        Assert.Equal(agent, target.Agent);
        Assert.Equal(Path.GetFullPath(configPath, temp.Path), target.ConfigPath);
        Assert.Equal(format, target.ConfigFormat);
        Assert.Equal(Path.GetFullPath(commandFolder, temp.Path), target.CommandFolder);
        Assert.Equal(extension, target.FileExtension);
        Assert.Equal(args, target.ArgsPlaceholder);
        Assert.False(string.IsNullOrWhiteSpace(target.ToolName));
        Assert.Equal(Marker, target.MarkerLine);
    }

    [Fact]
    public void UnknownAgentIsRejected()
    {
        using var temp = new TempDir();

        Assert.Throws<ArgumentException>(() => AgentTargets.For("unknown", temp.Path));
    }

    [Theory]
    [InlineData("claude")]
    [InlineData("codex")]
    [InlineData("cursor")]
    public void EmbeddedTemplatesRenderAllCommandsForTarget(string agent)
    {
        using var temp = new TempDir();
        var target = AgentTargets.For(agent, temp.Path);
        string[] commands = ["save-context", "get-context", "list-contexts", "remove-context"];

        foreach (var command in commands)
        {
            var rendered = TemplateRenderer.Render(TemplateRenderer.ReadTemplate(command), target).ReplaceLineEndings("\n");
            var tool = command.Replace('-', '_');

            Assert.DoesNotContain("{{", rendered);
            Assert.Contains($"`{string.Format(System.Globalization.CultureInfo.InvariantCulture, target.ToolName, tool)}`", rendered);
            Assert.Contains("`projectPath` set to the current folder", rendered);
            if (command != "list-contexts")
            {
                var argument = agent == "claude" ? "`$ARGUMENTS`" : target.ArgsPlaceholder;
                Assert.Contains(argument, rendered);
            }

            if (command == "save-context")
            {
                Assert.Contains($"`agent` set to `{agent}`", rendered);
            }

            if (agent == "codex")
            {
                Assert.StartsWith($"---\nname: cg-{command}\ndescription: ", rendered);
                Assert.Contains("\n---\n" + Marker + "\n", rendered);
            }
            else
            {
                Assert.StartsWith(Marker + "\n", rendered);
            }
        }
    }
}
