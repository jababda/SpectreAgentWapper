using System.Runtime.InteropServices;

namespace SpectreAgentWrapper.Containers;

/// <summary>
/// Describes a resolved Podman API endpoint and where it came from.
/// </summary>
/// <remarks>
/// This record is used for diagnostics so higher-level runtime code can explain whether it
/// connected through an environment variable, a default named pipe, or a default socket path.
/// </remarks>
internal sealed record PodmanApiConnection(Uri EndpointUri, string Source);

/// <summary>
/// Resolves which Podman Docker-compatible API endpoint the wrapper should use.
/// </summary>
/// <remarks>
/// This interface centralizes endpoint discovery so the rest of the runtime layer does not need to
/// know about environment-variable precedence or platform-specific socket and named-pipe rules.
/// Its scope stops at discovery; it does not create Docker clients or execute runtime operations.
/// </remarks>
internal interface IPodmanApiConnectionResolver
{
    /// <summary>
    /// Resolves the preferred Podman API endpoint for the current machine and process environment.
    /// </summary>
    /// <returns>The selected endpoint and its source metadata.</returns>
    PodmanApiConnection Resolve();
}

/// <summary>
/// Default endpoint resolver for Podman's Docker-compatible API.
/// </summary>
/// <remarks>
/// Resolution checks <c>DOCKER_HOST</c> and <c>CONTAINER_HOST</c> first, then falls back to
/// platform-specific default named pipes or socket paths. It also performs lightweight local
/// availability checks so callers get a fast, explicit failure when Podman's endpoint is not
/// reachable from the current machine.
/// </remarks>
internal sealed class PodmanApiConnectionResolver : IPodmanApiConnectionResolver
{
    private static readonly string[] EndpointEnvironmentVariables =
    [
        "DOCKER_HOST",
        "CONTAINER_HOST",
    ];

    /// <inheritdoc />
    public PodmanApiConnection Resolve()
    {
        foreach (var environmentVariable in EndpointEnvironmentVariables)
        {
            var configuredEndpoint = Environment.GetEnvironmentVariable(environmentVariable);

            if (!string.IsNullOrWhiteSpace(configuredEndpoint))
            {
                return new PodmanApiConnection(
                    new Uri(configuredEndpoint),
                    $"environment variable '{environmentVariable}'");
            }
        }

        foreach (var candidate in GetDefaultCandidates())
        {
            if (IsAvailable(candidate.EndpointUri))
            {
                return candidate;
            }
        }

        throw new InvalidOperationException(
            "Could not locate a Podman Docker-compatible API endpoint. " +
            "Set DOCKER_HOST or CONTAINER_HOST, or ensure the default Podman socket or named pipe is running.");
    }

    private static IEnumerable<PodmanApiConnection> GetDefaultCandidates()
    {
        if (OperatingSystem.IsWindows())
        {
            foreach (var candidate in GetWindowsCandidates())
            {
                yield return candidate;
            }

            yield break;
        }

        foreach (var candidate in GetUnixCandidates())
        {
            yield return candidate;
        }
    }

    private static IEnumerable<PodmanApiConnection> GetWindowsCandidates()
    {
        var preferredPipeNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "podman-machine-default",
            "podman",
        };

        foreach (var pipeName in preferredPipeNames)
        {
            yield return new PodmanApiConnection(
                CreateNamedPipeUri(pipeName),
                $"named pipe '{pipeName}'");
        }

        foreach (var pipeName in EnumerateNamedPipeNames()
                     .Where(static name => name.StartsWith("podman", StringComparison.OrdinalIgnoreCase))
                     .Where(name => !preferredPipeNames.Contains(name))
                     .OrderBy(static name => name, StringComparer.OrdinalIgnoreCase))
        {
            yield return new PodmanApiConnection(
                CreateNamedPipeUri(pipeName),
                $"named pipe '{pipeName}'");
        }
    }

    private static IEnumerable<PodmanApiConnection> GetUnixCandidates()
    {
        var runtimeDirectory = Environment.GetEnvironmentVariable("XDG_RUNTIME_DIR");

        if (!string.IsNullOrWhiteSpace(runtimeDirectory))
        {
            var runtimeSocketPath = Path.Combine(runtimeDirectory, "podman", "podman.sock");
            yield return new PodmanApiConnection(
                CreateUnixSocketUri(runtimeSocketPath),
                $"socket '{runtimeSocketPath}'");
        }

        var userId = Environment.GetEnvironmentVariable("UID");

        if (!string.IsNullOrWhiteSpace(userId))
        {
            var rootlessSocketPath = $"/run/user/{userId}/podman/podman.sock";
            yield return new PodmanApiConnection(
                CreateUnixSocketUri(rootlessSocketPath),
                $"socket '{rootlessSocketPath}'");
        }

        const string rootSocketPath = "/run/podman/podman.sock";

        yield return new PodmanApiConnection(
            CreateUnixSocketUri(rootSocketPath),
            $"socket '{rootSocketPath}'");
    }

    private static bool IsAvailable(Uri endpointUri)
    {
        return endpointUri.Scheme switch
        {
            "npipe" => NamedPipeExists(endpointUri),
            "unix" => File.Exists(endpointUri.AbsolutePath),
            _ => true,
        };
    }

    private static bool NamedPipeExists(Uri endpointUri)
    {
        try
        {
            var pipeName = endpointUri.AbsolutePath
                .Trim('/')
                .Replace('/', '\\');

            if (pipeName.StartsWith("pipe\\", StringComparison.OrdinalIgnoreCase))
            {
                pipeName = pipeName["pipe\\".Length..];
            }

            return Directory.EnumerateFileSystemEntries(@"\\.\pipe\", pipeName).Any();
        }
        catch (IOException)
        {
            return false;
        }
        catch (UnauthorizedAccessException)
        {
            return false;
        }
        catch (ArgumentException)
        {
            return false;
        }
    }

    private static Uri CreateNamedPipeUri(string pipeName)
    {
        return new($"npipe://./pipe/{pipeName}");
    }

    private static Uri CreateUnixSocketUri(string socketPath)
    {
        var normalizedPath = socketPath.Replace('\\', '/');
        return new($"unix://{normalizedPath}");
    }

    private static IEnumerable<string> EnumerateNamedPipeNames()
    {
        if (!OperatingSystem.IsWindows())
        {
            yield break;
        }

        string[] pipeNames;

        try
        {
            pipeNames = Directory.EnumerateFileSystemEntries(@"\\.\pipe\")
                .Select(Path.GetFileName)
                .Where(static name => !string.IsNullOrWhiteSpace(name))
                .Cast<string>()
                .ToArray();
        }
        catch (IOException)
        {
            yield break;
        }
        catch (UnauthorizedAccessException)
        {
            yield break;
        }

        foreach (var pipeName in pipeNames)
        {
            yield return pipeName;
        }
    }
}
