using System.Text.Json;
using System.Text.Json.Serialization;

namespace AccessibilityAgent.Agent;

internal sealed class AgentCredentialStore
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    public static string GetDefaultPath()
    {
        var xdg = Environment.GetEnvironmentVariable("XDG_CONFIG_HOME");
        string baseDir = !string.IsNullOrWhiteSpace(xdg)
            ? xdg!
            : Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".config");
        return Path.Combine(baseDir, "accessibilityagent", "agent.json");
    }

    public static AgentCredentials? Load(string path)
    {
        try
        {
            if (!File.Exists(path))
            {
                return null;
            }
            var json = File.ReadAllText(path);
            var data = JsonSerializer.Deserialize(json, AgentJsonContext.Default.AgentCredentials);
            return data;
        }
        catch
        {
            return null;
        }
    }

    public static bool Save(string path, AgentCredentials creds)
    {
        try
        {
            var dir = Path.GetDirectoryName(path);
            if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
            {
                Directory.CreateDirectory(dir);
            }
            var json = JsonSerializer.Serialize(creds, AgentJsonContext.Default.AgentCredentials);
            File.WriteAllText(path, json);
#if !WINDOWS
            try
            {
                // Установить права 600, чтобы токен был доступен только владельцу
                UnixSetFilePermissions600(path);
            }
            catch { /* best-effort */ }
#endif
            return true;
        }
        catch
        {
            return false;
        }
    }

#if !WINDOWS
    private static void UnixSetFilePermissions600(string path)
    {
        // 384 == 0o600
        const int mode = 384;
        chmod(path, mode);
    }

    [System.Runtime.InteropServices.DllImport("libc", SetLastError = true)]
    private static extern int chmod(string pathname, int mode);
#endif
}

internal sealed class AgentCredentials
{
    public string? AgentName { get; set; }
    public string? ServerUrl { get; set; }
    public string? Token { get; set; }
    public DateTimeOffset? IssuedAt { get; set; }
    public Dictionary<string, string>? Metadata { get; set; }
}
