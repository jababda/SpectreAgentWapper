using SpectreAgent.Services;
using Spectre.Console;
using Spectre.Console.Cli;
using System.ComponentModel;

namespace SpectreAgent.Commands;

[Description("Mount the current directory into the hardened container and run both agents.")]
public class RunCommand : AsyncCommand<RunCommand.Settings>
{
    public class Settings : CommandSettings
    {
        [CommandArgument(0, "<PROMPT>")]
        [Description("Natural-language description of the changes you want the agents to make.")]
        public string Prompt { get; set; } = string.Empty;

        [CommandOption("--repo <PATH>")]
        [Description("Path to the repository to mount. Defaults to the current directory.")]
        public string? RepoPath { get; set; }

        [CommandOption("--plan-file <FILE>")]
        [Description("Output path for the generated plan markdown file. Defaults to '.spectre-plan.md' inside the repo.")]
        public string? PlanFile { get; set; }

        [CommandOption("--approve-all")]
        [Description("Automatically approve all file-creation permission requests without prompting.")]
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

        var repoPath = Path.GetFullPath(settings.RepoPath ?? Directory.GetCurrentDirectory());
        var planFile = settings.PlanFile ?? Path.Combine(repoPath, ".spectre-plan.md");

        AnsiConsole.MarkupLine($"[bold]Repository:[/] {repoPath}");
        AnsiConsole.MarkupLine($"[bold]Plan file:[/]  {planFile}");
        AnsiConsole.WriteLine();

        // ── Step 1: Planner ───────────────────────────────────────────────────
        AnsiConsole.MarkupLine("[blue]Step 1/2 – Running planner agent …[/]");
        var planResult = await ContainerService.RunAgentAsync(
            agentName: "planner",
            prompt: settings.Prompt,
            repoPath: repoPath,
            planFile: planFile,
            config: config,
            approveAll: settings.ApproveAll);

        if (planResult != 0)
        {
            AnsiConsole.MarkupLine("[red]Planner agent failed.[/]");
            return planResult;
        }

        AnsiConsole.MarkupLine("[green]Plan written to:[/] " + planFile);
        AnsiConsole.WriteLine();

        // ── Step 2: Executor ──────────────────────────────────────────────────
        AnsiConsole.MarkupLine("[blue]Step 2/2 – Running executor agent …[/]");
        var execResult = await ContainerService.RunAgentAsync(
            agentName: "executor",
            prompt: planFile,
            repoPath: repoPath,
            planFile: planFile,
            config: config,
            approveAll: settings.ApproveAll);

        if (execResult != 0)
        {
            AnsiConsole.MarkupLine("[red]Executor agent failed.[/]");
            return execResult;
        }

        AnsiConsole.MarkupLine("[green]All done![/]");
        return 0;
    }
}
