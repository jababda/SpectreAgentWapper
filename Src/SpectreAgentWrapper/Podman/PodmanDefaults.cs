namespace SpectreAgentWrapper.Podman;

internal static class PodmanDefaults
{
    public const string DefaultImageBaseName = "spectre-copilot-default";
    public const string DefaultImageDefinitionFileName = "dotnet10.Podman";
    public const string MountedWorkspacePath = "/workspace";
}
