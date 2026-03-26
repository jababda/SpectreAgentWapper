using SpectreAgent.Services;
using Spectre.Console;
using Spectre.Console.Cli;
using System.ComponentModel;

namespace SpectreAgent.Commands;

[Description("Run only the executor agent against an existing plan file.")]
public class ExecuteCommand : AsyncCommand<ExecuteCommand.Settings>
{
    public class Settings : CommandSettings
    {
        [CommandArgument(0, "<PLAN_FILE>")]
        [Description("Path to the markdown plan file produced by the planner agent.")]
        public string PlanFile { get; set; } = string.Empty;

        [CommandOption("--repo <PATH>")]
        [Description("Path to the repository. Defaults to the current directory.")]
        public string? RepoPath { get; set; }

        [CommandOption("--approve-all")]
        [Description("Automatically approve all file-creation permission requests.")]
        public bool ApproveAll { get; set; }
    }

    public override async Task<int> ExecuteAsync(CommandContext context, Settings settings)
    {
        var config = ConfigService.LoadConfig();
        if (config == null)
        {
            AnsiConsole.MarkupLine("[red]No configuration found. Run 'spectre-agent setup' first.[/]");
            return 1;
        }

        if (!File.Exists(settings.PlanFile))
        {
            AnsiConsole.MarkupLine($"[red]Plan file not found:[/] {settings.PlanFile}");
            return 1;
        }

        var repoPath = Path.GetFullPath(settings.RepoPath ?? Directory.GetCurrentDirectory());

        AnsiConsole.MarkupLine("[blue]Running executor agent …[/]");

        return await ContainerService.RunAgentAsync(
            agentName: "executor",
            prompt: Path.GetFullPath(settings.PlanFile),
            repoPath: repoPath,
            planFile: Path.GetFullPath(settings.PlanFile),
            config: config,
            approveAll: settings.ApproveAll);
    }
}
