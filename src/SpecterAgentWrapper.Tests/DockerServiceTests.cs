using CopilotWrapper;
using NSubstitute;
using NSubstitute.ExceptionExtensions;
using Spectre.Console;
using Xunit;

namespace SpecterAgentWrapper.Tests;

/// <summary>
/// Unit tests for <see cref="DockerService"/>.
/// All external I/O is replaced by <see cref="IProcessRunner"/> and
/// <see cref="IAnsiConsole"/> test doubles so no real Docker daemon is required.
/// </summary>
public class DockerServiceTests
{
    // -----------------------------------------------------------------------
    // Helpers
    // -----------------------------------------------------------------------

    private static IAnsiConsole Console() => Substitute.For<IAnsiConsole>();

    private static ProcessResult Ok(string stdout = "") =>
        new ProcessResult(0, stdout, string.Empty);

    private static ProcessResult Fail(int code = 1) =>
        new ProcessResult(code, string.Empty, string.Empty);

    /// <summary>
    /// Test-friendly subclass that:
    /// - Skips the real Thread.Sleep delay in the retry loop.
    /// - Allows individual virtual methods to be overridden via simple flags
    ///   so that <see cref="DockerService.EnsureDependencies"/> can be tested
    ///   without spinning up any real process.
    /// </summary>
    private sealed class StubDockerService : DockerService
    {
        public bool? InstalledResult { get; set; }
        public bool? RunningResult { get; set; }
        public bool? TryStartResult { get; set; }
        public bool? EnsureImageResult { get; set; }

        public int TryStartDockerCallCount { get; private set; }

        public StubDockerService(IProcessRunner runner, IAnsiConsole console)
            : base(runner, console) { }

        public override bool IsDockerInstalled() =>
            InstalledResult ?? base.IsDockerInstalled();

        public override bool IsDockerRunning() =>
            RunningResult ?? base.IsDockerRunning();

        public override bool TryStartDocker()
        {
            TryStartDockerCallCount++;
            return TryStartResult ?? base.TryStartDocker();
        }

        public override bool EnsureImageAvailable(string image) =>
            EnsureImageResult ?? base.EnsureImageAvailable(image);

        protected override void WaitBeforeRetry() { /* no-op in tests */ }
    }

    // -----------------------------------------------------------------------
    // IsDockerInstalled
    // -----------------------------------------------------------------------

    [Fact]
    public void IsDockerInstalled_ReturnsTrue_WhenDockerVersionSucceeds()
    {
        var runner = Substitute.For<IProcessRunner>();
        runner.Run("docker", "--version").Returns(Ok("Docker version 24.0.0"));

        var svc = new DockerService(runner, Console());

        Assert.True(svc.IsDockerInstalled());
    }

    [Fact]
    public void IsDockerInstalled_ReturnsFalse_WhenDockerVersionFails()
    {
        var runner = Substitute.For<IProcessRunner>();
        runner.Run("docker", "--version").Returns(Fail());

        var svc = new DockerService(runner, Console());

        Assert.False(svc.IsDockerInstalled());
    }

    [Fact]
    public void IsDockerInstalled_ReturnsFalse_WhenRunnerThrows()
    {
        var runner = Substitute.For<IProcessRunner>();
        runner.Run("docker", "--version").Throws(new Exception("not found"));

        var svc = new DockerService(runner, Console());

        Assert.False(svc.IsDockerInstalled());
    }

    // -----------------------------------------------------------------------
    // IsDockerRunning
    // -----------------------------------------------------------------------

    [Fact]
    public void IsDockerRunning_ReturnsTrue_WhenDockerInfoSucceeds()
    {
        var runner = Substitute.For<IProcessRunner>();
        runner.Run("docker", "info").Returns(Ok());

        var svc = new DockerService(runner, Console());

        Assert.True(svc.IsDockerRunning());
    }

    [Fact]
    public void IsDockerRunning_ReturnsFalse_WhenDockerInfoFails()
    {
        var runner = Substitute.For<IProcessRunner>();
        runner.Run("docker", "info").Returns(Fail());

        var svc = new DockerService(runner, Console());

        Assert.False(svc.IsDockerRunning());
    }

    [Fact]
    public void IsDockerRunning_ReturnsFalse_WhenRunnerThrows()
    {
        var runner = Substitute.For<IProcessRunner>();
        runner.Run("docker", "info").Throws(new Exception("daemon not reachable"));

        var svc = new DockerService(runner, Console());

        Assert.False(svc.IsDockerRunning());
    }

    // -----------------------------------------------------------------------
    // TryStartDocker – uses StubDockerService (no sleep)
    // -----------------------------------------------------------------------

    [Fact]
    public void TryStartDocker_ReturnsTrue_WhenStartSucceedsAndDaemonBecomesReady()
    {
        var runner = Substitute.For<IProcessRunner>();

        // The platform-specific start command succeeds.
        runner.Run(Arg.Any<string>(), Arg.Any<string>()).Returns(Ok());

        // Docker info reports ready on the first poll.
        runner.Run("docker", "info").Returns(Ok());

        var svc = new StubDockerService(runner, Console());
        // Bypass the IsDockerRunning override so the real polling runs against runner.
        svc.RunningResult = null;

        Assert.True(svc.TryStartDocker());
    }

