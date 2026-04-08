namespace SpectreAgentWrapper.Podman;

internal interface IPodmanDefinitionLocator
{
    string GetBundleRootDirectory();

    string GetDefaultImageDefinitionPath();
}

internal sealed class PodmanDefinitionLocator : IPodmanDefinitionLocator
{
    private readonly string _bundleRootDirectory;

    public PodmanDefinitionLocator()
        : this(AppContext.BaseDirectory)
    {
    }

    internal PodmanDefinitionLocator(string bundleRootDirectory)
    {
        this._bundleRootDirectory = Path.GetFullPath(bundleRootDirectory);
    }

    public string GetBundleRootDirectory()
    {
        return _bundleRootDirectory;
    }

    public string GetDefaultImageDefinitionPath()
    {
        var definitionPath = Path.Combine(
            _bundleRootDirectory,
            "Podman",
            PodmanDefaults.DefaultImageDefinitionFileName);

        if (!File.Exists(definitionPath))
        {
            throw new FileNotFoundException(
                $"Could not find bundled Podman definition '{definitionPath}'. " +
                "Build or publish the application so the Podman files are copied next to the executable.",
                definitionPath);
        }

        return definitionPath;
    }
}
