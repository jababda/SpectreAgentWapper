using Docker.DotNet;
using Docker.DotNet.Models;
using SpectreAgentWrapper.Podman;
using System.Net;
using System.Text;

namespace SpectreAgentWrapper.Containers;

/// <summary>
/// Coordinates the wrapper's image rebuild and interactive container-start workflows.
/// </summary>
/// <remarks>
/// Command handlers depend on this abstraction so they can trigger the full container lifecycle
/// without knowing Docker.DotNet details. Its scope is intentionally high-level: it owns the app's
/// runtime flow while delegating endpoint resolution, client creation, and build-context archiving
/// to the supporting runtime services.
/// </remarks>
public interface IContainerRuntimeOrchestrator
{
    /// <summary>
    /// Rebuilds the workspace-specific default image from the bundled definition files.
    /// </summary>
    /// <param name="context">The workspace-specific Podman image and bundle metadata.</param>
    /// <param name="cancellationToken">Cancels the rebuild operation.</param>
    Task RebuildDefaultImageAsync(PodmanWorkspaceContext context, CancellationToken cancellationToken);

    /// <summary>
    /// Starts the workspace-specific default container and bridges its interactive session to the terminal.
    /// </summary>
    /// <param name="context">The workspace-specific Podman image and container metadata.</param>
    /// <param name="cancellationToken">Cancels the start or interactive session operation.</param>
    Task StartDefaultContainerAsync(PodmanWorkspaceContext context, CancellationToken cancellationToken);
}

