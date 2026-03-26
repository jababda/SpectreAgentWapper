using SpectreAgent.Commands;
using Spectre.Console.Cli;

var app = new CommandApp();
app.Configure(config =>
{
    config.SetApplicationName("spectre-agent");
    config.SetApplicationVersion("1.0.0");

    config.AddCommand<SetupCommand>("setup")
        .WithDescription("Configure environment variables required by the agents (e.g. GitHub token).");

    config.AddCommand<RunCommand>("run")
        .WithDescription("Mount the current directory into the hardened container and run both agents (plan then execute).");

    config.AddCommand<PlanCommand>("plan")
        .WithDescription("Run only the planner agent to produce a markdown plan file.");

    config.AddCommand<ExecuteCommand>("execute")
        .WithDescription("Run only the executor agent against an existing plan file.");

    config.AddCommand<WhitelistCommand>("whitelist")
        .WithDescription("Add a domain to the container's network allowlist.");
});

return app.Run(args);
