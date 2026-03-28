using CopilotWrapper;
using Spectre.Console;

// Use the current working directory (where the user invoked the app),
// not the directory where the executable lives.
var workingDirectory = Environment.CurrentDirectory;
const string dockerImage = "ghcr.io/jababda/spectreagentwrapper";

AnsiConsole.Write(
    new FigletText("Spectre Agent")
        .Centered()
        .Color(Color.Blue));

AnsiConsole.MarkupLine("[bold cyan]Welcome to the Spectre Agent Wrapper![/]");
AnsiConsole.MarkupLine($"[grey]Working directory:[/] [yellow]{workingDirectory}[/]");
AnsiConsole.WriteLine();

var dockerService = new DockerService(new ProcessRunner(), AnsiConsole.Console);

bool running = true;
while (running)
{
    var choice = AnsiConsole.Prompt(
        new SelectionPrompt<string>()
            .Title("[green]What would you like to do?[/]")
            .AddChoices(
                "Start copilot container",
                "Exit"));

    switch (choice)
    {
        case "Start copilot container":
            if (dockerService.EnsureDependencies(dockerImage))
            {
                dockerService.StartContainer(workingDirectory, dockerImage);
            }
            break;
        case "Exit":
            AnsiConsole.MarkupLine("[grey]Goodbye![/]");
            running = false;
            break;
    }
}
