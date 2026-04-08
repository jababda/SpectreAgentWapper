using Docker.DotNet;

namespace SpectreAgentWrapper.Containers;

/// <summary>
/// Creates short-lived Docker.DotNet clients for Podman's Docker-compatible API.
/// </summary>
/// <remarks>
/// This abstraction exists so the rest of the runtime layer can request a ready-to-use client
/// without duplicating endpoint resolution and disposal logic. Its scope is limited to client
/// construction and lifetime management; it does not decide which container operations to run.
/// </remarks>
internal interface IPodmanApiClientFactory
{
    /// <summary>
    /// Creates a Docker.DotNet client handle bound to the resolved Podman API endpoint.
    /// </summary>
    /// <returns>A disposable handle containing the client and connection metadata.</returns>
    PodmanApiClientHandle Create();
}

/// <summary>
/// Default implementation of <see cref="IPodmanApiClientFactory"/> for this wrapper.
/// </summary>
/// <remarks>
/// Use this factory whenever app code needs a Docker.DotNet client for Podman. It combines
/// endpoint resolution with a small amount of connection configuration, then hands back a
/// disposable handle so callers can keep client lifetime scoped to a single operation.
/// </remarks>
internal sealed class PodmanApiClientFactory(IPodmanApiConnectionResolver podmanApiConnectionResolver)
    : IPodmanApiClientFactory
{
    /// <inheritdoc />
    public PodmanApiClientHandle Create()
    {
        var connection = podmanApiConnectionResolver.Resolve();
        var configuration = new DockerClientConfiguration(
            connection.EndpointUri,
            namedPipeConnectTimeout: TimeSpan.FromSeconds(5));

        return new PodmanApiClientHandle(
            configuration,
            configuration.CreateClient(),
            connection);
    }
}

/// <summary>
/// Bundles a Docker.DotNet client together with the Podman connection details that produced it.
/// </summary>
/// <remarks>
/// The handle keeps disposal simple for callers and preserves the resolved endpoint metadata for
/// diagnostics. Its scope is intentionally small: it is only a transport container for client
/// lifetime and connection context.
/// </remarks>
internal sealed class PodmanApiClientHandle(
    DockerClientConfiguration configuration,
    DockerClient client,
    PodmanApiConnection connection)
    : IDisposable
{
    /// <summary>
    /// Gets the Docker.DotNet client configured for the resolved Podman API endpoint.
    /// </summary>
    public DockerClient Client { get; } = client;

    /// <summary>
    /// Gets the Podman endpoint metadata that was used to create <see cref="Client"/>.
    /// </summary>
    public PodmanApiConnection Connection { get; } = connection;

    /// <summary>
    /// Disposes the Docker.DotNet client and its underlying configuration.
    /// </summary>
    public void Dispose()
    {
        Client.Dispose();
        configuration.Dispose();
    }
}
