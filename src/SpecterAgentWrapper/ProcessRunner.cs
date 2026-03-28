using System.Diagnostics;

namespace CopilotWrapper;

/// <summary>Real implementation of <see cref="IProcessRunner"/> that shells out to the OS.</summary>
public class ProcessRunner : IProcessRunner
{
    public ProcessResult Run(string fileName, string arguments, bool inheritStdio = false)
    {
        var startInfo = new ProcessStartInfo
        {
            FileName = fileName,
            Arguments = arguments,
            UseShellExecute = false,
            RedirectStandardOutput = !inheritStdio,
            RedirectStandardError = !inheritStdio,
        };

        using var process = new Process { StartInfo = startInfo };
        process.Start();

        var stdout = inheritStdio ? string.Empty : process.StandardOutput.ReadToEnd();
        var stderr = inheritStdio ? string.Empty : process.StandardError.ReadToEnd();
        process.WaitForExit();

        return new ProcessResult(process.ExitCode, stdout, stderr);
    }
}
