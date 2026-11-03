using NetRouteList.Core.Models;

namespace NetRouteList.Core.Services;

public enum RestoreMode
{
    /// <summary>增量合并：只添加备份中存在、当前缺失的路由，不动现有路由。</summary>
    Merge,
    /// <summary>完全恢复：让路由表与备份完全一致 —— 补缺失、删多余、改差异。</summary>
    Exact,
}

public sealed class RestoreOptions
{
    public RestoreMode Mode { get; init; } = RestoreMode.Merge;
    /// <summary>是否连同 IPv6 路由一起恢复（默认只恢复 IPv4）。</summary>
    public bool IncludeIpv6 { get; init; }
    /// <summary>跳过系统关键路由：环回(127/8)、多播(224/4、ff00::/8)、广播(255.255.255.255)、链路本地(169.254/16、fe80::/10)。</summary>
    public bool SkipSystemRoutes { get; init; } = true;
    /// <summary>恢复前是否自动创建当前状态快照（强烈建议保持开启）。</summary>
    public bool SafetySnapshot { get; init; } = true;
}

public enum RouteRestoreAction { Added, Deleted, Updated, Skipped, Failed, Same }

public sealed class RouteRestoreResult
{
    public required RouteRestoreAction Action { get; init; }
    /// <summary>路由的人类可读描述。</summary>
    public required string Route { get; init; }
    public string Message { get; init; } = "";
    public string? EquivalentCommand { get; init; }
}

public sealed class RestoreReport
{
    public required string BackupName { get; init; }
    public required RestoreOptions Options { get; init; }
    public string? SafetySnapshotPath { get; init; }
    public required List<RouteRestoreResult> Results { get; init; }

    public int Added => Results.Count(r => r.Action == RouteRestoreAction.Added);
    public int Deleted => Results.Count(r => r.Action == RouteRestoreAction.Deleted);
    public int Updated => Results.Count(r => r.Action == RouteRestoreAction.Updated);
    public int Skipped => Results.Count(r => r.Action == RouteRestoreAction.Skipped);
    public int Failed => Results.Count(r => r.Action == RouteRestoreAction.Failed);
    public int Same => Results.Count(r => r.Action == RouteRestoreAction.Same);
}

/// <summary>备份与当前路由表的差异。</summary>
public sealed class BackupDiff
{
    /// <summary>备份里有、当前没有（恢复时会添加）。</summary>
    public required List<BackupRouteEntry> OnlyInBackup { get; init; }
    /// <summary>当前有、备份里没有（完全恢复时会删除）。</summary>
    public required List<RouteEntry> OnlyInCurrent { get; init; }
    /// <summary>两边都有但 metric 等不一致（完全恢复时会更新）。</summary>
    public required List<(BackupRouteEntry Backup, RouteEntry Current)> Different { get; init; }
}
