using Spectre.Console;
using System.Diagnostics;
using System.Text.Json;

namespace SpectreAgent.Services;

/// <summary>
/// Manages the lifecycle of the hardened Podman container that runs the agents.
/// </summary>
public static class ContainerService
{
    private const string ImageName = "spectre-agent:latest";

    // IPC directory name created inside the repo mount
    private const string IpcDirName = ".spectre-ipc";

    // Permission prompt choices (constants to avoid fragile text matching)
    private const string ChoiceAllowFile = "Allow this file";
    private const string ChoiceAllowAll = "Allow all (approve everything from now on)";
    private const string ChoiceDeny = "Deny";

    /// <summary>
    /// Builds the container image (if not already built) and runs the specified agent inside it,
    /// monitoring the IPC directory for file-creation permission requests.
    /// </summary>
    public static async Task<int> RunAgentAsync(
        string agentName,
        string prompt,
        string repoPath,
        string planFile,
        AgentConfig config,
        bool approveAll)
    {
        // Ensure the container image exists
        if (!await EnsureImageBuiltAsync())
            return 1;

        var ipcDir = Path.Combine(repoPath, IpcDirName);
        Directory.CreateDirectory(Path.Combine(ipcDir, "requests"));
        Directory.CreateDirectory(Path.Combine(ipcDir, "responses"));

        // Write the domain allowlist so the container entrypoint can configure iptables
        var allowlistFile = Path.Combine(ipcDir, "allowed-domains.txt");
        var domains = new List<string>(config.WhitelistedDomains)
        {
            // Always allow GitHub API so the agents can call Copilot
            "api.github.com",
            "copilot-proxy.githubusercontent.com",
            "githubcopilot.com",
        };
        await File.WriteAllLinesAsync(allowlistFile, domains.Distinct(StringComparer.OrdinalIgnoreCase));

        // Resolve the agents source directory (search upward from binary location)
        var agentsDir = ResolveAgentsDirectory();

        // Build the podman run arguments
        var podmanArgs = BuildPodmanArgs(
            agentName: agentName,
            prompt: prompt,
            repoPath: repoPath,
            ipcDir: ipcDir,
            agentsDir: agentsDir,
            planFile: Path.GetRelativePath(repoPath, planFile),
            config: config);

        using var cts = new CancellationTokenSource();

        // Start the IPC watcher in a background task
        var ipcTask = WatchIpcAsync(ipcDir, approveAll, cts.Token);

        // Start the container
        var exitCode = await RunProcessAsync("podman", podmanArgs);

        cts.Cancel();
        await ipcTask.ConfigureAwait(false);

        // Clean up IPC artefacts
        try { Directory.Delete(ipcDir, recursive: true); } catch { /* best-effort */ }

        return exitCode;
    }

    // ─────────────────────────────────────────────────────────────────────────
    // Private helpers
    // ─────────────────────────────────────────────────────────────────────────

    private static async Task<bool> EnsureImageBuiltAsync()
    {
        // Check if image already exists
        var checkResult = await RunProcessAsync("podman", ["image", "exists", ImageName], silent: true);
        if (checkResult == 0)
            return true;

        AnsiConsole.MarkupLine("[yellow]Container image not found – building …[/]");

        // Find the Containerfile
        var containerFile = ResolveContainerfile();
        if (containerFile == null)
        {
            AnsiConsole.MarkupLine("[red]Could not locate the Containerfile. Make sure you run spectre-agent from within the SpectreAgent repository or that the image has been pre-built.[/]");
            return false;
        }

        var buildResult = await RunProcessAsync(
            "podman",
            ["build", "-t", ImageName, "-f", containerFile, Path.GetDirectoryName(containerFile)!]);

        if (buildResult != 0)
        {
            AnsiConsole.MarkupLine("[red]Container image build failed.[/]");
            return false;
        }

        return true;
    }

    private static string[] BuildPodmanArgs(
        string agentName,
        string prompt,
        string repoPath,
        string ipcDir,
        string agentsDir,
        string planFile,
        AgentConfig config)
    {
        var args = new List<string>
        {
            "run", "--rm", "--interactive",

            // Security: drop all capabilities, run as non-root
            "--cap-drop=ALL",
            "--security-opt=no-new-privileges",

            // Use an isolated bridge network for the container.
            // The entrypoint applies iptables rules inside that network namespace so
            // that only whitelisted HTTPS destinations are reachable.
            // NET_ADMIN is needed only at startup (firewall setup) and is held by the
            // root entrypoint process before it drops to the unprivileged agent user.
            "--cap-add=NET_ADMIN",
            "--network=bridge",

            // Mount the repository (read-write so agents can edit files)
            $"--volume={repoPath}:/workspace:Z",

            // Mount agent scripts (read-only)
            $"--volume={agentsDir}:/agents:ro,Z",

            // Environment variables
            $"--env=GITHUB_TOKEN={config.GithubToken}",
            $"--env=COPILOT_MODEL={config.Model}",
            $"--env=AGENT_NAME={agentName}",
            $"--env=AGENT_PROMPT={prompt}",
            $"--env=PLAN_FILE=/workspace/{planFile}",
            "--env=IPC_DIR=/workspace/.spectre-ipc",

            ImageName
        };

        return args.ToArray();
    }

