using System.Diagnostics;

namespace SpectreAgent.Services;

/// <summary>
/// Attempts to retrieve a GitHub personal access token via the GitHub CLI.
/// </summary>
public static class GithubTokenService
{
    /// <summary>
    /// Returns the token obtained from <c>gh auth token</c>, or <c>null</c> if the gh CLI is not
    /// installed or the user is not authenticated.
    /// </summary>
    public static string? TryGetTokenFromGhCli()
    {
        try
        {
            using var process = new Process();
            process.StartInfo = new ProcessStartInfo
            {
                FileName = "gh",
                Arguments = "auth token",
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
            };

            process.Start();
            var output = process.StandardOutput.ReadToEnd().Trim();

            // Wait up to 5 seconds; if it times out the output may be incomplete
            bool exited = process.WaitForExit(5_000);
            if (!exited)
            {
                try { process.Kill(); } catch { /* best-effort */ }
                return null;
            }

            return string.IsNullOrWhiteSpace(output) ? null : output;
        }
        catch
        {
            return null;
        }
    }
}
