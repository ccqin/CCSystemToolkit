using System.IO;
using System.Net;
using System.Text.Json;
using System.Text.Json.Serialization;
using NetRouteList.Core.Models;

namespace NetRouteList.Core.Services;

/// <summary>
/// 路由表备份/恢复服务。
/// 备份为 JSON 文件，存于 %LOCALAPPDATA%\NetRouteList\backups。
/// 恢复支持“增量合并”与“完全恢复”两种模式，恢复前可自动创建安全快照。
/// </summary>
[System.Runtime.Versioning.SupportedOSPlatform("windows")]
public sealed class BackupService
{
    public static string BackupDirectory { get; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "NetRouteList", "backups");

    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        WriteIndented = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    private readonly RouteService _routes;
    private readonly AdapterService _adapters;

    public BackupService(RouteService routes, AdapterService adapters)
    {
        _routes = routes;
        _adapters = adapters;
    }

    // ---------- 创建备份 ----------

    /// <summary>立即抓取当前路由表与网卡信息，保存为一个备份。返回保存路径（失败抛异常）。</summary>
    public BackupSnapshot CreateBackup(string name, string note)
    {
        var adapters = _adapters.GetAdapters();
        var v4 = _routes.GetIpv4Routes(adapters);
        var v6 = _routes.GetIpv6Routes(adapters);

        var snapshot = new BackupSnapshot
        {
            Name = string.IsNullOrWhiteSpace(name) ? $"备份 {DateTime.Now:yyyy-MM-dd HH:mm:ss}" : name.Trim(),
            Note = note?.Trim() ?? "",
            MachineName = Environment.MachineName,
            CreatedAtLocal = DateTime.Now,
            Adapters = adapters.Select(a => new BackupAdapterInfo
            {
                IfIndex = a.IfIndex ?? 0,
                Name = a.Name,
                Description = a.Description,
                IsUp = a.IsUp,
                Ipv4 = a.Ipv4Addresses.Select(x => x.Cidr).ToList(),
                GatewayV4 = a.GatewayV4.Select(g => g.ToString()).ToList(),
            }).ToList(),
            Routes = v4.Select(ToBackupRoute).Concat(v6.Select(ToBackupRoute)).ToList(),
        };

        Directory.CreateDirectory(BackupDirectory);
        string file = Path.Combine(BackupDirectory,
            $"backup-{DateTime.Now:yyyyMMdd-HHmmss-fff}.json");
        File.WriteAllText(file, JsonSerializer.Serialize(snapshot, JsonOpts));
        snapshot.FilePath = file;
        return snapshot;
    }

    private static BackupRouteEntry ToBackupRoute(RouteEntry r) => new()
    {
        Family = r.IsIpv4 ? "IPv4" : "IPv6",
        Destination = r.Destination.ToString(),
        PrefixLength = r.PrefixLength,
        NextHop = r.IsOnLink ? null : r.NextHop?.ToString(),
        InterfaceIndex = r.InterfaceIndex,
        InterfaceName = r.InterfaceName ?? "",
        Metric = r.Metric,
        Protocol = (int)r.Protocol,
        Immortal = r.IsImmortal,
        Loopback = r.IsLoopback,
    };

    // ---------- 备份列表 ----------

    public List<BackupSnapshot> ListBackups()
    {
        if (!Directory.Exists(BackupDirectory)) return [];
        var list = new List<BackupSnapshot>();
        foreach (var file in Directory.GetFiles(BackupDirectory, "*.json"))
        {
            try
            {
                var snap = JsonSerializer.Deserialize<BackupSnapshot>(File.ReadAllText(file));
                if (snap is not null && snap.FormatVersion == BackupSnapshot.CurrentFormatVersion)
                {
                    snap.FilePath = file;
                    list.Add(snap);
                }
            }
            catch { /* 损坏的文件跳过 */ }
        }
        return list.OrderByDescending(s => s.CreatedAtLocal).ToList();
    }

    public void Delete(BackupSnapshot snap)
    {
        if (snap.FilePath is not null && File.Exists(snap.FilePath))
            File.Delete(snap.FilePath);
    }

    /// <summary>导出备份到任意路径（分享/归档用）。</summary>
    public void Export(BackupSnapshot snap, string targetPath) =>
        File.Copy(snap.FilePath!, targetPath, overwrite: true);

    /// <summary>从任意 JSON 文件导入一个备份。</summary>
    public BackupSnapshot Import(string sourcePath)
    {
        var snap = JsonSerializer.Deserialize<BackupSnapshot>(File.ReadAllText(sourcePath))
            ?? throw new InvalidDataException("文件不是有效的路由表备份。");
        if (snap.FormatVersion != BackupSnapshot.CurrentFormatVersion)
            throw new InvalidDataException($"备份格式版本({snap.FormatVersion})不受支持。");
        Directory.CreateDirectory(BackupDirectory);
        string file = Path.Combine(BackupDirectory,
            $"imported-{DateTime.Now:yyyyMMdd-HHmmss-fff}{Path.GetFileName(sourcePath)}");
        File.Copy(sourcePath, file, overwrite: true);
        snap.FilePath = file;
        if (string.IsNullOrWhiteSpace(snap.Name)) snap.Name = Path.GetFileNameWithoutExtension(sourcePath);
        return snap;
    }

