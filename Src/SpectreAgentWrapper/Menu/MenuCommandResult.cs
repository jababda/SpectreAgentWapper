namespace SpectreAgentWrapper.Menu;

public sealed record MenuCommandResult(bool ShouldExit, string? Message = null);
