using Cortex.Mediator.DependencyInjection;
using Microsoft.Extensions.DependencyInjection;
using Spectre.Console;
using SpectreAgentWrapper.Commands;
using SpectreAgentWrapper.Containers;
using SpectreAgentWrapper.Menu;
using SpectreAgentWrapper.Podman;

namespace SpectreAgentWrapper;

internal static class Program
{
    public static async Task<int> Main()
    {
        var services = new ServiceCollection();

        services.AddSingleton<IAnsiConsole>(_ => AnsiConsole.Console);
        services.AddSingleton<IPodmanDefinitionLocator, PodmanDefinitionLocator>();
        services.AddSingleton<IPodmanWorkspaceContextFactory, PodmanWorkspaceContextFactory>();
        services.AddSingleton<IPodmanApiConnectionResolver, PodmanApiConnectionResolver>();
        services.AddSingleton<IPodmanApiClientFactory, PodmanApiClientFactory>();
        services.AddSingleton<IBuildContextArchiveFactory, BuildContextArchiveFactory>();
        services.AddSingleton<IContainerRuntimeOrchestrator, ContainerRuntimeOrchestrator>();
        services.AddSingleton<TerminalUserInterface>();

        services.AddCortexMediator(
            new[]
            {
                typeof(StartDefaultContainerCommandHandler),
                typeof(RebuildDefaultImageCommandHandler),
                typeof(ExitApplicationCommandHandler),
            },
            _ => { });

        using var serviceProvider = services.BuildServiceProvider();

        var terminalUserInterface = serviceProvider.GetRequiredService<TerminalUserInterface>();
        return await terminalUserInterface.RunAsync();
    }
}
