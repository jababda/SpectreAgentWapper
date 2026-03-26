using SpectreAgent.Services;
using Spectre.Console;
using Spectre.Console.Cli;
using System.ComponentModel;

namespace SpectreAgent.Commands;

[Description("Run only the planner agent to produce a markdown plan file.")]
public class PlanCommand : AsyncCommand<PlanCommand.Settings>
{
    public class Settings : CommandSettings
    {
        [CommandArgument(0, "<PROMPT>")]
        [Description("Natural-language description of the desired changes.")]
        public string Prompt { get; set; } = string.Empty;

        [CommandOption("--repo <PATH>")]
        [Description("Path to the repository. Defaults to the current directory.")]
        public string? RepoPath { get; set; }

        [CommandOption("--plan-file <FILE>")]
        [Description("Output path for the plan markdown. Defaults to '.spectre-plan.md' inside the repo.")]
        public string? PlanFile { get; set; }
    }

    public override async Task<int> ExecuteAsync(CommandContext context, Settings settings)
    {
        var config = ConfigService.LoadConfig();
        if (config == null)
        {
            AnsiConsole.MarkupLine("[red]No configuration found. Run 'spectre-agent setup' first.[/]");
            return 1;
        }

        var repoPath = Path.GetFullPath(settings.RepoPath ?? Directory.GetCurrentDirectory());
        var planFile = settings.PlanFile ?? Path.Combine(repoPath, ".spectre-plan.md");

        AnsiConsole.MarkupLine("[blue]Running planner agent …[/]");

        var result = await ContainerService.RunAgentAsync(
            agentName: "planner",
            prompt: settings.Prompt,
            repoPath: repoPath,
            planFile: planFile,
            config: config,
            approveAll: false);

        if (result == 0)
            AnsiConsole.MarkupLine("[green]Plan written to:[/] " + planFile);

        return result;
    }
}
