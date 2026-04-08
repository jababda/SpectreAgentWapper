using Cortex.Mediator.Commands;
using Spectre.Console;
using SpectreAgentWrapper.Containers;
using SpectreAgentWrapper.Menu;
using SpectreAgentWrapper.Podman;

namespace SpectreAgentWrapper.Commands;

public sealed record RebuildDefaultImageCommand : ICommand<MenuCommandResult>;

public sealed class RebuildDefaultImageCommandHandler(
    IAnsiConsole console,
    IContainerRuntimeOrchestrator containerRuntimeOrchestrator,
    IPodmanWorkspaceContextFactory workspaceContextFactory)
    : ICommandHandler<RebuildDefaultImageCommand, MenuCommandResult>
{
    public async Task<MenuCommandResult> Handle(RebuildDefaultImageCommand command, CancellationToken cancellationToken)
    {
        var context = workspaceContextFactory.Create();

        console.MarkupLine(
            $"[grey]Rebuilding image[/] [blue]{Markup.Escape(context.ImageName)}[/] [grey]from[/] [blue]{Markup.Escape(context.ImageDefinitionPath)}[/]");

        await containerRuntimeOrchestrator.RebuildDefaultImageAsync(context, cancellationToken);

        return new MenuCommandResult(false, $"Rebuilt image '{context.ImageName}'.");
    }
}
