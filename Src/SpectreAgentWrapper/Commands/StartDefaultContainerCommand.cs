using Cortex.Mediator.Commands;
using Spectre.Console;
using SpectreAgentWrapper.Containers;
using SpectreAgentWrapper.Menu;
using SpectreAgentWrapper.Podman;

namespace SpectreAgentWrapper.Commands;

public sealed record StartDefaultContainerCommand : ICommand<MenuCommandResult>;

public sealed class StartDefaultContainerCommandHandler(
    IAnsiConsole console,
    IContainerRuntimeOrchestrator containerRuntimeOrchestrator,
    IPodmanWorkspaceContextFactory workspaceContextFactory)
    : ICommandHandler<StartDefaultContainerCommand, MenuCommandResult>
{
    public async Task<MenuCommandResult> Handle(StartDefaultContainerCommand command, CancellationToken cancellationToken)
    {
        var context = workspaceContextFactory.Create();

        if (string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("GH_COPILOT_PAT")))
        {
            console.MarkupLine(
                "[yellow]GH_COPILOT_PAT is not set. Copilot may ask you to authenticate inside the container.[/]");
        }

        console.MarkupLine(
            $"[grey]Starting container[/] [blue]{Markup.Escape(context.ContainerName)}[/] [grey]for workspace[/] [blue]{Markup.Escape(context.WorkspacePath)}[/]");

        await containerRuntimeOrchestrator.StartDefaultContainerAsync(context, cancellationToken);

        return new MenuCommandResult(false, $"Returned from container '{context.ContainerName}'.");
    }
}
