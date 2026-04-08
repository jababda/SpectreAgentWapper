using Docker.DotNet;
using Docker.DotNet.Models;
using SpectreAgentWrapper.Containers;
using Xunit.Sdk;

namespace SpectreAgentWrapper.ImageTests;

internal sealed class PodmanSmokeTestHarness
{
    private static readonly TimeSpan CommandTimeout = TimeSpan.FromMinutes(10);
    private static readonly TimeSpan PollInterval = TimeSpan.FromSeconds(1);

    private readonly string _repositoryRoot;
    private readonly string _imageFilePath;
    private readonly string _imageDisplayName;
    private readonly string _imageTag;
    private readonly string _containerName;
    private bool _imageBuilt;
    private bool _containerCreated;

    public PodmanSmokeTestHarness(string repositoryRoot, string imageFilePath)
    {
        _repositoryRoot = repositoryRoot;
        _imageFilePath = imageFilePath;

        _imageDisplayName = Path.GetFileNameWithoutExtension(imageFilePath);

        var normalizedName = NormalizeForPodmanName(_imageDisplayName);
        var uniqueSuffix = Guid.NewGuid().ToString("N")[..12];

        _imageTag = $"spectre-agentwrapper-{normalizedName}-{uniqueSuffix}";
        _containerName = $"{_imageTag}-container";
    }

    public static async Task EnsurePodmanAvailableAsync()
    {
        try
        {
            using var clientHandle = CreateClientHandle();
            await clientHandle.Client.System.GetSystemInfoAsync(CancellationToken.None);
        }
        catch (InvalidOperationException exception)
        {
            HandleUnavailable(exception.Message);
        }
        catch (Exception exception) when (exception is DockerApiException or HttpRequestException or TimeoutException)
        {
            HandleUnavailable($"Podman is installed but unavailable. {exception.Message}");
        }
    }

    public async Task AssertImageRunsForMinimumDurationAsync(TimeSpan minimumRuntime)
    {
        using var clientHandle = CreateClientHandle();
        var client = clientHandle.Client;

        await BuildImageAsync(client);
        await StartContainerAsync(client);

        var stabilityDeadline = DateTimeOffset.UtcNow + minimumRuntime;

        while (DateTimeOffset.UtcNow < stabilityDeadline)
        {
            var state = await InspectContainerStateAsync(client);

            if (!state.Running)
            {
                throw new XunitException(await BuildStoppedContainerMessageAsync(client, minimumRuntime, state));
            }

            var remaining = stabilityDeadline - DateTimeOffset.UtcNow;

            if (remaining > TimeSpan.Zero)
            {
                await Task.Delay(remaining < PollInterval ? remaining : PollInterval);
            }
        }

        var finalState = await InspectContainerStateAsync(client);

        if (!finalState.Running)
        {
            throw new XunitException(await BuildStoppedContainerMessageAsync(client, minimumRuntime, finalState));
        }
    }

    public async Task<List<string>> CleanupAsync()
    {
        var failures = new List<string>();

        try
        {
            using var clientHandle = CreateClientHandle();
            var client = clientHandle.Client;

            if (_containerCreated)
            {
                await CleanupContainerAsync(client, failures);
            }

            if (_imageBuilt)
            {
                await CleanupImageAsync(client, failures);
            }
        }
        catch (InvalidOperationException exception)
        {
            failures.Add($"Failed to connect to Podman for cleanup: {exception.Message}");
        }
        catch (Exception exception) when (exception is DockerApiException or HttpRequestException or TimeoutException)
        {
            failures.Add($"Failed to use the Podman API for cleanup: {exception.Message}");
        }

        return failures;
    }

