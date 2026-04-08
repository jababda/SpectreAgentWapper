using SpectreAgentWrapper.Podman;

namespace SpectreAgentWrapper.ImageTests;

public class PodmanWorkspaceContextFactoryTests
{
    [Fact]
    public void Create_appends_sanitized_workspace_name_to_image_and_container_names()
    {
        var bundleRootDirectory = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Path.Combine(bundleRootDirectory, "Podman"));

        try
        {
            File.WriteAllText(
                Path.Combine(bundleRootDirectory, "Podman", PodmanDefaults.DefaultImageDefinitionFileName),
                "FROM scratch");

            var locator = new PodmanDefinitionLocator(bundleRootDirectory);
            var factory = new PodmanWorkspaceContextFactory(locator);
            var workspacePath = Path.Combine(Path.GetTempPath(), "My Workspace!");

            var context = factory.Create(workspacePath);

            Assert.Equal(Path.GetFullPath(workspacePath), context.WorkspacePath);
            Assert.Equal("My Workspace!", context.WorkspaceName);
            Assert.Equal("my-workspace", context.WorkspaceSuffix);
            Assert.Equal("spectre-copilot-default-my-workspace", context.ImageName);
            Assert.Equal("spectre-copilot-default-my-workspace-container", context.ContainerName);
            Assert.Equal(bundleRootDirectory, context.BuildContextPath);
            Assert.Equal(bundleRootDirectory, context.BundleRootDirectory);
        }
        finally
        {
            if (Directory.Exists(bundleRootDirectory))
            {
                Directory.Delete(bundleRootDirectory, recursive: true);
            }
        }
    }

    [Fact]
    public void Create_uses_workspace_fallback_when_folder_name_sanitizes_to_empty()
    {
        var bundleRootDirectory = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Path.Combine(bundleRootDirectory, "Podman"));

        try
        {
            File.WriteAllText(
                Path.Combine(bundleRootDirectory, "Podman", PodmanDefaults.DefaultImageDefinitionFileName),
                "FROM scratch");

            var locator = new PodmanDefinitionLocator(bundleRootDirectory);
            var factory = new PodmanWorkspaceContextFactory(locator);
            var workspacePath = Path.Combine(Path.GetTempPath(), "!!!");

            var context = factory.Create(workspacePath);

            Assert.Equal("workspace", context.WorkspaceSuffix);
            Assert.Equal("spectre-copilot-default-workspace", context.ImageName);
            Assert.Equal("spectre-copilot-default-workspace-container", context.ContainerName);
        }
        finally
        {
            if (Directory.Exists(bundleRootDirectory))
            {
                Directory.Delete(bundleRootDirectory, recursive: true);
            }
        }
    }
}
