using System.IO;
using Microsoft.Data.Sqlite;

namespace Toolkit.Shell.Storage;

/// <summary>
/// 统一 SQLite 存储（进程级单份）：%LOCALAPPDATA%\SystemToolkit\toolkit.db（WAL 模式）。
/// 各工具自建表（CREATE TABLE IF NOT EXISTS）后读写；全局锁串行执行——写入均为低频批量场景。
/// 首次访问自动初始化；库损坏时重命名 .bad.bak 后重建（与 json 存储同一防灾约定）。
/// </summary>
public static class SqliteStore
{
    private static readonly object _sync = new();
    private static SqliteConnection? _conn;
    private static bool _broken;

    private static string BaseDir =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "SystemToolkit");

    public static string DbPath => Path.Combine(BaseDir, "toolkit.db");

    /// <summary>初始化（幂等）：建目录、连接、WAL、外键。失败重命名坏库重建。</summary>
    public static void Initialize()
    {
        lock (_sync)
        {
            if (_conn is not null || _broken) return;
            try
            {
                Directory.CreateDirectory(BaseDir);
                var conn = new SqliteConnection($"Data Source={DbPath};Cache=Shared");
                conn.Open();
                using (var pragma = new SqliteCommand("PRAGMA journal_mode=WALL; PRAGMA synchronous=NORMAL;", conn))
                {
                    pragma.ExecuteNonQuery();
                }
                _conn = conn;
            }
            catch
            {
                // 库文件损坏等：留底重建（数据可弃——均为可再生的监控/日志数据）
                try { _conn?.Dispose(); } catch { }
                _conn = null;
                try
                {
                    if (File.Exists(DbPath))
                    {
                        File.Move(DbPath, DbPath + ".bad.bak", overwrite: true);
                        var conn = new SqliteConnection($"Data Source={DbPath}");
                        conn.Open();
                        using (var pragma = new SqliteCommand("PRAGMA journal_mode=WALL;", conn))
                        {
                            pragma.ExecuteNonQuery();
                        }
                        _conn = conn;
                    }
                }
                catch
                {
                    _broken = true;   // 彻底不可用：静默降级为无持久化（不影响功能运行）
                }
            }
        }
    }

    /// <summary>参数化执行（INSERT/UPDATE/DELETE/DDL），返回受影响行数。args 为 (@名, 值) 列表。</summary>
    public static int Execute(string sql, params (string Name, object? Value)[] args)
    {
        lock (_sync)
        {
            if (!EnsureConn()) return 0;
            using var cmd = new SqliteCommand(sql, _conn);
            foreach (var (name, value) in args)
                cmd.Parameters.AddWithValue(name, value ?? DBNull.Value);
            try { return cmd.ExecuteNonQuery(); }
            catch { return 0; }
        }
    }

    /// <summary>批量执行多条参数化语句（一个隐式事务，减少 fsync）。</summary>
    public static void ExecuteBatch(IEnumerable<(string Sql, (string Name, object? Value)[] Args)> items)
    {
        lock (_sync)
        {
            if (!EnsureConn()) return;
            using var tx = _conn!.BeginTransaction();
            foreach (var (sql, args) in items)
            {
                using var cmd = new SqliteCommand(sql, _conn, tx);
                foreach (var (name, value) in args)
                    cmd.Parameters.AddWithValue(name, value ?? DBNull.Value);
                try { cmd.ExecuteNonQuery(); } catch { /* 单条失败跳过 */ }
            }
            try { tx.Commit(); } catch { }
        }
    }

    /// <summary>
    /// 查询：每行返回一个按列名的字典（值可能是 long/double/string/byte[]/null）。
    /// </summary>
    public static List<Dictionary<string, object?>> Query(string sql, params (string Name, object? Value)[] args)
    {
        var result = new List<Dictionary<string, object?>>();
        lock (_sync)
        {
            if (!EnsureConn()) return result;
            using var cmd = new SqliteCommand(sql, _conn);
            foreach (var (name, value) in args)
                cmd.Parameters.AddWithValue(name, value ?? DBNull.Value);
            try
            {
                using var reader = cmd.ExecuteReader();
                while (reader.Read())
                {
                    var row = new Dictionary<string, object?>(reader.FieldCount);
                    for (var i = 0; i < reader.FieldCount; i++)
                        row[reader.GetName(i)] = reader.IsDBNull(i) ? null : reader.GetValue(i);
                    result.Add(row);
                }
            }
            catch { /* 查询失败返回空 */ }
        }
        return result;
    }

    /// <summary>标量查询（COUNT/MAX 等），失败返回 fallback。</summary>
    public static long QueryScalar(string sql, long fallback, params (string Name, object? Value)[] args)
    {
        var rows = Query(sql, args);
        if (rows.Count == 0 || rows[0].Count == 0) return fallback;
        return rows[0].Values.First() switch
        {
            long l => l,
            int i => i,
            double d => (long)d,
            _ => fallback,
        };
    }

    private static bool EnsureConn()
    {
        if (_conn is not null) return true;
        if (_broken) return false;
        Initialize();
        return _conn is not null;
    }
}
