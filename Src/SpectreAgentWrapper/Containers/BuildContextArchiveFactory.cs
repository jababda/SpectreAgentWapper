using System.Formats.Tar;

namespace SpectreAgentWrapper.Containers;

/// <summary>
/// Creates tar archives for Docker.DotNet image-build calls from an on-disk build context.
/// </summary>
/// <remarks>
/// This factory is intentionally narrow in scope: it only packages files and directories into the
/// stream shape required by the image-build API. It does not resolve Podman endpoints or execute
/// any build or container lifecycle operations.
/// </remarks>
internal interface IBuildContextArchiveFactory
{
    /// <summary>
    /// Archives the supplied build context directory into an in-memory tar stream.
    /// </summary>
    /// <param name="buildContextPath">The root directory to package for the image build.</param>
    /// <returns>A seekable tar stream positioned at the beginning.</returns>
    MemoryStream Create(string buildContextPath);
}

/// <summary>
/// Default implementation of <see cref="IBuildContextArchiveFactory"/> for the wrapper's bundled image definitions.
/// </summary>
/// <remarks>
/// Use this when a caller needs to rebuild an image from the shipped <c>Podman\</c> files and the
/// surrounding build context directory. The implementation deliberately stays file-focused so the
/// higher-level runtime orchestrator remains responsible for all Docker.DotNet and Podman behavior.
/// </remarks>
internal sealed class BuildContextArchiveFactory : IBuildContextArchiveFactory
{
    /// <inheritdoc />
    public MemoryStream Create(string buildContextPath)
    {
        var archiveStream = new MemoryStream();
        TarFile.CreateFromDirectory(buildContextPath, archiveStream, includeBaseDirectory: false);
        archiveStream.Position = 0;
        return archiveStream;
    }
}
