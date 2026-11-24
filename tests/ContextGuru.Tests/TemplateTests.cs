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
    [InlineData("claude", "`mcp__contextguru__", "`", "`$ARGUMENTS`")]
    [InlineData("codex", "`", "` on the `contextguru` MCP server", "the text after the skill name")]
    [InlineData("cursor", "`contextguru-", "`", "the text after the command name")]
    public void EmbeddedTemplatesRenderAllCommandsForTarget(string agent, string toolPrefix, string toolSuffix, string args)
    {
        using var temp = new TempDir();
        var target = AgentTargets.For(agent, temp.Path);
        (string Name, string Body)[] commands =
        [
            ("save-context", $"Write a handoff for the work in this session. The body must use exactly these level-two headings: Goal, Done, Decisions, Open tasks, Next steps. Also write a one-line summary of at most 200 characters. Leave out secrets and file contents. Call {toolPrefix}save_context{toolSuffix} with `summary`, `body`, `agent` set to `{agent}`, `projectPath` set to the current folder, and `name` set to {args} if given. If the tool returns an error, fix the handoff and call it once more."),
            ("get-context", $"Call {toolPrefix}get_context{toolSuffix} with `projectPath` set to the current folder, and `name` set to {args} if given. Follow the `agent_instructions` in the result."),
            ("list-contexts", $"Call {toolPrefix}list_contexts{toolSuffix} with `projectPath` set to the current folder. Show name, summary, updated time and source agent as a table."),
            ("remove-context", $"If no name was given, ask for one and stop. Otherwise ask the user to confirm removing the context named {args}. Only after they confirm, call {toolPrefix}remove_context{toolSuffix} with that name and `projectPath` set to the current folder."),
        ];

        Assert.Equal(commands.Select(command => command.Name).Order(StringComparer.Ordinal),
            AgentTargets.CommandNames.Order(StringComparer.Ordinal));

        foreach (var (name, body) in commands)
        {
            var rendered = TemplateRenderer.Render(name, target).ReplaceLineEndings("\n");
            var frontMatter = agent == "codex"
                ? $"---\nname: cg-{name}\ndescription: Run ContextGuru {name}.\n---\n"
                : "";

            Assert.Equal(frontMatter + Marker + "\n" + body + "\n", rendered);
            Assert.True(target.IsManagedCommand(rendered));
            Assert.True(target.IsManagedCommand(rendered.ReplaceLineEndings("\r\n")));
            Assert.False(target.IsManagedCommand(frontMatter + "Other content\n" + Marker + "\n"));
            var relativePath = agent == "codex" ? $"cg-{name}/SKILL.md" : $"cg-{name}.md";
            Assert.Equal(Path.GetFullPath(relativePath, target.CommandFolder), target.GetCommandPath(name));
        }
    }

    [Fact]
    public void RenderingUsesTargetMarkerAtTargetPosition()
    {
        using var temp = new TempDir();
        var target = AgentTargets.For("codex", temp.Path) with { MarkerLine = "<!-- alternate marker -->" };
        var rendered = TemplateRenderer.Render("save-context", target).ReplaceLineEndings("\n");

        Assert.Contains("\n---\n<!-- alternate marker -->\n", rendered);
        Assert.DoesNotContain(Marker, rendered);
        Assert.True(target.IsManagedCommand(rendered));
        Assert.False(target.IsManagedCommand(Marker + "\n"));
        Assert.False(target.IsManagedCommand("---\nname: cg-save-context\n<!-- alternate marker -->\n"));
        Assert.False(target.IsManagedCommand("---\nname: cg-save-context\n...\n<!-- alternate marker -->\n"));
    }

    [Fact]
    public void UnknownCommandIsRejectedByPathAndRenderer()
    {
        using var temp = new TempDir();
        var target = AgentTargets.For("codex", temp.Path);

        Assert.Throws<ArgumentException>(() => target.GetCommandPath("../other"));
        Assert.Throws<ArgumentException>(() => TemplateRenderer.Render("../other", target));
    }
}