    // ---------- 差异对比 ----------

    /// <summary>把备份与当前路由表对比，返回三组差异。</summary>
    public BackupDiff Diff(BackupSnapshot snap, bool includeIpv6 = false)
    {
        var adapters = _adapters.GetAdapters();
        var current = _routes.GetIpv4Routes(adapters)
            .Concat(includeIpv6 ? _routes.GetIpv6Routes(adapters) : [])
            .ToList();

        // 当前表按“内容键”（不含 ifIndex，因为恢复时会重映射接口）：前缀|网关
        static string ContentKey(string dest, int prefix, string? nh) =>
            $"{dest}/{prefix}|{nh ?? "onlink"}";

        var currentByKey = current.GroupBy(r => ContentKey(r.Destination.ToString(), r.PrefixLength,
                r.IsOnLink ? null : r.NextHop!.ToString()))
            .ToDictionary(g => g.Key, g => g.ToList());

        var backupRoutes = snap.Routes.Where(r => includeIpv6 || r.Family == "IPv4").ToList();

        var onlyInBackup = new List<BackupRouteEntry>();
        var different = new List<(BackupRouteEntry, RouteEntry)>();
        var matchedCurrent = new HashSet<RouteEntry>(ReferenceEqualityComparer.Instance);

        foreach (var b in backupRoutes)
        {
            string key = ContentKey(b.Destination, b.PrefixLength, b.NextHop);
            if (!currentByKey.TryGetValue(key, out var currents))
            {
                onlyInBackup.Add(b);
                continue;
            }
            // 同键存在：比较 metric（接口不同导致的 metric 差异不算本质差异）
            var cur = currents.FirstOrDefault(c => c.Metric == b.Metric);
            if (cur is null)
            {
                different.Add((b, currents[0]));
                matchedCurrent.Add(currents[0]);
            }
            else
            {
                matchedCurrent.Add(cur);
            }
        }

        var onlyInCurrent = current.Where(c => !matchedCurrent.Contains(c)).ToList();
        return new BackupDiff { OnlyInBackup = onlyInBackup, OnlyInCurrent = onlyInCurrent, Different = different };
    }

    // ---------- 恢复 ----------

