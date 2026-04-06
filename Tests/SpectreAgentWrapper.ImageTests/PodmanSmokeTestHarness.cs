using System.ComponentModel;
using System.Diagnostics;
using System.Text.Json;
using Xunit.Sdk;

namespace SpectreAgentWrapper.ImageTests;

internal sealed class PodmanSmokeTestHarness
{
    private static readonly TimeSpan CommandTimeout = TimeSpan.FromMinutes(10);
    private static readonly TimeSpan BuildTimeout = TimeSpan.FromMinutes(30);
    private static readonly TimeSpan PollInterval = TimeSpan.FromSeconds(1);
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
    };

    private readonly string repositoryRoot;
    private readonly string imageFilePath;
    private readonly string imageDisplayName;
    private readonly string imageTag;
    private readonly string containerName;
    private bool imageBuilt;
    private bool containerCreated;

    public PodmanSmokeTestHarness(string repositoryRoot, string imageFilePath)
    {
        this.repositoryRoot = repositoryRoot;
        this.imageFilePath = imageFilePath;

        imageDisplayName = Path.GetFileNameWithoutExtension(imageFilePath);

        var normalizedName = NormalizeForPodmanName(imageDisplayName);
        var uniqueSuffix = Guid.NewGuid().ToString("N")[..12];

        imageTag = $"spectre-agentwrapper-{normalizedName}-{uniqueSuffix}";
        containerName = $"{imageTag}-container";
    }

    public static async Task EnsurePodmanAvailableAsync()
    {
        ProcessResult infoResult;

        try
        {
            infoResult = await RunProcessAsync(
                "podman",
                new[] { "info", "--format", "json" },
                Environment.CurrentDirectory,
                TimeSpan.FromSeconds(30));
        }
        catch (Win32Exception exception)
        {
            HandleUnavailable($"Podman CLI was not found on PATH. {exception.Message}");
            return;
        }
        catch (TimeoutException exception)
        {
            HandleUnavailable($"Timed out while checking Podman availability. {exception.Message}");
            return;
        }

        if (infoResult.ExitCode != 0)
        {
            HandleUnavailable(
                $"Podman is installed but unavailable.{Environment.NewLine}{FormatCommandFailure(infoResult)}");
        }
    }

    public async Task AssertImageRunsForMinimumDurationAsync(TimeSpan minimumRuntime)
    {
        var buildResult = await RunProcessAsync(
            "podman",
            new[] { "build", "-f", imageFilePath, "-t", imageTag, repositoryRoot },
            repositoryRoot,
            BuildTimeout);

        imageBuilt = buildResult.ExitCode == 0;

        if (buildResult.ExitCode != 0)
        {
            throw new XunitException(
                $"Failed to build image '{imageDisplayName}'.{Environment.NewLine}{FormatCommandFailure(buildResult)}");
        }

        var runResult = await RunProcessAsync(
            "podman",
            new[] { "run", "-d", "-i", "-t", "--name", containerName, imageTag },
            repositoryRoot,
            CommandTimeout);

        if (runResult.ExitCode != 0)
        {
            throw new XunitException(
                $"Failed to start container for image '{imageDisplayName}'.{Environment.NewLine}{FormatCommandFailure(runResult)}");
        }

        containerCreated = true;

        if (string.IsNullOrWhiteSpace(runResult.StandardOutput))
        {
            throw new XunitException(
                $"Podman did not return a container ID for image '{imageDisplayName}'.{Environment.NewLine}{FormatCommandFailure(runResult)}");
        }

        var stabilityDeadline = DateTimeOffset.UtcNow + minimumRuntime;

        while (DateTimeOffset.UtcNow < stabilityDeadline)
        {
            var state = await InspectContainerStateAsync();

            if (!state.Running)
            {
                throw new XunitException(await BuildStoppedContainerMessageAsync(minimumRuntime, state));
            }

            var remaining = stabilityDeadline - DateTimeOffset.UtcNow;

            if (remaining > TimeSpan.Zero)
            {
                await Task.Delay(remaining < PollInterval ? remaining : PollInterval);
            }
        }

        var finalState = await InspectContainerStateAsync();

        if (!finalState.Running)
        {
            throw new XunitException(await BuildStoppedContainerMessageAsync(minimumRuntime, finalState));
        }
    }

    public async Task<List<string>> CleanupAsync()
    {
        var failures = new List<string>();

        if (containerCreated)
        {
            await CleanupArtifactAsync(
                failures,
                "container",
                containerName,
                new[] { "rm", "-f", containerName });
        }

        if (imageBuilt)
        {
            await CleanupArtifactAsync(
                failures,
                "image",
                imageTag,
                new[] { "rmi", "-f", imageTag });
        }

        return failures;
    }

    private static void HandleUnavailable(string message)
    {
        if (IsCiEnvironment())
        {
            throw new XunitException(message);
        }

        Assert.Skip(message);
    }

    private static bool IsCiEnvironment()
    {
        return string.Equals(Environment.GetEnvironmentVariable("CI"), "true", StringComparison.OrdinalIgnoreCase)
            || string.Equals(Environment.GetEnvironmentVariable("GITHUB_ACTIONS"), "true", StringComparison.OrdinalIgnoreCase);
    }

    private static string NormalizeForPodmanName(string value)
    {
        var buffer = value
            .ToLowerInvariant()
            .Select(character => char.IsLetterOrDigit(character) ? character : '-')
            .ToArray();

        return new string(buffer).Trim('-');
    }

    private static async Task<ProcessResult> RunProcessAsync(
        string fileName,
        IReadOnlyList<string> arguments,
        string workingDirectory,
        TimeSpan timeout)
    {
        using var process = new Process();

        process.StartInfo = new ProcessStartInfo
        {
            FileName = fileName,
            WorkingDirectory = workingDirectory,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
        };

        foreach (var argument in arguments)
        {
            process.StartInfo.ArgumentList.Add(argument);
        }

        process.Start();

        var standardOutputTask = process.StandardOutput.ReadToEndAsync();
        var standardErrorTask = process.StandardError.ReadToEndAsync();

        using var timeoutCts = new CancellationTokenSource(timeout);

        try
        {
            await process.WaitForExitAsync(timeoutCts.Token);
        }
        catch (OperationCanceledException)
        {
            if (!process.HasExited)
            {
                process.Kill(entireProcessTree: true);
            }

            var timedOutOutput = await standardOutputTask;
            var timedOutError = await standardErrorTask;

            throw new TimeoutException(
                $"Command timed out after {timeout.TotalSeconds:0} seconds: {FormatCommand(fileName, arguments)}{Environment.NewLine}" +
                $"stdout:{Environment.NewLine}{NormalizeOutput(timedOutOutput)}{Environment.NewLine}" +
                $"stderr:{Environment.NewLine}{NormalizeOutput(timedOutError)}");
        }

        return new ProcessResult(
            fileName,
            arguments,
            process.ExitCode,
            await standardOutputTask,
            await standardErrorTask);
    }

    private static string FormatCommandFailure(ProcessResult result)
    {
        return
            $"Command: {FormatCommand(result.FileName, result.Arguments)}{Environment.NewLine}" +
            $"Exit code: {result.ExitCode}{Environment.NewLine}" +
            $"stdout:{Environment.NewLine}{NormalizeOutput(result.StandardOutput)}{Environment.NewLine}" +
            $"stderr:{Environment.NewLine}{NormalizeOutput(result.StandardError)}";
    }

    private static string FormatCommand(string fileName, IReadOnlyList<string> arguments)
    {
        return $"{fileName} {string.Join(" ", arguments.Select(QuoteArgument))}";
    }

    private static string QuoteArgument(string value)
    {
        return value.Any(char.IsWhiteSpace) ? $"\"{value}\"" : value;
    }

    private static string NormalizeOutput(string value)
    {
        return string.IsNullOrWhiteSpace(value) ? "<empty>" : value.Trim();
    }

    private async Task<PodmanContainerState> InspectContainerStateAsync()
    {
        var inspectResult = await RunProcessAsync(
            "podman",
            new[] { "inspect", "--format", "{{json .State}}", containerName },
            repositoryRoot,
            CommandTimeout);

        if (inspectResult.ExitCode != 0)
        {
            throw new XunitException(
                $"Failed to inspect container '{containerName}' for image '{imageDisplayName}'.{Environment.NewLine}" +
                $"{FormatCommandFailure(inspectResult)}");
        }

        var state = JsonSerializer.Deserialize<PodmanContainerState>(inspectResult.StandardOutput, JsonOptions);

        if (state is null)
        {
            throw new XunitException(
                $"Podman returned an empty container state for '{containerName}'.{Environment.NewLine}{FormatCommandFailure(inspectResult)}");
        }

        return state;
    }

    private async Task<string> BuildStoppedContainerMessageAsync(TimeSpan minimumRuntime, PodmanContainerState state)
    {
        var logsResult = await RunProcessAsync(
            "podman",
            new[] { "logs", containerName },
            repositoryRoot,
            CommandTimeout);

        return
            $"Container for image '{imageDisplayName}' stopped before the required {minimumRuntime.TotalSeconds:0}-second stability window elapsed.{Environment.NewLine}" +
            $"Status: {state.Status ?? "<unknown>"}{Environment.NewLine}" +
            $"Running: {state.Running}{Environment.NewLine}" +
            $"Exit code: {state.ExitCode?.ToString() ?? "<unknown>"}{Environment.NewLine}" +
            $"Error: {state.Error ?? "<none>"}{Environment.NewLine}" +
            $"Logs:{Environment.NewLine}{NormalizeOutput(logsResult.StandardOutput)}{Environment.NewLine}" +
            $"Log stderr:{Environment.NewLine}{NormalizeOutput(logsResult.StandardError)}";
    }

    private async Task CleanupArtifactAsync(
        List<string> failures,
        string artifactType,
        string artifactName,
        IReadOnlyList<string> arguments)
    {
        try
        {
            var result = await RunProcessAsync("podman", arguments, repositoryRoot, CommandTimeout);

            if (result.ExitCode != 0)
            {
                failures.Add(
                    $"Failed to remove {artifactType} '{artifactName}'.{Environment.NewLine}{FormatCommandFailure(result)}");
            }
        }
        catch (Win32Exception exception)
        {
            failures.Add(
                $"Failed to start Podman while removing {artifactType} '{artifactName}': {exception.Message}");
        }
        catch (TimeoutException exception)
        {
            failures.Add(
                $"Timed out while removing {artifactType} '{artifactName}': {exception.Message}");
        }
    }

    private sealed record ProcessResult(
        string FileName,
        IReadOnlyList<string> Arguments,
        int ExitCode,
        string StandardOutput,
        string StandardError);

    private sealed class PodmanContainerState
    {
        public string? Error { get; init; }

        public int? ExitCode { get; init; }

        public bool Running { get; init; }

        public string? Status { get; init; }
    }
}

internal static class TestPaths
{
    public static string GetRepositoryRoot()
    {
        var currentDirectory = new DirectoryInfo(AppContext.BaseDirectory);

        while (currentDirectory is not null)
        {
            if (File.Exists(Path.Combine(currentDirectory.FullName, "SpectreAgentWrapper.sln")))
            {
                return currentDirectory.FullName;
            }

            currentDirectory = currentDirectory.Parent;
        }

        throw new DirectoryNotFoundException(
            "Could not locate the repository root from the test assembly output directory.");
    }
}
