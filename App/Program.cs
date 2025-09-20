using System.Text.RegularExpressions;
using Npgsql;

static class Env
{
    public static string Get(string key, string? def = null) =>
        Environment.GetEnvironmentVariable(key) ?? def ?? "";

    public static int GetInt(string key, int def)
        => int.TryParse(Environment.GetEnvironmentVariable(key), out var v) ? v : def;
}

static class Log
{
    private static string? _dupPath;
    private static readonly object _lock = new();

    public static void Init(string? duplicatePath)
    {
        if (!string.IsNullOrWhiteSpace(duplicatePath))
        {
            _dupPath = duplicatePath;
            Directory.CreateDirectory(Path.GetDirectoryName(_dupPath!) ?? ".");
        }
    }

    public static void Info(string msg)
    {
        Console.Out.WriteLine(msg);
        Duplicate("[INFO] " + msg);
    }

    public static void Warn(string msg)
    {
        Console.Out.WriteLine(msg);
        Duplicate("[WARN] " + msg);
    }

    public static void Error(string msg)
    {
        Console.Error.WriteLine(msg);
        Duplicate("[ERROR] " + msg);
    }

    private static void Duplicate(string line)
    {
        if (_dupPath is null) return;
        lock (_lock)
        {
            File.AppendAllText(_dupPath, $"{DateTimeOffset.UtcNow:o} {line}{Environment.NewLine}");
        }
    }
}

static class VersionValidator
{
    private static readonly Regex Typical = new(@"^PostgreSQL\s+\d+(\.\d+)?", RegexOptions.Compiled);

    public static bool IsTypical(string? v) => v is not null && Typical.IsMatch(v);
}

static class ConnectionFactory
{
    public static NpgsqlConnection Create(AppConfig cfg, string user, string password)
    {
        var csb = new NpgsqlConnectionStringBuilder
        {
            Host = cfg.Host,
            Port = cfg.Port,
            Database = cfg.Database,
            Username = user,
            Password = password,
            SslMode = ParseSsl(cfg.SslMode),
            Timeout = Math.Max(1, cfg.TimeoutSeconds),
            CommandTimeout = Math.Max(1, cfg.CommandTimeoutSeconds),
        };
        return new NpgsqlConnection(csb.ConnectionString);
    }

    private static Npgsql.SslMode ParseSsl(string v) =>
        Enum.TryParse<Npgsql.SslMode>(v, ignoreCase: true, out var m) ? m : Npgsql.SslMode.Disable;
}

static class Program
{
    private static readonly Regex UserRegex = new(@"^[A-Za-z0-9_.]{1,64}$", RegexOptions.Compiled);

    public static async Task<int> Main()
    {
        try
        {
  
            var cfg = AppConfig.Load();

          
            var user = Env.Get("DB_USER");
            var pass = Env.Get("DB_PASSWORD");
            var intervalSec = Env.GetInt("PINGER_INTERVAL_SECONDS", 300);
            var logFile = Env.Get("PINGER_LOG_FILE", null);

            if (string.IsNullOrWhiteSpace(user) || string.IsNullOrEmpty(pass))
            {
                Log.Error("DB_USER/DB_PASSWORD не заданы в окружении.");
                return 2;
            }
            if (!UserRegex.IsMatch(user))
            {
                Log.Error("DB_USER содержит недопустимые символы.");
                return 2;
            }

            intervalSec = Math.Clamp(intervalSec, 5, 24*60*60); // 5с..24ч
            Log.Init(logFile);

            Log.Info($"Старт пингера. Хост={cfg.Host}:{cfg.Port}, БД={cfg.Database}, SSL={cfg.SslMode}, интервал={intervalSec}с");

            using var cts = new CancellationTokenSource();
            Console.CancelKeyPress += (_, e) => { e.Cancel = true; cts.Cancel(); };

            
            while (!cts.IsCancellationRequested)
            {
                var started = DateTimeOffset.UtcNow;

                try
                {
                    await PingOnce(cfg, user, pass, cts.Token);
                }
                catch (OperationCanceledException) when (cts.IsCancellationRequested)
                {
                    break;
                }
                catch (Exception ex)
                {
                   
                    Log.Error($"Ошибка пинга: {ex.GetType().Name}: {ex.Message}");
                }

                
                var elapsed = (int)(DateTimeOffset.UtcNow - started).TotalSeconds;
                var delay = Math.Max(0, intervalSec - elapsed);
                try
                {
                    await Task.Delay(TimeSpan.FromSeconds(delay), cts.Token);
                }
                catch (OperationCanceledException) { break; }
            }

            Log.Info("Завершение пингера.");
            return 0;
        }
        catch (Exception ex)
        {
            Log.Error($"Критическая ошибка: {ex}");
            return 1;
        }
    }

    private static async Task PingOnce(AppConfig cfg, string user, string pass, CancellationToken ct)
    {
      
        using var opCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        opCts.CancelAfter(TimeSpan.FromSeconds(Math.Max(5, cfg.TimeoutSeconds + cfg.CommandTimeoutSeconds + 2)));

        await using var conn = ConnectionFactory.Create(cfg, user, pass);
        await conn.OpenAsync(opCts.Token);

        await using var cmd = new NpgsqlCommand("SELECT version();", conn);
        cmd.CommandTimeout = Math.Max(1, cfg.CommandTimeoutSeconds);

        var result = await cmd.ExecuteScalarAsync(opCts.Token);
        var version = result?.ToString();

        if (VersionValidator.IsTypical(version))
        {
           
            Log.Info($"OK: {version}");
        }
        else
        {
           
            Log.Warn($"ATYPICAL VERSION: {(string.IsNullOrWhiteSpace(version) ? "<empty>" : version)}");
        }
    }
}