/// <summary>
/// Docker.DotNet-backed implementation of <see cref="IContainerRuntimeOrchestrator"/>.
/// </summary>
/// <remarks>
/// This is the runtime entry point for the application. It rebuilds images, ensures the target
/// image exists, removes stale containers, creates and starts containers, relays the interactive
/// terminal session, waits for exit, and formats runtime failures. It intentionally does not own
/// endpoint discovery or Docker client lifetime management; those concerns stay in the dedicated
/// factory and resolver classes.
/// </remarks>
internal sealed class ContainerRuntimeOrchestrator(
    IPodmanApiClientFactory podmanApiClientFactory,
    IBuildContextArchiveFactory buildContextArchiveFactory)
    : IContainerRuntimeOrchestrator
{
    private static readonly IDictionary<string, string> EmptyHeaders = new Dictionary<string, string>();

    /// <inheritdoc />
    public async Task RebuildDefaultImageAsync(PodmanWorkspaceContext context, CancellationToken cancellationToken)
    {
        using var clientHandle = podmanApiClientFactory.Create();
        await EnsurePodmanAvailableAsync(clientHandle, cancellationToken);

        using var buildContextArchive = buildContextArchiveFactory.Create(context.BuildContextPath);
        var progressMessages = new List<string>();

        try
        {
            await clientHandle.Client.Images.BuildImageFromDockerfileAsync(
                new ImageBuildParameters
                {
                    Dockerfile = GetArchivePath(context.BuildContextPath, context.ImageDefinitionPath),
                    Tags = [context.ImageName],
                    Remove = true,
                    ForceRemove = true,
                },
                buildContextArchive,
                Array.Empty<AuthConfig>(),
                EmptyHeaders,
                new Progress<JSONMessage>(message => CollectBuildProgress(progressMessages, message)),
                cancellationToken);
        }
        catch (DockerApiException exception)
        {
            throw new InvalidOperationException(
                BuildFailureMessage(
                    $"Podman failed to rebuild image '{context.ImageName}' via {clientHandle.Connection.Source}.",
                    progressMessages,
                    exception.ResponseBody),
                exception);
        }

        var buildError = progressMessages.LastOrDefault(static message => message.StartsWith("ERROR: ", StringComparison.Ordinal));

        if (!string.IsNullOrWhiteSpace(buildError))
        {
            throw new InvalidOperationException(
                BuildFailureMessage(
                    $"Podman failed to rebuild image '{context.ImageName}' via {clientHandle.Connection.Source}.",
                    progressMessages,
                    additionalDetails: null));
        }
    }

    /// <inheritdoc />
    public async Task StartDefaultContainerAsync(PodmanWorkspaceContext context, CancellationToken cancellationToken)
    {
        using var clientHandle = podmanApiClientFactory.Create();
        await EnsurePodmanAvailableAsync(clientHandle, cancellationToken);

        if (!await ImageExistsAsync(clientHandle.Client, context.ImageName, cancellationToken))
        {
            await RebuildDefaultImageAsync(context, cancellationToken);
        }

        using var runtimeClientHandle = podmanApiClientFactory.Create();
        await EnsurePodmanAvailableAsync(runtimeClientHandle, cancellationToken);

        var client = runtimeClientHandle.Client;

        await RemoveExistingContainerIfPresentAsync(client, context.ContainerName, cancellationToken);

        var createResponse = await client.Containers.CreateContainerAsync(
            CreateContainerParameters(context),
            cancellationToken);

        try
        {
            using var attachedStream = await client.Containers.AttachContainerAsync(
                createResponse.ID,
                tty: true,
                new ContainerAttachParameters
                {
                    Stream = true,
                    Stdin = true,
                    Stdout = true,
                    Stderr = true,
                },
                cancellationToken);

            var started = await client.Containers.StartContainerAsync(
                createResponse.ID,
                new ContainerStartParameters(),
                cancellationToken);

            if (!started)
            {
                throw new InvalidOperationException(
                    $"Podman created container '{context.ContainerName}' but it did not start.");
            }

            using var standardInput = Console.OpenStandardInput();
            using var standardOutput = Console.OpenStandardOutput();
            using var standardError = Console.OpenStandardError();

            var relayTask = attachedStream.CopyOutputToAsync(
                standardInput,
                standardOutput,
                standardError,
                cancellationToken);

            var waitResponse = await client.Containers.WaitContainerAsync(createResponse.ID, cancellationToken);
            await relayTask;

            if (waitResponse.Error is not null || waitResponse.StatusCode != 0)
            {
                throw new InvalidOperationException(
                    await BuildContainerExitMessageAsync(
                        client,
                        context.ContainerName,
                        createResponse.ID,
                        waitResponse,
                        cancellationToken));
            }
        }
        finally
        {
            await RemoveContainerIfPresentAsync(client, createResponse.ID, cancellationToken);
        }
    }

    private static CreateContainerParameters CreateContainerParameters(PodmanWorkspaceContext context)
    {
        var environmentVariables = new List<string>();
        var personalAccessToken = Environment.GetEnvironmentVariable("GH_COPILOT_PAT");

        if (!string.IsNullOrWhiteSpace(personalAccessToken))
        {
            environmentVariables.Add($"GH_COPILOT_PAT={personalAccessToken}");
        }

        return new CreateContainerParameters
        {
            Name = context.ContainerName,
            Image = context.ImageName,
            AttachStdin = true,
            AttachStdout = true,
            AttachStderr = true,
            OpenStdin = true,
            Tty = true,
            Env = environmentVariables,
            HostConfig = new HostConfig
            {
                Binds =
                [
                    $"{context.WorkspacePath}:{PodmanDefaults.MountedWorkspacePath}",
                ],
            },
        };
    }

    private static async Task<bool> ImageExistsAsync(
        DockerClient client,
        string imageName,
        CancellationToken cancellationToken)
    {
        try
        {
            await client.Images.InspectImageAsync(imageName, cancellationToken);
            return true;
        }
        catch (DockerImageNotFoundException)
        {
            return false;
        }
        catch (DockerApiException exception) when (exception.StatusCode == HttpStatusCode.NotFound)
        {
            return false;
        }
    }

    private static async Task RemoveExistingContainerIfPresentAsync(
        DockerClient client,
        string containerName,
        CancellationToken cancellationToken)
    {
        try
        {
            await client.Containers.RemoveContainerAsync(
                containerName,
                new ContainerRemoveParameters
                {
                    Force = true,
                    RemoveVolumes = true,
                },
                cancellationToken);
        }
        catch (DockerContainerNotFoundException)
        {
        }
        catch (DockerApiException exception) when (exception.StatusCode == HttpStatusCode.NotFound)
        {
        }
    }

    private static async Task RemoveContainerIfPresentAsync(
        DockerClient client,
        string containerId,
        CancellationToken cancellationToken)
    {
        try
        {
            await client.Containers.RemoveContainerAsync(
                containerId,
                new ContainerRemoveParameters
                {
                    Force = true,
                    RemoveVolumes = true,
                },
                cancellationToken);
        }
        catch (DockerContainerNotFoundException)
        {
        }
        catch (DockerApiException exception) when (exception.StatusCode == HttpStatusCode.NotFound)
        {
        }
    }

    private static async Task EnsurePodmanAvailableAsync(
        PodmanApiClientHandle clientHandle,
        CancellationToken cancellationToken)
    {
        try
        {
            await clientHandle.Client.System.GetSystemInfoAsync(cancellationToken);
        }
        catch (Exception exception) when (exception is DockerApiException or HttpRequestException or TimeoutException)
        {
            throw new InvalidOperationException(
                $"Timed out or failed while connecting to the Podman API at {clientHandle.Connection.EndpointUri}. " +
                "Set DOCKER_HOST or CONTAINER_HOST to a reachable Podman endpoint, or ensure the default Podman socket or named pipe is running.",
                exception);
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

        var builder = new StringBuilder(message.Status.Trim());

        if (!string.IsNullOrWhiteSpace(message.ID))
        {
            builder.Append(' ');
            builder.Append(message.ID.Trim());
        }

        if (!string.IsNullOrWhiteSpace(message.ProgressMessage))
        {
            builder.Append(' ');
            builder.Append(message.ProgressMessage.Trim());
        }

        return builder.ToString();
    }

    private static string BuildFailureMessage(
        string heading,
        IReadOnlyList<string> progressMessages,
        string? additionalDetails)
    {
        var builder = new StringBuilder()
            .AppendLine(heading)
            .AppendLine("Build output:");

        if (progressMessages.Count == 0)
        {
            builder.AppendLine("<empty>");
        }
        else
        {
            foreach (var line in progressMessages.TakeLast(80))
            {
                builder.AppendLine(line);
            }
        }

        if (!string.IsNullOrWhiteSpace(additionalDetails))
        {
            builder.AppendLine("API response:");
            builder.AppendLine(additionalDetails.Trim());
        }

        return builder.ToString().TrimEnd();
    }

    private static async Task<string> BuildContainerExitMessageAsync(
        DockerClient client,
        string containerName,
        string containerId,
        ContainerWaitResponse waitResponse,
        CancellationToken cancellationToken)
    {
        var builder = new StringBuilder()
            .AppendLine($"Container '{containerName}' exited with code {waitResponse.StatusCode}.")
            .AppendLine($"API error: {waitResponse.Error?.Message ?? "<none>"}");

        var logs = await TryGetContainerLogsAsync(client, containerId, cancellationToken);

        builder.AppendLine("Container logs:");
        builder.AppendLine(string.IsNullOrWhiteSpace(logs) ? "<empty>" : logs.Trim());

        return builder.ToString().TrimEnd();
    }

    private static async Task<string> TryGetContainerLogsAsync(
        DockerClient client,
        string containerId,
        CancellationToken cancellationToken)
    {
        try
        {
            using var logStream = await client.Containers.GetContainerLogsAsync(
                containerId,
                tty: true,
                new ContainerLogsParameters
                {
                    ShowStdout = true,
                    ShowStderr = true,
                    Tail = "200",
                },
                cancellationToken);

            var (stdout, stderr) = await logStream.ReadOutputToEndAsync(cancellationToken);

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
        catch (DockerApiException exception) when (exception.StatusCode == HttpStatusCode.NotFound)
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