    /// <summary>Watches the IPC directory for permission requests from the container.</summary>
    private static async Task WatchIpcAsync(string ipcDir, bool approveAll, CancellationToken ct)
    {
        var requestsDir = Path.Combine(ipcDir, "requests");
        var responsesDir = Path.Combine(ipcDir, "responses");

        // Track per-directory approvals
        var approvedDirs = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var approveAllLocal = approveAll;

        try
        {
            while (!ct.IsCancellationRequested)
            {
                await Task.Delay(200, ct).ConfigureAwait(false);

                if (!Directory.Exists(requestsDir))
                    continue;

                foreach (var requestFile in Directory.GetFiles(requestsDir, "*.json"))
                {
                    try
                    {
                        var json = await File.ReadAllTextAsync(requestFile, ct);
                        var request = JsonSerializer.Deserialize<PermissionRequest>(json);
                        if (request == null)
                            continue;

                        var responseFile = Path.Combine(responsesDir,
                            Path.GetFileNameWithoutExtension(requestFile) + ".response");

                        if (File.Exists(responseFile))
                            continue; // Already answered

                        bool granted;

                        if (approveAllLocal)
                        {
                            granted = true;
                        }
                        else if (approvedDirs.Contains(request.Directory))
                        {
                            granted = true;
                        }
                        else
                        {
                            // Prompt the user
                            AnsiConsole.WriteLine();
                            AnsiConsole.MarkupLine($"[yellow][[Permission request]][/] The agent wants to create a file:");
                            AnsiConsole.MarkupLine($"  Path: [bold]{Markup.Escape(request.FilePath)}[/]");
                            if (!string.IsNullOrWhiteSpace(request.Reason))
                                AnsiConsole.MarkupLine($"  Reason: {Markup.Escape(request.Reason)}");

                            // Build choice list with a stable Allow-Directory entry
                            var allowDirChoice = $"Allow all files in {request.Directory}";

                            var choice = AnsiConsole.Prompt(
                                new SelectionPrompt<string>()
                                    .Title("How do you want to proceed?")
                                    .AddChoices([
                                        ChoiceAllowFile,
                                        allowDirChoice,
                                        ChoiceAllowAll,
                                        ChoiceDeny
                                    ]));

                            if (choice == ChoiceAllowAll)
                            {
                                approveAllLocal = true;
                                granted = true;
                            }
                            else if (choice == allowDirChoice)
                            {
                                approvedDirs.Add(request.Directory);
                                granted = true;
                            }
                            else if (choice == ChoiceDeny)
                            {
                                granted = false;
                            }
                            else
                            {
                                // ChoiceAllowFile (or any unrecognised value → allow)
                                granted = true;
                            }
                        }

                        await File.WriteAllTextAsync(
                            responseFile,
                            JsonSerializer.Serialize(new PermissionResponse { Granted = granted }),
                            ct);

                        // Remove the request file
                        File.Delete(requestFile);
                    }
                    catch (OperationCanceledException) { throw; }
                    catch { /* skip malformed request */ }
                }
            }
        }
        catch (OperationCanceledException) { /* normal shutdown */ }
    }

    private static string ResolveAgentsDirectory()
    {
        // Search upward from the binary directory for a directory containing agents/planner
        // and agents/executor subdirectories (marker-based discovery).
        for (var dir = new DirectoryInfo(AppContext.BaseDirectory); dir != null; dir = dir.Parent)
        {
            var candidate = Path.Combine(dir.FullName, "agents");
            if (Directory.Exists(Path.Combine(candidate, "planner")) &&
                Directory.Exists(Path.Combine(candidate, "executor")))
            {
                return candidate;
            }
        }

        // Fallback: look in CWD
        var cwdCandidate = Path.GetFullPath(Path.Combine(Directory.GetCurrentDirectory(), "agents"));
        if (Directory.Exists(cwdCandidate))
            return cwdCandidate;

        // Last resort: assume agents/ is next to the binary
        return Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "agents"));
    }

    private static string? ResolveContainerfile()
    {
        // Search upward for a directory containing container/Containerfile
        for (var dir = new DirectoryInfo(AppContext.BaseDirectory); dir != null; dir = dir.Parent)
        {
            var candidate = Path.Combine(dir.FullName, "container", "Containerfile");
            if (File.Exists(candidate))
                return candidate;
        }

        // Fallback: CWD
        var cwdCandidate = Path.GetFullPath(
            Path.Combine(Directory.GetCurrentDirectory(), "container", "Containerfile"));
        return File.Exists(cwdCandidate) ? cwdCandidate : null;
    }

    private static async Task<int> RunProcessAsync(string fileName, string[] args, bool silent = false)
    {
        using var process = new Process();
        process.StartInfo = new ProcessStartInfo
        {
            FileName = fileName,
            UseShellExecute = false,
            CreateNoWindow = false,
        };

        foreach (var arg in args)
            process.StartInfo.ArgumentList.Add(arg);

        if (silent)
        {
            process.StartInfo.RedirectStandardOutput = true;
            process.StartInfo.RedirectStandardError = true;
        }

        process.Start();

        if (silent)
        {
            await process.StandardOutput.ReadToEndAsync();
            await process.StandardError.ReadToEndAsync();
        }

        await process.WaitForExitAsync();
        return process.ExitCode;
    }

    private record PermissionRequest
    {
        public string FilePath { get; init; } = string.Empty;
        public string Directory { get; init; } = string.Empty;
        public string Reason { get; init; } = string.Empty;
    }

    private record PermissionResponse
    {
        public bool Granted { get; init; }
    }
}
