using System.Text.Json.Serialization;

namespace NetRouteList.Core.Models;

/// <summary>
/// 路由表备份的 JSON 模型。存储于 %LOCALAPPDATA%\NetRouteList\backups\*.json。
/// 除路由外还记录网卡快照，便于恢复时按“网卡名称”重新映射接口索引。
/// </summary>
public sealed class BackupSnapshot
{
    public const int CurrentFormatVersion = 1;

    public int FormatVersion { get; set; } = CurrentFormatVersion;

    /// <summary>备份显示名（用户可编辑）。</summary>
    public string Name { get; set; } = "";

    /// <summary>备注。</summary>
    public string Note { get; set; } = "";

    public string MachineName { get; set; } = "";

    public DateTime CreatedAtLocal { get; set; }

    /// <summary>备份文件完整路径（运行时填充，不序列化到内容里）。</summary>
    [JsonIgnore]
    public string? FilePath { get; set; }

    /// <summary>网卡快照（恢复时用于按名称重映射 ifIndex）。</summary>
    public List<BackupAdapterInfo> Adapters { get; set; } = [];

    /// <summary>路由快照。</summary>
    public List<BackupRouteEntry> Routes { get; set; } = [];

    [JsonIgnore]
    public string Ipv4RouteCount => Routes.Count(r => r.Family == "IPv4").ToString();
    [JsonIgnore]
    public string Ipv6RouteCount => Routes.Count(r => r.Family == "IPv6").ToString();
}

public sealed class BackupAdapterInfo
{
    public uint IfIndex { get; set; }
    public string Name { get; set; } = "";
    public string Description { get; set; } = "";
    public bool IsUp { get; set; }
    public List<string> Ipv4 { get; set; } = [];
    public List<string> GatewayV4 { get; set; } = [];
}

public sealed class BackupRouteEntry
{
    public string Family { get; set; } = "IPv4";   // "IPv4" / "IPv6"
    public string Destination { get; set; } = "";  // 网络地址（主机位已归零）
    public int PrefixLength { get; set; }
    public string? NextHop { get; set; }           // null = on-link
    public uint InterfaceIndex { get; set; }
    public string InterfaceName { get; set; } = "";// 备份时的接口名，恢复时按名重映射
    public uint Metric { get; set; }
    public int Protocol { get; set; }
    public bool Immortal { get; set; }
    public bool Loopback { get; set; }

    public string Key => $"{Destination}/{PrefixLength}|{NextHop ?? "onlink"}|{InterfaceName}|{InterfaceIndex}";

    public string Display => $"{Destination}/{PrefixLength} → {(NextHop ?? "on-link")} via {InterfaceName ?? "?"} (if {InterfaceIndex}, metric {Metric})";
}
