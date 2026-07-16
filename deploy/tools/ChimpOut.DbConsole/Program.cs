using System.Diagnostics;
using System.Text;
using System.Text.Json;
using Npgsql;

// chimpout-db — interactive read-write REPL over the game or messaging DB via the
// Cloud SQL Auth Proxy + IAM. Reads freely; any write requires a typed confirmation.
//
// Usage: chimpout-db --db game|messaging [--env production] [--secrets <path>] [--local-port 5433]

var opts = Options.Parse(args);
if (opts.ShowHelp) { Options.PrintUsage(); return 0; }

Config cfg;
try
{
    cfg = Config.Load(opts);
}
catch (Exception ex)
{
    Console.Error.WriteLine($"error: {ex.Message}");
    return 1;
}

var instanceConnectionName = $"{cfg.ProjectId}:{cfg.Region}:chimpout-pg";
Console.WriteLine($"Starting Cloud SQL Auth Proxy for {instanceConnectionName} on 127.0.0.1:{opts.LocalPort}…");

Process? proxy = null;
try
{
    proxy = StartProxy(instanceConnectionName, opts.LocalPort);

    var connString = new NpgsqlConnectionStringBuilder
    {
        Host = "127.0.0.1",
        Port = opts.LocalPort,
        Database = cfg.Database,
        Username = cfg.Username,
        Password = cfg.Password,
        Timeout = 15,
    }.ConnectionString;

    using var conn = new NpgsqlConnection(connString);
    conn.Open();
    Console.WriteLine($"Connected to '{cfg.Database}'. Type SQL; \\q to quit. Writes require confirmation.\n");

    Repl(conn);
    return 0;
}
catch (Exception ex)
{
    Console.Error.WriteLine($"error: {ex.Message}");
    return 1;
}
finally
{
    if (proxy is { HasExited: false })
    {
        try { proxy.Kill(entireProcessTree: true); } catch { /* best effort */ }
    }
}

static Process StartProxy(string instanceConnectionName, int localPort)
{
    // Cloud SQL Auth Proxy v2: `cloud-sql-proxy <INSTANCE> --port <PORT>`.
    var psi = new ProcessStartInfo
    {
        FileName = "cloud-sql-proxy",
        Arguments = $"{instanceConnectionName} --port {localPort}",
        UseShellExecute = false,
    };
    try
    {
        var p = Process.Start(psi) ?? throw new InvalidOperationException("proxy did not start");
        // Give the proxy a moment to open the local listener before we connect.
        Thread.Sleep(3000);
        return p;
    }
    catch (System.ComponentModel.Win32Exception)
    {
        throw new InvalidOperationException(
            "'cloud-sql-proxy' not found on PATH. Install the Cloud SQL Auth Proxy v2 (see deploy/README.md).");
    }
}

static void Repl(NpgsqlConnection conn)
{
    while (true)
    {
        Console.Write("sql> ");
        var line = Console.ReadLine();
        if (line is null || line.Trim() is "\\q" or "quit" or "exit") break;
        var sql = line.Trim();
        if (sql.Length == 0) continue;

        try
        {
            if (IsReadOnly(sql))
                RunQuery(conn, sql);
            else
                RunWrite(conn, sql);
        }
        catch (PostgresException pg)
        {
            Console.WriteLine($"  SQL error: {pg.MessageText}");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"  error: {ex.Message}");
        }
    }
}

static bool IsReadOnly(string sql)
{
    var head = sql.TrimStart().Split([' ', '\t', '\n', '('], 2)[0].ToUpperInvariant();
    return head is "SELECT" or "WITH" or "SHOW" or "EXPLAIN" or "TABLE" or "VALUES";
}

static void RunQuery(NpgsqlConnection conn, string sql)
{
    using var cmd = new NpgsqlCommand(sql, conn);
    using var reader = cmd.ExecuteReader();

    var cols = Enumerable.Range(0, reader.FieldCount).Select(reader.GetName).ToArray();
    Console.WriteLine("  " + string.Join(" | ", cols));
    Console.WriteLine("  " + new string('-', Math.Max(3, cols.Sum(c => c.Length) + (cols.Length * 3))));

    var rows = 0;
    while (reader.Read())
    {
        var values = new string[reader.FieldCount];
        for (var i = 0; i < reader.FieldCount; i++)
            values[i] = reader.IsDBNull(i) ? "NULL" : reader.GetValue(i).ToString() ?? "";
        Console.WriteLine("  " + string.Join(" | ", values));
        rows++;
    }
    Console.WriteLine($"  ({rows} row{(rows == 1 ? "" : "s")})");
}

