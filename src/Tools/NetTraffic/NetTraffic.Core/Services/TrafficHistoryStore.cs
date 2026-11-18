using Toolkit.Shell.Storage;

namespace NetTraffic.Core.Services;

/// <summary>一个时间范围内的进程流量汇总行。</summary>
public sealed class TrafficHistorySummary
{
    public int Pid { get; init; }
    public string Name { get; init; } = "";
    public long UpBytes { get; init; }
    public long DownBytes { get; init; }
    public long TotalBytes => UpBytes + DownBytes;
    /// <summary>活跃采样周期数（该进程有流量的 10s 周期数）。</summary>
    public long ActivePeriods { get; init; }
    public DateTime FirstSeen { get; init; }
    public DateTime LastSeen { get; init; }
}

/// <summary>
/// 网络流量历史存储（net_traffic 表）：监控运行期间每 10 秒写一次各进程的周期增量，
/// 用于事后分析"这段时间哪个进程消耗了多少流量"。保留 14 天，启动监控时自动清理。
/// </summary>
public static class TrafficHistoryStore
{
    /// <summary>落库快照间隔（秒）。5 秒对"哪个进程耗流量 + 突发毛刺"都有足够分辨率；
    /// 日行数量级约 50 进程 × 17280 点，SQLite 无压力。</summary>
    public const int SnapshotSeconds = 5;

    private const int KeepDays = 14;

    private static bool _tableReady;

    private static void EnsureTable()
    {
        if (_tableReady) return;
        SqliteStore.Initialize();
        SqliteStore.Execute("""
            CREATE TABLE IF NOT EXISTS net_traffic (
                id    INTEGER PRIMARY KEY AUTOINCREMENT,
                ts    INTEGER NOT NULL,           -- Unix 毫秒
                pid   INTEGER NOT NULL,
                name  TEXT    NOT NULL,
                up    INTEGER NOT NULL,           -- 周期内上行字节
                down  INTEGER NOT NULL            -- 周期内下行字节
            );
            """);
        SqliteStore.Execute("CREATE INDEX IF NOT EXISTS idx_net_traffic_ts ON net_traffic(ts);");
        SqliteStore.Execute("CREATE INDEX IF NOT EXISTS idx_net_traffic_pid ON net_traffic(pid);");
        _tableReady = true;
    }

    /// <summary>批量写入一个周期的增量快照（rows 为空则跳过）。</summary>
    public static void WriteSnapshots(long unixMs, IEnumerable<(int Pid, string Name, long Up, long Down)> rows)
    {
        var batch = rows
            .Where(r => r.Up > 0 || r.Down > 0)
            .Select(r => ((string)"INSERT INTO net_traffic(ts,pid,name,up,down) VALUES(@ts,@pid,@name,@up,@down)",
                new (string, object?)[] { ("@ts", unixMs), ("@pid", r.Pid), ("@name", r.Name), ("@up", r.Up), ("@down", r.Down) }))
            .ToList();
        if (batch.Count == 0) return;
        EnsureTable();
        SqliteStore.ExecuteBatch(batch);
    }

    /// <summary>按时间范围汇总各进程流量（按总字节降序）。</summary>
    public static List<TrafficHistorySummary> QuerySummary(DateTime from, DateTime to)
    {
        EnsureTable();
        var rows = SqliteStore.Query("""
            SELECT pid, name, SUM(up) AS up, SUM(down) AS down, COUNT(*) AS periods,
                   MIN(ts) AS first_ts, MAX(ts) AS last_ts
            FROM net_traffic
            WHERE ts >= @from AND ts < @to
            GROUP BY pid, name
            ORDER BY (SUM(up) + SUM(down)) DESC;
            """,
            ("@from", new DateTimeOffset(from).ToUnixTimeMilliseconds()),
            ("@to", new DateTimeOffset(to).ToUnixTimeMilliseconds()));
        return rows.Select(r => new TrafficHistorySummary
        {
            Pid = Convert.ToInt32(r["pid"]),
            Name = Convert.ToString(r["name"]) ?? "",
            UpBytes = Convert.ToInt64(r["up"]),
            DownBytes = Convert.ToInt64(r["down"]),
            ActivePeriods = Convert.ToInt64(r["periods"]),
            FirstSeen = DateTimeOffset.FromUnixTimeMilliseconds(Convert.ToInt64(r["first_ts"])).LocalDateTime,
            LastSeen = DateTimeOffset.FromUnixTimeMilliseconds(Convert.ToInt64(r["last_ts"])).LocalDateTime,
        }).ToList();
    }

    /// <summary>查询单进程在一个时间范围内的逐采样点序列（画折线用，按时间升序）。</summary>
    public static List<(DateTime Ts, long Down, long Up)> QuerySeries(int pid, DateTime from, DateTime to)
    {
        EnsureTable();
        var rows = SqliteStore.Query("""
            SELECT ts, down, up FROM net_traffic
            WHERE pid = @pid AND ts >= @from AND ts < @to
            ORDER BY ts;
            """,
            ("@pid", pid),
            ("@from", new DateTimeOffset(from).ToUnixTimeMilliseconds()),
            ("@to", new DateTimeOffset(to).ToUnixTimeMilliseconds()));
        return rows.Select(r => (
            DateTimeOffset.FromUnixTimeMilliseconds(Convert.ToInt64(r["ts"])).LocalDateTime,
            Convert.ToInt64(r["down"]),
            Convert.ToInt64(r["up"]))).ToList();
    }

    /// <summary>总行数（诊断用）。</summary>
    public static long RowCount()
    {
        EnsureTable();
        return SqliteStore.QueryScalar("SELECT COUNT(*) FROM net_traffic;", 0);
    }

    /// <summary>清理过期数据（保留 KeepDays 天；监控启动时调用）。</summary>
    public static void Prune()
    {
        EnsureTable();
        var cutoff = new DateTimeOffset(DateTime.Now.AddDays(-KeepDays)).ToUnixTimeMilliseconds();
        SqliteStore.Execute("DELETE FROM net_traffic WHERE ts < @cutoff;", ("@cutoff", cutoff));
    }
}
