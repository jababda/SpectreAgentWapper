namespace SpectreAgentWrapper.ImageTests;

public class PodmanImageSmokeTests
{
    public static TheoryData<string> ImageDefinitionFiles
    {
        get
        {
            var repositoryRoot = TestPaths.GetRepositoryRoot();
            var podmanDirectory = Path.Combine(repositoryRoot, "Podman");

            var imageFiles = Directory
                .EnumerateFiles(podmanDirectory, "*", SearchOption.TopDirectoryOnly)
                .Where(path => path.EndsWith(".Podman", StringComparison.OrdinalIgnoreCase))
                .OrderBy(path => path, StringComparer.OrdinalIgnoreCase)
                .ToArray();

            if (imageFiles.Length == 0)
            {
                throw new InvalidOperationException($"No Podman image definitions were found under '{podmanDirectory}'.");
            }

            var data = new TheoryData<string>();

            foreach (var imageFile in imageFiles)
            {
                data.Add(imageFile);
            }

            return data;
        }
    }

    [Theory]
    [MemberData(nameof(ImageDefinitionFiles))]
    public async Task Image_builds_and_stays_running_for_ten_seconds(string imageFilePath)
    {
        await PodmanSmokeTestHarness.EnsurePodmanAvailableAsync();

        var harness = new PodmanSmokeTestHarness(TestPaths.GetRepositoryRoot(), imageFilePath);
        List<string> cleanupFailures;

        try
        {
            await harness.AssertImageRunsForMinimumDurationAsync(TimeSpan.FromSeconds(10));
        }
        finally
        {
            cleanupFailures = await harness.CleanupAsync();
        }

        Assert.True(
            cleanupFailures.Count == 0,
            $"Podman smoke-test cleanup failed:{Environment.NewLine}{string.Join(Environment.NewLine, cleanupFailures)}");
    }
}
