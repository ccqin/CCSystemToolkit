using System.Diagnostics;
using Microsoft.Win32;
using StartupMgr.Core.Models;

namespace StartupMgr.Core.Services;

/// <summary>
/// 服务扫描器：HKLM\SYSTEM\CurrentControlSet\Services 下的用户态服务（Type 含 0x10/0x20）。
/// 支持修改启动类型（Start 2自动/3手动/4禁用）——写 HKLM 需要管理员。
/// </summary>
public sealed class ServiceScanner
{
    private const string ServicesRoot = @"SYSTEM\CurrentControlSet\Services";

    public List<ServiceEntry> ScanAll(Action<string>? progress = null)
    {
        var entries = new List<ServiceEntry>();
        progress?.Invoke("扫描服务…");
        using var root = Registry.LocalMachine.OpenSubKey(ServicesRoot);
        if (root is null) return entries;

        foreach (var name in root.GetSubKeyNames())
        {
            try
            {
                using var k = root.OpenSubKey(name);
                if (k is null) continue;

                var type = k.GetValue("Type") as int? ?? 0;
                // 只保留用户态服务（0x10 自有进程 / 0x20 共享进程 / 0x100 交互）
                if ((type & 0x30) == 0) continue;

                var start = k.GetValue("Start") as int? ?? 3;
                if (start is < 0 or > 4) continue;
                var delayed = (k.GetValue("DelayedAutostart") as int? ?? 0) == 1;

                var imagePath = k.GetValue("ImagePath", null, RegistryValueOptions.DoNotExpandEnvironmentNames) as string
                                ?? k.GetValue("ImagePath") as string;
                imagePath = string.IsNullOrWhiteSpace(imagePath) ? null : Environment.ExpandEnvironmentVariables(imagePath);

                // svchost 服务：实际载体是 Parameters\ServiceDll
                string? serviceDll = null;
                if (imagePath is not null && imagePath.Contains("svchost", StringComparison.OrdinalIgnoreCase))
                {
                    serviceDll = (k.OpenSubKey("Parameters")?.GetValue("ServiceDll", null,
                        RegistryValueOptions.DoNotExpandEnvironmentNames) as string) is { } dll
                        ? Environment.ExpandEnvironmentVariables(dll) : null;
                }

                var target = StartupScanner.ExtractTarget(imagePath) ?? serviceDll;
                var (publisher, residual) = InspectTarget(target);

                entries.Add(new ServiceEntry
                {
                    Name = name,
                    DisplayName = (k.GetValue("DisplayName") as string)?.Trim() is { Length: > 0 } d ? d : name,
                    Description = k.GetValue("Description") as string,
                    StartType = (ServiceStartType)start,
                    Delayed = delayed,
                    ImagePath = imagePath ?? "",
                    ServiceDll = serviceDll,
                    Account = k.GetValue("ObjectName") as string,
                    TargetPath = target,
                    Publisher = publisher,
                    IsResidual = residual,
                    IsSystem = publisher?.Contains("Microsoft", StringComparison.OrdinalIgnoreCase) == true
                                || target?.StartsWith(@"C:\Windows", StringComparison.OrdinalIgnoreCase) == true,
                });
            }
            catch
            {
                // 单项失败不阻断
            }
        }

        return [.. entries.OrderBy(e => e.SortOrder).ThenBy(e => e.DisplayName, StringComparer.OrdinalIgnoreCase)];
    }

    /// <summary>修改启动类型（需管理员）。</summary>
    public StartupResult SetStartType(ServiceEntry entry, ServiceStartType newType)
    {
        try
        {
            using var key = Registry.LocalMachine.OpenSubKey($@"{ServicesRoot}\{entry.Name}", writable: true);
            if (key is null)
                return new StartupResult(false, "✘ 打开服务键被拒绝：需要管理员权限（以管理员重启后重试）。");
            key.SetValue("Start", (int)newType, RegistryValueKind.DWord);
            if (newType == ServiceStartType.Automatic)
            {
                // 改回自动时清延迟标记，由用户自行决定是否延迟（保守：不动 DelayedAutostart）
            }
            entry.StartType = newType;
            return new StartupResult(true,
                $"✔ 「{entry.DisplayName}」启动类型已改为 {newType switch
                {
                    ServiceStartType.Automatic => "自动",
                    ServiceStartType.Manual => "手动",
                    _ => "禁用",
                }}（重启后生效；改错服务可能影响系统功能）。");
        }
        catch (UnauthorizedAccessException)
        {
            return new StartupResult(false, "✘ 被拒绝：修改服务启动类型需要管理员权限（以管理员重启后重试）。");
        }
        catch (Exception ex)
        {
            return new StartupResult(false, $"✘ 修改失败：{ex.Message}");
        }
    }

    private static (string? Publisher, bool Residual) InspectTarget(string? target)
    {
        if (string.IsNullOrWhiteSpace(target)) return (null, false);
        try
        {
            if (!File.Exists(target)) return (null, true);
            var info = FileVersionInfo.GetVersionInfo(target);
            return (string.IsNullOrWhiteSpace(info.CompanyName) ? null : info.CompanyName.Trim(), false);
        }
        catch
        {
            return (null, false);
        }
    }
}
