using System.Net;
using System.Runtime.InteropServices;
using NetRouteList.Core.Docs;
using NetRouteList.Core.Native;
using NetRouteList.Core.Models;
using NetRouteList.Core.Services;

// RouteDump：命令行自测 / 快照工具。
// 1) 校验 P/Invoke 结构体大小  2) 输出网卡与路由表  3) 输出诊断  4) 输出帮助章节清单

Console.OutputEncoding = System.Text.Encoding.UTF8;

// ---- 结构体大小校验（x64 布局错误会直接体现在这里） ----
int sRow = Marshal.SizeOf<MIB_IPFORWARD_ROW2>();
int sPrefix = Marshal.SizeOf<IP_ADDRESS_PREFIX>();
int sSock = Marshal.SizeOf<SOCKADDR_INET>();
int sIfRow = Marshal.SizeOf<MIB_IPINTERFACE_ROW>();
int sTableHdr = (int)Marshal.OffsetOf<MIB_IPFORWARD_TABLE2>(nameof(MIB_IPFORWARD_TABLE2.Table));
Console.WriteLine($"[结构体校验] MIB_IPFORWARD_ROW2={sRow}(期望104) IP_ADDRESS_PREFIX={sPrefix}(期望32) " +
                  $"SOCKADDR_INET={sSock}(期望28) MIB_IPINTERFACE_ROW={sIfRow}(期望168) 表头偏移={sTableHdr}(期望8)");
if (sRow != 104 || sPrefix != 32 || sSock != 28 || sIfRow != 168 || sTableHdr != 8)
{
    Console.WriteLine("!! 结构体布局与 Windows x64 不符，后续数据不可信 !!");
    return 1;
}

var adapters = new AdapterService().GetAdapters();
var routeService = new RouteService();

Console.WriteLine($"\n===== 网卡（{adapters.Count}）=====");
foreach (var a in adapters)
{
    Console.WriteLine($"if {a.IfIndex,-3} {a.Name,-22} [{a.Kind}] {a.StatusText}  接口跃点v4={a.InterfaceMetricV4}");
    foreach (var ip in a.Ipv4Addresses)
        Console.WriteLine($"        IP {ip.Cidr,-20} 来源={ip.Origin} APIPA={ip.IsApipa}");
    foreach (var g in a.GatewayV4)
        Console.WriteLine($"        网关 {g}");
}

var v4 = routeService.GetIpv4Routes(adapters);
Console.WriteLine($"\n===== IPv4 路由（{v4.Count} 条）=====");
Console.WriteLine($"{"目标",-18} {"掩码",-16} {"网关",-18} {"if",-4} {"metric",-7} {"协议",-16} 类型");
foreach (var r in v4)
    Console.WriteLine($"{r.Destination,-18} {r.MaskText,-16} {r.GatewayText,-18} {r.InterfaceIndex,-4} {(r.EffectiveMetric?.ToString() ?? r.Metric.ToString()),-7} {r.Protocol.DisplayName(),-16} {r.Origin.DisplayName()} {r.KindText}");

try
{
    var v6 = routeService.GetIpv6Routes(adapters);
    Console.WriteLine($"\n===== IPv6 路由（{v6.Count} 条，前 10）=====");
    foreach (var r in v6.Take(10))
        Console.WriteLine(RouteCommandText.PrintStyle(r));
}
catch (Exception ex) { Console.WriteLine($"IPv6 读取失败：{ex.Message}"); }

Console.WriteLine("\n===== 诊断 =====");
var report = new DiagnosticsService(routeService).Analyze(adapters, v4);
Console.WriteLine($"网卡 {report.AdapterCount} | IPv4 路由 {report.RouteCountV4} | 默认路由 {report.DefaultRouteCount} | 错误 {report.ErrorCount} | 警告 {report.WarningCount}");
foreach (var issue in report.Issues)
{
    Console.WriteLine($"[{issue.Severity}] {issue.Title}");
    foreach (var line in issue.Affected) Console.WriteLine($"    · {line}");
}

Console.WriteLine($"\n===== 帮助文档：{HelpDocs.Sections.Count} 章节，{HelpDocs.FieldTips.Count} 字段提示 =====");
Console.WriteLine($"备份目录：{BackupService.BackupDirectory}");
Console.WriteLine("自测完成。");
return 0;