    [Fact]
    public void TryStartDocker_ReturnsFalse_WhenStartCommandFails()
    {
        var runner = Substitute.For<IProcessRunner>();

        // Both possible start commands fail.
        runner.Run("sudo", Arg.Any<string>()).Returns(Fail(1));
        runner.Run("powershell", Arg.Any<string>()).Returns(Fail(1));

        var svc = new StubDockerService(runner, Console());

        Assert.False(svc.TryStartDocker());
    }

    [Fact]
    public void TryStartDocker_ReturnsFalse_WhenDaemonNeverBecomesReady()
    {
        var runner = Substitute.For<IProcessRunner>();

        // Start commands succeed but daemon never reports ready.
        runner.Run("sudo", Arg.Any<string>()).Returns(Ok());
        runner.Run("powershell", Arg.Any<string>()).Returns(Ok());
        runner.Run("docker", "info").Returns(Fail(1));

        var svc = new StubDockerService(runner, Console());
        svc.RunningResult = null; // let the real polling run

        Assert.False(svc.TryStartDocker());
    }

    // -----------------------------------------------------------------------
    // EnsureImageAvailable
    // -----------------------------------------------------------------------

    [Fact]
    public void EnsureImageAvailable_ReturnsTrue_WhenImageExistsLocally()
    {
        var runner = Substitute.For<IProcessRunner>();
        runner.Run("docker", "image inspect myimage").Returns(Ok());

        var svc = new DockerService(runner, Console());

        Assert.True(svc.EnsureImageAvailable("myimage"));
    }

    [Fact]
    public void EnsureImageAvailable_PullsAndReturnsTrue_WhenImageNotLocalButPullSucceeds()
    {
        var runner = Substitute.For<IProcessRunner>();
        runner.Run("docker", "image inspect myimage").Returns(Fail());
        runner.Run("docker", "pull myimage", inheritStdio: true).Returns(Ok());

        var svc = new DockerService(runner, Console());

        Assert.True(svc.EnsureImageAvailable("myimage"));
        runner.Received(1).Run("docker", "pull myimage", inheritStdio: true);
    }

    [Fact]
    public void EnsureImageAvailable_ReturnsFalse_WhenImageNotLocalAndPullFails()
    {
        var runner = Substitute.For<IProcessRunner>();
        runner.Run("docker", "image inspect myimage").Returns(Fail());
        runner.Run("docker", "pull myimage", inheritStdio: true).Returns(Fail(1));

        var svc = new DockerService(runner, Console());

        Assert.False(svc.EnsureImageAvailable("myimage"));
    }

    [Fact]
    public void EnsureImageAvailable_ReturnsFalse_WhenRunnerThrows()
    {
        var runner = Substitute.For<IProcessRunner>();
        runner.Run("docker", "image inspect myimage").Throws(new Exception("io error"));

        var svc = new DockerService(runner, Console());

        Assert.False(svc.EnsureImageAvailable("myimage"));
    }

    // -----------------------------------------------------------------------
    // EnsureDependencies – orchestration tests using StubDockerService
    // -----------------------------------------------------------------------

    [Fact]
    public void EnsureDependencies_ReturnsFalse_WhenDockerNotInstalled()
    {
        var svc = new StubDockerService(Substitute.For<IProcessRunner>(), Console())
        {
            InstalledResult = false,
        };

        Assert.False(svc.EnsureDependencies("img"));
    }

    [Fact]
    public void EnsureDependencies_CallsTryStartDocker_WhenDockerNotRunning()
    {
        var svc = new StubDockerService(Substitute.For<IProcessRunner>(), Console())
        {
            InstalledResult = true,
            RunningResult = false,
            TryStartResult = true,
            EnsureImageResult = true,
        };

        svc.EnsureDependencies("img");

        Assert.Equal(1, svc.TryStartDockerCallCount);
    }

    [Fact]
    public void EnsureDependencies_ReturnsFalse_WhenDockerNotRunningAndStartFails()
    {
        var svc = new StubDockerService(Substitute.For<IProcessRunner>(), Console())
        {
            InstalledResult = true,
            RunningResult = false,
            TryStartResult = false,
        };

        Assert.False(svc.EnsureDependencies("img"));
    }

    [Fact]
    public void EnsureDependencies_ReturnsTrue_WhenDockerRunningAndImageAvailable()
    {
        var svc = new StubDockerService(Substitute.For<IProcessRunner>(), Console())
        {
            InstalledResult = true,
            RunningResult = true,
            EnsureImageResult = true,
        };

        Assert.True(svc.EnsureDependencies("img"));
    }

    [Fact]
    public void EnsureDependencies_ReturnsFalse_WhenImageUnavailable()
    {
        var svc = new StubDockerService(Substitute.For<IProcessRunner>(), Console())
        {
            InstalledResult = true,
            RunningResult = true,
            EnsureImageResult = false,
        };

        Assert.False(svc.EnsureDependencies("img"));
    }

    [Fact]
    public void EnsureDependencies_SkipsTryStartDocker_WhenDockerAlreadyRunning()
    {
        var svc = new StubDockerService(Substitute.For<IProcessRunner>(), Console())
        {
            InstalledResult = true,
            RunningResult = true,
            EnsureImageResult = true,
        };

        svc.EnsureDependencies("img");

        Assert.Equal(0, svc.TryStartDockerCallCount);
    }
}
