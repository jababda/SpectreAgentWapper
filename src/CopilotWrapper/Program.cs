using Spectre.Console;
using System.Diagnostics;
using System.Runtime.InteropServices;

// Use the current working directory (where the user invoked the app),
// not the directory where the executable lives.
var workingDirectory = Environment.CurrentDirectory;

AnsiConsole.Write(
    new FigletText("Spectre Agent")
        .Centered()
        .Color(Color.Blue));

AnsiConsole.MarkupLine("[bold cyan]Welcome to the Spectre Agent Wrapper![/]");
AnsiConsole.MarkupLine($"[grey]Working directory:[/] [yellow]{workingDirectory}[/]");
AnsiConsole.WriteLine();

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
            StartCopilotContainer(workingDirectory);
            break;
        case "Exit":
            AnsiConsole.MarkupLine("[grey]Goodbye![/]");
            running = false;
            break;
    }
}

static void StartCopilotContainer(string workingDirectory)
{
    AnsiConsole.MarkupLine("[bold]Starting copilot container...[/]");

    // Normalize the working directory path for Windows (use forward slashes for Docker)
    var mountPath = workingDirectory;
    if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
    {
        // Docker Desktop on Windows expects paths like /c/Users/... or the full Windows path
        mountPath = workingDirectory.Replace('\\', '/');
    }

    var dockerArgs = $"run --rm -it " +
                     $"--cap-drop ALL --cap-add NET_ADMIN " +
                     $"--security-opt no-new-privileges:true " +
                     $"-v \"{mountPath}:/workspace\" " +
                     $"ghcr.io/jababda/spectreagentWapper";

    var processInfo = new ProcessStartInfo
    {
        FileName = "docker",
        Arguments = dockerArgs,
        UseShellExecute = false,
        WorkingDirectory = workingDirectory,
    };

    AnsiConsole.MarkupLine($"[grey]Running:[/] docker {Markup.Escape(dockerArgs)}");
    AnsiConsole.WriteLine();

    try
    {
        using var process = Process.Start(processInfo);
        process?.WaitForExit();

        var exitCode = process?.ExitCode ?? -1;
        if (exitCode == 0)
        {
            AnsiConsole.MarkupLine("[green]Container exited successfully.[/]");
        }
        else
        {
            AnsiConsole.MarkupLine($"[red]Container exited with code {exitCode}.[/]");
        }
    }
    catch (Exception ex)
    {
        AnsiConsole.MarkupLine($"[red]Failed to start container: {Markup.Escape(ex.Message)}[/]");
    }

    AnsiConsole.WriteLine();
}
