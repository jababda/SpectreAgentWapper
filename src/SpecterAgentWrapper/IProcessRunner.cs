namespace CopilotWrapper;

/// <summary>Result of running an external process.</summary>
public record ProcessResult(int ExitCode, string StandardOutput, string StandardError);

/// <summary>
/// Abstraction for launching external processes, enabling test doubles.
/// When <c>inheritStdio</c> is <c>true</c> the process inherits the current
/// stdin/stdout/stderr (interactive mode) and the output fields are empty.
/// </summary>
public interface IProcessRunner
{
    ProcessResult Run(string fileName, string arguments, bool inheritStdio = false);
}
