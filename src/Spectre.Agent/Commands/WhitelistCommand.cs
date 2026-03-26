using SpectreAgent.Services;
using Spectre.Console;
using Spectre.Console.Cli;
using System.ComponentModel;

namespace SpectreAgent.Commands;

[Description("Add a domain to the container's outbound network allowlist.")]
public class WhitelistCommand : Command<WhitelistCommand.Settings>
{
    public class Settings : CommandSettings
    {
        [CommandArgument(0, "<DOMAIN>")]
        [Description("Domain to allow (e.g. api.github.com).")]
        public string Domain { get; set; } = string.Empty;
    }

    public override int Execute(CommandContext context, Settings settings)
    {
        var domain = settings.Domain.Trim().ToLowerInvariant();

        if (string.IsNullOrWhiteSpace(domain))
        {
            AnsiConsole.MarkupLine("[red]Domain cannot be empty.[/]");
            return 1;
        }

        ConfigService.AddWhitelistedDomain(domain);
        AnsiConsole.MarkupLine($"[green]Domain '{domain}' added to allowlist.[/]");
        AnsiConsole.MarkupLine("The new allowlist will take effect the next time you start a container.");
        return 0;
    }
}