static void RunWrite(NpgsqlConnection conn, string sql)
{
    Console.WriteLine("  This statement MODIFIES data.");
    Console.Write("  Type YES to execute: ");
    if (!string.Equals(Console.ReadLine()?.Trim(), "YES", StringComparison.Ordinal))
    {
        Console.WriteLine("  Skipped.");
        return;
    }

    using var cmd = new NpgsqlCommand(sql, conn);
    var affected = cmd.ExecuteNonQuery();
    Console.WriteLine($"  OK ({affected} row{(affected == 1 ? "" : "s")} affected).");
}

// --- Config + args ---------------------------------------------------------------

sealed class Options
{
    public string Db { get; private set; } = "game";
    public string Environment { get; private set; } = "production";
    public string? SecretsPath { get; private set; }
    public int LocalPort { get; private set; } = 5433;
    public bool ShowHelp { get; private set; }

    public static Options Parse(string[] args)
    {
        var o = new Options();
        for (var i = 0; i < args.Length; i++)
        {
            switch (args[i])
            {
                case "--db": o.Db = Req(args, ref i); break;
                case "--env": o.Environment = Req(args, ref i); break;
                case "--secrets": o.SecretsPath = Req(args, ref i); break;
                case "--local-port": o.LocalPort = int.Parse(Req(args, ref i)); break;
                case "-h" or "--help": o.ShowHelp = true; break;
                default: throw new ArgumentException($"Unknown argument: {args[i]}");
            }
        }
        if (o.Db is not ("game" or "messaging"))
            throw new ArgumentException("--db must be 'game' or 'messaging'.");
        return o;
    }

    private static string Req(string[] args, ref int i)
    {
        if (i + 1 >= args.Length) throw new ArgumentException($"Missing value after {args[i]}.");
        return args[++i];
    }

    public static void PrintUsage() => Console.WriteLine(
        """
        chimpout-db — read-write DB REPL over the Cloud SQL Auth Proxy

        Usage: chimpout-db --db game|messaging [--env production] [--secrets <path>] [--local-port 5433]

        Reads run immediately; INSERT/UPDATE/DELETE/DDL require typing YES.
        Prerequisites: cloud-sql-proxy (v2), authenticated gcloud on PATH.
        """);
}

sealed class Config
{
    public string ProjectId { get; init; } = "";
    public string Region { get; init; } = "";
    public string Database { get; init; } = "";
    public string Username { get; init; } = "";
    public string Password { get; init; } = "";

    public static Config Load(Options opts)
    {
        // Resolve secrets file: explicit path, else walk up for monkify-meta/deploy/secrets.<env>.json.
        var path = opts.SecretsPath ?? FindSecretsFile(opts.Environment)
            ?? throw new FileNotFoundException(
                $"Could not find secrets.{opts.Environment}.json. Pass --secrets <path>.");

        using var doc = JsonDocument.Parse(File.ReadAllText(path), new JsonDocumentOptions
        {
            CommentHandling = JsonCommentHandling.Skip,
            AllowTrailingCommas = true,
        });
        var root = doc.RootElement;
        var gcp = root.GetProperty("gcp");
        var secrets = root.GetProperty("secrets");

        var db = opts.Db == "game" ? "chimpout" : "chimpout_messaging";
        var connKey = opts.Db == "game" ? "ConnectionStrings:ChimpOut" : "ConnectionStrings:Messaging";
        var connString = secrets.GetProperty(connKey).GetString()
            ?? throw new InvalidOperationException($"secrets.{connKey} is empty.");

        var csb = new NpgsqlConnectionStringBuilder(connString);
        return new Config
        {
            ProjectId = gcp.GetProperty("projectId").GetString() ?? "",
            Region = gcp.TryGetProperty("region", out var r) ? r.GetString() ?? "us-central1" : "us-central1",
            Database = db,
            Username = csb.Username ?? "",
            Password = csb.Password ?? "",
        };
    }

    private static string? FindSecretsFile(string env)
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null)
        {
            var candidate = Path.Combine(dir.FullName, "monkify-meta", "deploy", $"secrets.{env}.json");
            if (File.Exists(candidate)) return candidate;
            dir = dir.Parent;
        }
        return null;
    }
}
