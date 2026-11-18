using Toolkit.Shell;
using Toolkit.Shell.Storage;

namespace Toolkit.App.Services;

/// <summary>
/// 统一日志持久化（logs 表）：MainViewModel.Log 双写内存 + SQLite，保留 30 天。
/// 用于事后排查（比如"昨天下午网络监控为什么停了"）。
/// </summary>
public static class LogHistoryStore
{
    private const int KeepDays = 30;
    private static bool _tableReady;
    private static DateTime _lastPrune = DateTime.MinValue;

    public static void Write(string source, string level, string message)
    {
        try
        {
            EnsureTable();
            SqliteStore.Execute(
                "INSERT INTO logs(ts, source, level, message) VALUES(@ts, @source, @level, @message);",
                ("@ts", new DateTimeOffset(DateTime.Now).ToUnixTimeMilliseconds()),
                ("@source", source),
                ("@level", level),
                ("@message", message.Length > 4000 ? message[..4000] : message));
            // 每天清一次过期日志（顺手，不额外起定时器）
            if ((DateTime.Now - _lastPrune).TotalHours > 24)
            {
                _lastPrune = DateTime.Now;
                var cutoff = new DateTimeOffset(DateTime.Now.AddDays(-KeepDays)).ToUnixTimeMilliseconds();
                SqliteStore.Execute("DELETE FROM logs WHERE ts < @cutoff;", ("@cutoff", cutoff));
            }
        }
        catch
        { /* 落库失败不影响日志的内存展示 */ }
    }

    private static void EnsureTable()
    {
        if (_tableReady) return;
        SqliteStore.Initialize();
        SqliteStore.Execute("""
            CREATE TABLE IF NOT EXISTS logs (
                id      INTEGER PRIMARY KEY AUTOINCREMENT,
                ts      INTEGER NOT NULL,
                source  TEXT    NOT NULL,
                level   TEXT    NOT NULL,
                message TEXT    NOT NULL
            );
            """);
        SqliteStore.Execute("CREATE INDEX IF NOT EXISTS idx_logs_ts ON logs(ts);");
        _tableReady = true;
    }
}
