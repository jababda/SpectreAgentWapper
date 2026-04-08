namespace SpectreAgentWrapper.Podman;

public sealed record PodmanWorkspaceContext(
    string WorkspacePath,
    string WorkspaceName,
    string WorkspaceSuffix,
    string ImageName,
    string ContainerName,
    string ImageDefinitionPath,
    string BuildContextPath,
    string BundleRootDirectory);