    /// <summary>应用一个备份到当前系统。恢复是逐条执行的，任何一条失败不影响其它条目。</summary>
    public RestoreReport Restore(BackupSnapshot snap, RestoreOptions options)
    {
        var results = new List<RouteRestoreResult>();
        string? safetyPath = null;

        if (options.SafetySnapshot)
        {
            var safety = CreateBackup($"恢复前自动快照（恢复 “{snap.Name}”）", "系统在执行恢复操作前自动创建，用于撤销本次恢复。");
            safetyPath = safety.FilePath;
        }

        var adapters = _adapters.GetAdapters();
        var byIndex = adapters.Where(a => a.IfIndex is not null).ToDictionary(a => a.IfIndex!.Value, a => a.Name);
        var byName = new Dictionary<string, uint>(StringComparer.OrdinalIgnoreCase);
        foreach (var a in adapters.Where(a => a.IfIndex is not null))
            byName[a.Name] = a.IfIndex!.Value;

        var current = _routes.GetIpv4Routes(adapters)
            .Concat(options.IncludeIpv6 ? _routes.GetIpv6Routes(adapters) : [])
            .ToList();

        static string ContentKey(RouteEntry r) => $"{r.Destination}/{r.PrefixLength}|{(r.IsOnLink ? "onlink" : r.NextHop!.ToString())}";
        static string BackupKey(BackupRouteEntry b) => $"{b.Destination}/{b.PrefixLength}|{b.NextHop ?? "onlink"}";

        // 1. 添加备份中缺失的路由（两种模式都做）
        foreach (var b in snap.Routes)
        {
            if (b.Family == "IPv6" && !options.IncludeIpv6)
            { results.Add(Skip(b, "IPv6 路由（当前未启用 IPv6 恢复）")); continue; }
            if (options.SkipSystemRoutes && IsSystemRoute(b))
            { results.Add(Skip(b, "系统关键路由（环回/多播/广播/链路本地），由系统自行维护")); continue; }

            // 接口重映射：索引对应的名字一致就直接用，否则按网卡名找
            uint ifIndex = b.InterfaceIndex;
            string reason;
            if (byIndex.TryGetValue(ifIndex, out var curName) &&
                string.Equals(curName, b.InterfaceName, StringComparison.OrdinalIgnoreCase))
            {
                reason = "";
            }
            else if (byName.TryGetValue(b.InterfaceName ?? "", out var remap))
            {
                ifIndex = remap;
                reason = $"接口索引已从 {b.InterfaceIndex} 重映射到 {remap}（按网卡名称 “{b.InterfaceName}” 匹配）";
            }
            else
            {
                results.Add(Skip(b, $"网卡 “{b.InterfaceName ?? "?"}”（if {b.InterfaceIndex}）当前不存在或已改名，无法恢复"));
                continue;
            }

            var key = BackupKey(b);
            var sameKey = current.FirstOrDefault(c => ContentKey(c) == key);
            if (sameKey is not null)
            {
                if (sameKey.Metric == b.Metric)
                { results.Add(new RouteRestoreResult { Action = RouteRestoreAction.Same, Route = b.Display, Message = "当前系统中已存在相同条目" }); continue; }

                // metric 不同 → 更新
                var upd = new RouteMutation
                {
                    Destination = IPAddress.Parse(b.Destination),
                    PrefixLength = b.PrefixLength,
                    NextHop = b.NextHop is null ? null : IPAddress.Parse(b.NextHop),
                    InterfaceIndex = ifIndex,
                    Metric = b.Metric,
                    Persistent = b.Immortal,
                };
                var op = _routes.UpdateRoute(sameKey, upd);
                bool ok = op.Count > 0 && op[^1].Success;
                results.Add(new RouteRestoreResult
                {
                    Action = ok ? RouteRestoreAction.Updated : RouteRestoreAction.Failed,
                    Route = b.Display,
                    Message = (ok ? "已更新（跃点不同）" : "更新失败：") + op[^1].Message,
                    EquivalentCommand = op[^1].EquivalentCommand,
                });
                continue;
            }

            var mutation = new RouteMutation
            {
                Destination = IPAddress.Parse(b.Destination),
                PrefixLength = b.PrefixLength,
                NextHop = b.NextHop is null ? null : IPAddress.Parse(b.NextHop),
                InterfaceIndex = ifIndex,
                Metric = b.Metric,
                Persistent = b.Immortal,
            };
            var add = _routes.AddRoute(mutation);
            results.Add(new RouteRestoreResult
            {
                Action = add.Success ? RouteRestoreAction.Added : RouteRestoreAction.Failed,
                Route = b.Display,
                Message = (add.Success ? "" : "添加失败：") + add.Message + (add.Success && reason != "" ? $"；{reason}" : ""),
                EquivalentCommand = add.EquivalentCommand,
            });
        }

        // 2. 完全恢复：删除当前表中、备份里没有的可管理路由
        if (options.Mode == RestoreMode.Exact)
        {
            var backupKeys = snap.Routes
                .Where(b => options.IncludeIpv6 || b.Family == "IPv4")
                .Where(b => !options.SkipSystemRoutes || !IsSystemRoute(b))
                .Select(BackupKey)
                .ToHashSet();

            foreach (var c in current)
            {
                if (c.IsLoopback) { results.Add(Skip2(c, "环回系统路由，不参与比对")); continue; }
                if (options.SkipSystemRoutes && IsSystemRoute(ToBackupRoute(c))) { continue; }
                if (c.Protocol is RouteProtocol.Local or RouteProtocol.Dhcp or RouteProtocol.Icmp)
                { results.Add(Skip2(c, "系统/DHCP 自动生成的路由，删除后会由系统重建，保留")); continue; }
                if (backupKeys.Contains(ContentKey(c))) continue;

                var del = _routes.DeleteRoute(c);
                results.Add(new RouteRestoreResult
                {
                    Action = del.Success ? RouteRestoreAction.Deleted : RouteRestoreAction.Failed,
                    Route = c.ToString(),
                    Message = del.Success ? "已删除（完全恢复：备份中不存在此路由）" : "删除失败：" + del.Message,
                    EquivalentCommand = del.EquivalentCommand,
                });
            }
        }

        return new RestoreReport
        {
            BackupName = snap.Name,
            Options = options,
            SafetySnapshotPath = safetyPath,
            Results = results,
        };

        static RouteRestoreResult Skip(BackupRouteEntry b, string why) => new()
        { Action = RouteRestoreAction.Skipped, Route = b.Display, Message = why };
        static RouteRestoreResult Skip2(RouteEntry c, string why) => new()
        { Action = RouteRestoreAction.Skipped, Route = c.ToString(), Message = why };
    }

    /// <summary>系统关键路由：环回、受限广播、多播、链路本地。删除/覆盖它们极易破坏系统网络。</summary>
    public static bool IsSystemRoute(BackupRouteEntry r)
    {
        if (r.Family == "IPv4")
        {
            return (r.Destination, r.PrefixLength) switch
            {
                ("127.0.0.0", 8) => true,
                ("224.0.0.0", 4) => true,
                ("255.255.255.255", 32) => true,
                ("169.254.0.0", 16) => true,
                _ => false,
            };
        }
        return (r.Destination, r.PrefixLength) switch
        {
            ("::1", 128) => true,
            ("ff00::", 8) => true,
            ("fe80::", 10) => true,
            _ => false,
        };
    }
}
