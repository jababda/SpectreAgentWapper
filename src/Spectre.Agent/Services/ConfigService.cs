using System.Text.Json;

namespace SpectreAgent.Services;

public record AgentConfig
{
    public string GithubToken { get; init; } = string.Empty;
    public string Model { get; init; } = "gpt-4o";
    public List<string> WhitelistedDomains { get; init; } = new();
}

public static class ConfigService
{
    private static readonly string ConfigDir =
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".spectre-agent");

    private static readonly string ConfigFile = Path.Combine(ConfigDir, "config.json");

    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };

    /// <summary>
    /// Persists the supplied config.
    /// Note: <see cref="AgentConfig.WhitelistedDomains"/> in the incoming <paramref name="config"/>
    /// is ignored – the existing list is always preserved so that individual domain additions
    /// (via <see cref="AddWhitelistedDomain"/>) are not accidentally cleared.
    /// Pass a config with an empty <see cref="AgentConfig.WhitelistedDomains"/> list deliberately
    /// to keep the current allowlist intact.
    /// </summary>
    public static void SaveConfig(AgentConfig config)
    {
        Directory.CreateDirectory(ConfigDir);

        // Preserve existing whitelisted domains if not overwriting them
        var existing = LoadConfig();
        var merged = config with
        {
            WhitelistedDomains = existing?.WhitelistedDomains ?? config.WhitelistedDomains
        };

        File.WriteAllText(ConfigFile, JsonSerializer.Serialize(merged, JsonOptions));

        // Restrict permissions so only the owner can read the file
        if (!OperatingSystem.IsWindows())
        {
            try { File.SetUnixFileMode(ConfigFile, UnixFileMode.UserRead | UnixFileMode.UserWrite); }
            catch { /* best-effort */ }
        }
    }

    public static AgentConfig? LoadConfig()
    {
        if (!File.Exists(ConfigFile))
            return null;

        try
        {
            var json = File.ReadAllText(ConfigFile);
            return JsonSerializer.Deserialize<AgentConfig>(json);
        }
        catch
        {
            return null;
        }
    }

    public static void AddWhitelistedDomain(string domain)
    {
        var config = LoadConfig() ?? new AgentConfig();
        if (!config.WhitelistedDomains.Contains(domain, StringComparer.OrdinalIgnoreCase))
        {
            var updated = config with
            {
                WhitelistedDomains = new List<string>(config.WhitelistedDomains) { domain }
            };
            SaveConfig(updated);
        }
    }
}
