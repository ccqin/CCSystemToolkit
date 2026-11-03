using System.Net;
using System.Net.Sockets;

namespace NetRouteList.Core.Models;

/// <summary>一次“添加 / 修改路由”的请求参数。</summary>
public sealed class RouteMutation
{
    /// <summary>目标网络地址（主机部分会被自动归零，例如 192.168.1.5/24 → 192.168.1.0/24）。</summary>
    public required IPAddress Destination { get; init; }

    public required int PrefixLength { get; init; }

    /// <summary>下一跳网关；null = on-link 直连路由。</summary>
    public IPAddress? NextHop { get; init; }

    public required uint InterfaceIndex { get; init; }

    /// <summary>路由跃点（最终生效 = 接口跃点 + 该值，越小越优先）。</summary>
    public uint Metric { get; init; }

    /// <summary>true = 持久路由（route add -p，写入注册表，重启保留）。</summary>
    public bool Persistent { get; init; }

    public AddressFamily Family => Destination.AddressFamily;

    /// <summary>目标网络（主机位归零后的网络地址）。</summary>
    public IPAddress Network => IpAddressInfo.ComputeNetwork(Destination, PrefixLength);
}

/// <summary>一次路由操作的结果。</summary>
public sealed class RouteOpResult
{
    public required bool Success { get; init; }
    /// <summary>给人看的中文结果说明（含失败原因）。</summary>
    public required string Message { get; init; }
    /// <summary>等效的命令行命令（教学/手工执行用）。</summary>
    public string? EquivalentCommand { get; init; }
    public int Win32Error { get; init; }

    public static RouteOpResult Ok(string message, string? cmd = null) => new() { Success = true, Message = message, EquivalentCommand = cmd };
    public static RouteOpResult Fail(string message, int error = 0, string? cmd = null) => new() { Success = false, Message = message, Win32Error = error, EquivalentCommand = cmd };
}
