using System.Text;

namespace SpectreAgentWrapper.Podman;

public interface IPodmanWorkspaceContextFactory
{
    PodmanWorkspaceContext Create();

    PodmanWorkspaceContext Create(string workspacePath);
}

internal sealed class PodmanWorkspaceContextFactory(
    IPodmanDefinitionLocator definitionLocator)
    : IPodmanWorkspaceContextFactory
{
    public PodmanWorkspaceContext Create()
    {
        return Create(Environment.CurrentDirectory);
    }

    public PodmanWorkspaceContext Create(string workspacePath)
    {
        var fullWorkspacePath = Path.GetFullPath(workspacePath);
        var workspaceName = Path.GetFileName(fullWorkspacePath.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));

        if (string.IsNullOrWhiteSpace(workspaceName))
        {
            workspaceName = "workspace";
        }

        var workspaceSuffix = SanitizeName(workspaceName);
        var imageName = $"{PodmanDefaults.DefaultImageBaseName}-{workspaceSuffix}";

        return new PodmanWorkspaceContext(
            fullWorkspacePath,
            workspaceName,
            workspaceSuffix,
            imageName,
            $"{imageName}-container",
            definitionLocator.GetDefaultImageDefinitionPath(),
            definitionLocator.GetBundleRootDirectory(),
            definitionLocator.GetBundleRootDirectory());
    }

    private static string SanitizeName(string value)
    {
        var builder = new StringBuilder();
        var previousWasSeparator = false;

        foreach (var character in value.Trim().ToLowerInvariant())
        {
            if (char.IsLetterOrDigit(character))
            {
                builder.Append(character);
                previousWasSeparator = false;
                continue;
            }

            if (previousWasSeparator)
            {
                continue;
            }

            builder.Append('-');
            previousWasSeparator = true;
        }

        var sanitized = builder.ToString().Trim('-');
        return string.IsNullOrWhiteSpace(sanitized) ? "workspace" : sanitized;
    }
}
