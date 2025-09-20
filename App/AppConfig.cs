using System.Text.Json;

public sealed class AppConfig
{
    public string Host { get; init; } = "localhost";
    public int Port { get; init; } = 5432;
    public string Database { get; init; } = "appdb";
    public string SslMode { get; init; } = "Disable";
    public int TimeoutSeconds { get; init; } = 5;
    public int CommandTimeoutSeconds { get; init; } = 5;

    public static AppConfig Load(string path = "appsettings.json")
    {
        using var fs = File.OpenRead(path);
        var cfg = JsonSerializer.Deserialize<AppConfig>(fs, new JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = true
        });
        if (cfg is null) throw new InvalidOperationException("Invalid config file");
        return cfg;
    }
}
