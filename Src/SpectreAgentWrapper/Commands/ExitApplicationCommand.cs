using Cortex.Mediator.Commands;
using SpectreAgentWrapper.Menu;

namespace SpectreAgentWrapper.Commands;

public sealed record ExitApplicationCommand : ICommand<MenuCommandResult>;

public sealed class ExitApplicationCommandHandler : ICommandHandler<ExitApplicationCommand, MenuCommandResult>
{
    public Task<MenuCommandResult> Handle(ExitApplicationCommand command, CancellationToken cancellationToken)
    {
        return Task.FromResult(new MenuCommandResult(true));
    }
}
