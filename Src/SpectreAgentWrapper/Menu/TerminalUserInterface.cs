using Cortex.Mediator;
using Spectre.Console;
using SpectreAgentWrapper.Commands;
using SpectreAgentWrapper.Podman;

namespace SpectreAgentWrapper.Menu;

internal sealed class TerminalUserInterface(
    IAnsiConsole console,
    IMediator mediator,
    IPodmanWorkspaceContextFactory workspaceContextFactory)
{
    public async Task<int> RunAsync(CancellationToken cancellationToken = default)
    {
        while (true)
        {
            var context = workspaceContextFactory.Create();

            console.Clear();
            RenderHeader(context);

            var selection = console.Prompt(
                new SelectionPrompt<MenuChoice>()
                    .Title("Choose an action")
                    .AddChoices(MenuChoice.StartDefaultContainer, MenuChoice.RebuildImage, MenuChoice.Exit)
                    .UseConverter(ConvertMenuChoice));

            try
            {
                var result = await ExecuteSelectionAsync(selection, cancellationToken);

                if (!string.IsNullOrWhiteSpace(result.Message))
                {
                    console.MarkupLine($"[green]{Markup.Escape(result.Message)}[/]");
                }

                if (result.ShouldExit)
                {
                    return 0;
                }
            }
            catch (Exception exception)
            {
                console.Write(
                    new Panel(Markup.Escape(exception.Message))
                        .Header("Action failed")
                        .BorderColor(Color.Red));
            }

            console.WriteLine();
            console.Markup("[grey]Press any key to return to the menu...[/]");
            Console.ReadKey(intercept: true);
        }
    }

    private static string ConvertMenuChoice(MenuChoice choice)
    {
        return choice switch
        {
            MenuChoice.StartDefaultContainer => "Start default container",
            MenuChoice.RebuildImage => "Rebuild image",
            MenuChoice.Exit => "Exit",
            _ => choice.ToString(),
        };
    }

    private async Task<MenuCommandResult> ExecuteSelectionAsync(MenuChoice selection, CancellationToken cancellationToken)
    {
        return selection switch
        {
            MenuChoice.StartDefaultContainer => await mediator.SendAsync(new StartDefaultContainerCommand(), cancellationToken),
            MenuChoice.RebuildImage => await mediator.SendAsync(new RebuildDefaultImageCommand(), cancellationToken),
            MenuChoice.Exit => await mediator.SendAsync(new ExitApplicationCommand(), cancellationToken),
            _ => throw new InvalidOperationException($"Unsupported menu selection '{selection}'."),
        };
    }

    private void RenderHeader(PodmanWorkspaceContext context)
    {
        console.Write(
            new Rule("[green]Secure Copilot CLI Wrapper[/]")
                .RuleStyle("grey")
                .LeftJustified());

        console.MarkupLine($"[grey]Workspace:[/] [blue]{Markup.Escape(context.WorkspacePath)}[/]");
        console.MarkupLine($"[grey]Image:[/] [blue]{Markup.Escape(context.ImageName)}[/]");
        console.MarkupLine($"[grey]Container:[/] [blue]{Markup.Escape(context.ContainerName)}[/]");
        console.WriteLine();
    }
}
