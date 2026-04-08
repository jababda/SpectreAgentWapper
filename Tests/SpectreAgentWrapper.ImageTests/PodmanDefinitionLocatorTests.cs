using SpectreAgentWrapper.Podman;

namespace SpectreAgentWrapper.ImageTests;

public class PodmanDefinitionLocatorTests
{
    [Fact]
    public void GetDefaultImageDefinitionPath_returns_bundled_podman_file_under_base_directory()
    {
        var tempDirectory = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Path.Combine(tempDirectory, "Podman"));

        try
        {
            var expectedDefinitionPath = Path.Combine(
                tempDirectory,
                "Podman",
                PodmanDefaults.DefaultImageDefinitionFileName);

            File.WriteAllText(expectedDefinitionPath, "FROM scratch");

            var locator = new PodmanDefinitionLocator(tempDirectory);

            Assert.Equal(tempDirectory, locator.GetBundleRootDirectory());
            Assert.Equal(expectedDefinitionPath, locator.GetDefaultImageDefinitionPath());
        }
        finally
        {
            if (Directory.Exists(tempDirectory))
            {
                Directory.Delete(tempDirectory, recursive: true);
            }
        }
    }
}
