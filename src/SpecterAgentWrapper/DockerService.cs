using Spectre.Console;
using System.Runtime.InteropServices;

namespace CopilotWrapper;

/// <summary>
/// Handles Docker dependency checks and container lifecycle for the Copilot wrapper.
/// All public/protected members are virtual so they can be substituted in unit tests.
/// </summary>
public class DockerService
{
    private readonly IProcessRunner _runner;
    private readonly IAnsiConsole _console;

    public DockerService(IProcessRunner runner, IAnsiConsole console)
    {
        _runner = runner;
        _console = console;
    }

    // ---------------------------------------------------------------------------
    // Dependency checks
    // ---------------------------------------------------------------------------

    /// <summary>Returns <c>true</c> when the <c>docker</c> CLI is found on PATH.</summary>
    public virtual bool IsDockerInstalled()
    {
        try
        {
            var result = _runner.Run("docker", "--version");
            return result.ExitCode == 0;
        }
        catch
        {
            return false;
        }
    }

    /// <summary>Returns <c>true</c> when the Docker daemon is accepting connections.</summary>
    public virtual bool IsDockerRunning()
    {
        try
        {
            var result = _runner.Run("docker", "info");
            return result.ExitCode == 0;
        }
        catch
        {
            return false;
        }
    }

    /// <summary>
    /// Attempts to start the Docker daemon/desktop and waits up to ~30 s for it to become ready.
    /// Returns <c>true</c> if Docker is ready after the attempt.
    /// </summary>
    public virtual bool TryStartDocker()
    {
        _console.MarkupLine("[yellow]Docker is not running. Attempting to start Docker...[/]");

        try
        {
            ProcessResult startResult;

            if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            {
                startResult = _runner.Run(
                    "powershell",
                    "-Command \"Start-Process 'C:\\Program Files\\Docker\\Docker\\Docker Desktop.exe'\"");
            }
            else if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
            {
                startResult = _runner.Run("sudo", "systemctl start docker");
            }
            else
            {
                _console.MarkupLine("[red]Cannot automatically start Docker on this platform.[/]");
                return false;
            }

            if (startResult.ExitCode != 0)
            {
                _console.MarkupLine($"[red]Start command failed (exit code {startResult.ExitCode}).[/]");
                return false;
            }

            // Poll until the daemon is ready (up to 10 retries × 3 s = ~30 s).
            for (int i = 0; i < 10; i++)
            {
                WaitBeforeRetry();
                if (IsDockerRunning())
                {
                    _console.MarkupLine("[green]Docker started successfully.[/]");
                    return true;
                }

                _console.MarkupLine($"[grey]Waiting for Docker to start... ({i + 1}/10)[/]");
            }

            _console.MarkupLine("[red]Docker did not start within the expected time.[/]");
            return false;
        }
        catch (Exception ex)
        {
            _console.MarkupLine($"[red]Failed to start Docker: {Markup.Escape(ex.Message)}[/]");
            return false;
        }
    }

    /// <summary>
    /// Checks whether <paramref name="image"/> exists locally; pulls it if not.
    /// Returns <c>true</c> when the image is available after the call.
    /// </summary>
    public virtual bool EnsureImageAvailable(string image)
    {
        _console.MarkupLine($"[grey]Checking for image:[/] [yellow]{Markup.Escape(image)}[/]");

        try
        {
            var inspectResult = _runner.Run("docker", $"image inspect {image}");
            if (inspectResult.ExitCode == 0)
            {
                _console.MarkupLine("[green]Image is already available locally.[/]");
                return true;
            }

            _console.MarkupLine($"[yellow]Image not found locally. Pulling {Markup.Escape(image)}...[/]");
            var pullResult = _runner.Run("docker", $"pull {image}", inheritStdio: true);

            if (pullResult.ExitCode == 0)
            {
                _console.MarkupLine("[green]Image pulled successfully.[/]");
                return true;
            }

            _console.MarkupLine($"[red]Failed to pull image (exit code {pullResult.ExitCode}).[/]");
            return false;
        }
        catch (Exception ex)
        {
            _console.MarkupLine($"[red]Failed to check/pull image: {Markup.Escape(ex.Message)}[/]");
            return false;
        }
    }

    /// <summary>
    /// Orchestrates all pre-flight checks: ensures Docker is installed, running,
    /// and that the required image is available. Returns <c>true</c> only when
    /// all prerequisites are satisfied.
    /// </summary>
    public virtual bool EnsureDependencies(string image)
    {
        if (!IsDockerInstalled())
        {
            _console.MarkupLine("[red]Docker is not installed. Please install Docker and try again.[/]");
            _console.MarkupLine("[grey]Visit https://docs.docker.com/get-docker/ for installation instructions.[/]");
            return false;
        }

        if (!IsDockerRunning())
        {
            if (!TryStartDocker())
            {
                _console.MarkupLine("[red]Docker is not running. Please start Docker and try again.[/]");
                return false;
            }
        }

        return EnsureImageAvailable(image);
    }

    // ---------------------------------------------------------------------------
    // Container launch
    // ---------------------------------------------------------------------------

    /// <summary>Runs the container image, mounting <paramref name="workingDirectory"/> at /workspace.</summary>
    public virtual void StartContainer(string workingDirectory, string image)
    {
        _console.MarkupLine("[bold]Starting copilot container...[/]");

        var mountPath = workingDirectory;
        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            mountPath = workingDirectory.Replace('\\', '/');
        }

        var dockerArgs = $"run --rm -it " +
                         $"--cap-drop ALL --cap-add NET_ADMIN " +
                         $"--security-opt no-new-privileges:true " +
                         $"-v \"{mountPath}:/workspace\" " +
                         $"{image}";

        _console.MarkupLine($"[grey]Running:[/] docker {Markup.Escape(dockerArgs)}");
        _console.WriteLine();

        try
        {
            var result = _runner.Run("docker", dockerArgs, inheritStdio: true);

            if (result.ExitCode == 0)
            {
                _console.MarkupLine("[green]Container exited successfully.[/]");
            }
            else
            {
                _console.MarkupLine($"[red]Container exited with code {result.ExitCode}.[/]");
            }
        }
        catch (Exception ex)
        {
            _console.MarkupLine($"[red]Failed to start container: {Markup.Escape(ex.Message)}[/]");
        }

        _console.WriteLine();
    }

    // ---------------------------------------------------------------------------
    // Extension point for testing
    // ---------------------------------------------------------------------------

    /// <summary>
    /// Called between retry attempts in <see cref="TryStartDocker"/>.
    /// Override in tests to eliminate the real delay.
    /// </summary>
    protected virtual void WaitBeforeRetry() => Thread.Sleep(3_000);
}