    private static PodmanApiClientHandle CreateClientHandle()
    {
        return new PodmanApiClientFactory(new PodmanApiConnectionResolver()).Create();
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

    private async Task BuildImageAsync(DockerClient client)
    {
        using var buildContextArchive = new BuildContextArchiveFactory().Create(_repositoryRoot);
        var progressMessages = new List<string>();

        try
        {
            await client.Images.BuildImageFromDockerfileAsync(
                new ImageBuildParameters
                {
                    Dockerfile = GetArchivePath(_repositoryRoot, _imageFilePath),
                    Tags = [_imageTag],
                    Remove = true,
                    ForceRemove = true,
                },
                buildContextArchive,
                Array.Empty<AuthConfig>(),
                new Dictionary<string, string>(),
                new Progress<JSONMessage>(message => CollectBuildProgress(progressMessages, message)),
                CancellationToken.None);
        }
        catch (DockerApiException exception)
        {
            throw new XunitException(
                $"Failed to build image '{_imageDisplayName}'.{Environment.NewLine}" +
                $"{FormatBuildFailure(progressMessages, exception.ResponseBody)}");
        }

        _imageBuilt = true;

        var buildError = progressMessages.LastOrDefault(static message => message.StartsWith("ERROR: ", StringComparison.Ordinal));

        if (!string.IsNullOrWhiteSpace(buildError))
        {
            throw new XunitException(
                $"Failed to build image '{_imageDisplayName}'.{Environment.NewLine}" +
                $"{FormatBuildFailure(progressMessages, additionalDetails: null)}");
        }
    }

    private async Task StartContainerAsync(DockerClient client)
    {
        var createResponse = await client.Containers.CreateContainerAsync(
            new CreateContainerParameters
            {
                Name = _containerName,
                Image = _imageTag,
                AttachStdin = true,
                OpenStdin = true,
                Tty = true,
            },
            CancellationToken.None);

        _containerCreated = true;

        var started = await client.Containers.StartContainerAsync(
            createResponse.ID,
            new ContainerStartParameters(),
            CancellationToken.None);

        if (!started)
        {
            throw new XunitException(
                $"Failed to start container for image '{_imageDisplayName}'.{Environment.NewLine}" +
                "Podman returned a non-started container without an API error.");
        }
    }

    private async Task CleanupContainerAsync(DockerClient client, List<string> failures)
    {
        try
        {
            await client.Containers.RemoveContainerAsync(
                _containerName,
                new ContainerRemoveParameters
                {
                    Force = true,
                    RemoveVolumes = true,
                },
                CancellationToken.None);
        }
        catch (DockerContainerNotFoundException)
        {
        }
        catch (DockerApiException exception)
        {
            failures.Add(
                $"Failed to remove container '{_containerName}'.{Environment.NewLine}" +
                $"{FormatApiFailure(exception)}");
        }
    }

    private async Task CleanupImageAsync(DockerClient client, List<string> failures)
    {
        try
        {
            await client.Images.DeleteImageAsync(
                _imageTag,
                new ImageDeleteParameters
                {
                    Force = true,
                },
                CancellationToken.None);
        }
        catch (DockerImageNotFoundException)
        {
        }
        catch (DockerApiException exception)
        {
            failures.Add(
                $"Failed to remove image '{_imageTag}'.{Environment.NewLine}" +
                $"{FormatApiFailure(exception)}");
        }
    }

    private static void CollectBuildProgress(List<string> progressMessages, JSONMessage message)
    {
        var line =
            message.ErrorMessage is { Length: > 0 } errorMessage ? $"ERROR: {errorMessage.Trim()}" :
            message.Error is { Message.Length: > 0 } error ? $"ERROR: {error.Message.Trim()}" :
            message.Stream is { Length: > 0 } stream ? stream.TrimEnd() :
            BuildStatusLine(message);

        if (!string.IsNullOrWhiteSpace(line))
        {
            progressMessages.Add(line);
        }
    }

    private static string? BuildStatusLine(JSONMessage message)
    {
        if (string.IsNullOrWhiteSpace(message.Status))
        {
            return null;
        }

        return string.Join(
            ' ',
            new[]
            {
                message.Status.Trim(),
                message.ID?.Trim(),
                message.ProgressMessage?.Trim(),
            }.Where(static value => !string.IsNullOrWhiteSpace(value)));
    }

    private static string FormatBuildFailure(IReadOnlyList<string> progressMessages, string? additionalDetails)
    {
        var sections = new List<string>
        {
            "Build output:",
            progressMessages.Count == 0 ? "<empty>" : string.Join(Environment.NewLine, progressMessages.TakeLast(80)),
        };

        if (!string.IsNullOrWhiteSpace(additionalDetails))
        {
            sections.Add("API response:");
            sections.Add(additionalDetails.Trim());
        }

        return string.Join(Environment.NewLine, sections);
    }

    private static string FormatApiFailure(DockerApiException exception)
    {
        return
            $"Status code: {(int)exception.StatusCode}{Environment.NewLine}" +
            $"Message: {exception.Message}{Environment.NewLine}" +
            $"Response: {NormalizeOutput(exception.ResponseBody)}";
    }

    private static string NormalizeOutput(string? value)
    {
        return string.IsNullOrWhiteSpace(value) ? "<empty>" : value.Trim();
    }

    private async Task<ContainerState> InspectContainerStateAsync(DockerClient client)
    {
        try
        {
            var inspectResponse = await client.Containers.InspectContainerAsync(
                _containerName,
                CancellationToken.None);

            return inspectResponse.State
                ?? throw new XunitException($"Podman returned an empty container state for '{_containerName}'.");
        }
        catch (DockerApiException exception)
        {
            throw new XunitException(
                $"Failed to inspect container '{_containerName}' for image '{_imageDisplayName}'.{Environment.NewLine}" +
                $"{FormatApiFailure(exception)}");
        }
    }

    private async Task<string> BuildStoppedContainerMessageAsync(
        DockerClient client,
        TimeSpan minimumRuntime,
        ContainerState state)
    {
        var logs = await TryGetContainerLogsAsync(client);

        return
            $"Container for image '{_imageDisplayName}' stopped before the required {minimumRuntime.TotalSeconds:0}-second stability window elapsed.{Environment.NewLine}" +
            $"Status: {state.Status ?? "<unknown>"}{Environment.NewLine}" +
            $"Running: {state.Running}{Environment.NewLine}" +
            $"Exit code: {state.ExitCode}{Environment.NewLine}" +
            $"Error: {state.Error ?? "<none>"}{Environment.NewLine}" +
            $"Logs:{Environment.NewLine}{NormalizeOutput(logs)}";
    }

    private async Task<string> TryGetContainerLogsAsync(DockerClient client)
    {
        try
        {
            using var logStream = await client.Containers.GetContainerLogsAsync(
                _containerName,
                tty: true,
                new ContainerLogsParameters
                {
                    ShowStdout = true,
                    ShowStderr = true,
                },
                CancellationToken.None);

            var (stdout, stderr) = await logStream.ReadOutputToEndAsync(CancellationToken.None);

            return string.Join(
                Environment.NewLine,
                new[]
                {
                    stdout?.Trim(),
                    stderr?.Trim(),
                }.Where(static value => !string.IsNullOrWhiteSpace(value)));
        }
        catch (DockerContainerNotFoundException)
        {
            return string.Empty;
        }
        catch (DockerApiException)
        {
            return string.Empty;
        }
    }

    private static string GetArchivePath(string buildContextPath, string filePath)
    {
        return Path.GetRelativePath(buildContextPath, filePath)
            .Replace(Path.DirectorySeparatorChar, '/')
            .Replace(Path.AltDirectorySeparatorChar, '/');
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
