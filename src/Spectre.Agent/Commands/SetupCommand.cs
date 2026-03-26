using SpectreAgent.Services;
using Spectre.Console;
using Spectre.Console.Cli;
using System.ComponentModel;

namespace SpectreAgent.Commands;

[Description("Configure environment variables required by the agents.")]
public class SetupCommand : Command<SetupCommand.Settings>
{
    public class Settings : CommandSettings
    {
        [CommandOption("--token <TOKEN>")]
        [Description("GitHub personal access token with 'copilot' scope. If omitted you will be prompted interactively.")]
        public string? Token { get; set; }

        [CommandOption("--model <MODEL>")]
        [Description("GitHub Copilot / Models endpoint model name. Defaults to 'gpt-4o'.")]
        [DefaultValue("gpt-4o")]
        public string Model { get; set; } = "gpt-4o";
    }

    public override int Execute(CommandContext context, Settings settings)
    {
        AnsiConsole.Write(new FigletText("Spectre Agent").Color(Color.Purple));
        AnsiConsole.MarkupLine("[bold]Setup wizard[/]");
        AnsiConsole.WriteLine();

        var token = settings.Token;

        if (string.IsNullOrWhiteSpace(token))
        {
            // Try to retrieve via gh CLI first
            token = GithubTokenService.TryGetTokenFromGhCli();

            if (string.IsNullOrWhiteSpace(token))
            {
                AnsiConsole.MarkupLine("[yellow]Could not obtain a token via 'gh auth token'.[/]");
                AnsiConsole.MarkupLine("You can create one at [link]https://github.com/settings/tokens[/] with the [bold]'copilot'[/] scope.");
                token = AnsiConsole.Prompt(
                    new TextPrompt<string>("Enter your GitHub personal access token:")
                        .Secret());
            }
            else
            {
                AnsiConsole.MarkupLine("[green]Retrieved token via 'gh auth token'.[/]");
            }
        }

        ConfigService.SaveConfig(new AgentConfig
        {
            GithubToken = token,
            Model = settings.Model
        });

        AnsiConsole.MarkupLine("[green]Configuration saved to ~/.spectre-agent/config.json[/]");
        return 0;
    }
}
