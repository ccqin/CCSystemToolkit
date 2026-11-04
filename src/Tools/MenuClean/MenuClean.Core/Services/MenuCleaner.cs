using MenuClean.Core.Models;
using Microsoft.Win32;

namespace MenuClean.Core.Services;

/// <summary>操作结果（沿用权限医生的模式）。</summary>
public sealed record CleanResult(bool Success, string Message);

/// <summary>
/// 菜单清理器：禁用/启用/删除。
/// 禁用策略全部走 HKCU（HKCU\Software\Classes 对 HKCR 的合并优先级高于 HKLM，且普通权限可写）：
/// - shell 项：在 HKCU 同路径写 LegacyDisable（不动 HKLM 原键，完全可逆）
/// - shellex：HKCU\...\Shell Extensions\Blocked 黑名单加 CLSID
/// - 删除：需先备份；HKLM 删除需要管理员。
/// </summary>
public sealed class MenuCleaner(MenuBackupService backup)
{
    /// <summary>禁用一个条目（可逆）。</summary>
    public CleanResult Disable(MenuEntry entry)
    {
        try
        {
            if (entry.Kind == MenuEntryKind.Shell)
            {
                using var key = EnsureHkcuKey(entry.RegistryPath);
                key.SetValue("LegacyDisable", "");
                return new CleanResult(true, $"✔ 已禁用「{entry.Title}」（LegacyDisable，可随时恢复）。");
            }

            if (entry.Clsid is not { } clsid) return new CleanResult(false, "✘ 该扩展缺少 CLSID，无法禁用。");
            using (var blocked = Registry.CurrentUser.CreateSubKey(
                       @"Software\Microsoft\Windows\CurrentVersion\Shell Extensions\Blocked",
                       writable: true))
            {
                blocked.SetValue(clsid, "");
            }
            return new CleanResult(true, $"✔ 已禁用扩展「{entry.Title}」（黑名单拦截，可随时恢复）。");
        }
        catch (UnauthorizedAccessException)
        {
            return new CleanResult(false, "✘ 被拒绝：请以管理员重启工具集后重试。");
        }
        catch (Exception ex)
        {
            return new CleanResult(false, $"✘ 禁用失败：{ex.Message}");
        }
    }

    /// <summary>启用（撤销禁用）。</summary>
    public CleanResult Enable(MenuEntry entry)
    {
        try
        {
            if (entry.Kind == MenuEntryKind.Shell)
            {
                using var key = Registry.CurrentUser.OpenSubKey(@"Software\Classes\" + entry.RegistryPath, writable: true);
                key?.DeleteValue("LegacyDisable", throwOnMissingValue: false);
                return new CleanResult(true, $"✔ 已启用「{entry.Title}」。");
            }

            if (entry.Clsid is not { } clsid) return new CleanResult(false, "✘ 该扩展缺少 CLSID。");
            using (var blocked = Registry.CurrentUser.OpenSubKey(
                       @"Software\Microsoft\Windows\CurrentVersion\Shell Extensions\Blocked", writable: true))
            {
                blocked?.DeleteValue(clsid, throwOnMissingValue: false);
            }
            return new CleanResult(true, $"✔ 已启用扩展「{entry.Title}」。");
        }
        catch (Exception ex)
        {
            return new CleanResult(false, $"✘ 启用失败：{ex.Message}");
        }
    }

    /// <summary>删除条目（先自动备份 .reg；系统项拒绝；HKLM 下的键需要管理员）。</summary>
    public CleanResult Delete(MenuEntry entry)
    {
        if (entry.IsSystem)
        {
            return new CleanResult(false, "✘ 已阻止：系统/Microsoft 项不提供删除（可禁用）。");
        }

        try
        {
            var hivePath = entry.HiveRoot switch
            {
                "HKCU" => @"Software\Classes\" + entry.RegistryPath,
                "HKLM" => @"Software\Classes\" + entry.RegistryPath,
                _ => entry.RegistryPath,
            };
            var root = entry.HiveRoot == "HKCU" ? Registry.CurrentUser
                : entry.HiveRoot == "HKLM" ? Registry.LocalMachine
                : Registry.ClassesRoot;

            // 备份（reg export 以合并视图路径导出即可还原内容）
            var exported = backup.Export(entry.RegistryPath, entry.VerbName);

            using (root.OpenSubKey(hivePath, writable: true))
            {
                // 探测可写性：HKLM 普通权限会在 OpenSubKey(writable) 得 null
            }
            var parent = root.OpenSubKey(GetParentPath(hivePath), writable: true);
            if (parent is null)
            {
                return new CleanResult(false,
                    "✘ 删除被拒绝：该键位于 HKLM，需要管理员权限。请以管理员重启工具集，或使用等效命令。");
            }
            using (parent)
            {
                parent.DeleteSubKeyTree(entry.VerbName, throwOnMissingSubKey: false);
            }
            return new CleanResult(true,
                $"✔ 已删除「{entry.Title}」。（备份：{System.IO.Path.GetFileName(exported)}，可还原）");
        }
        catch (UnauthorizedAccessException)
        {
            return new CleanResult(false, "✘ 删除被拒绝：需要管理员权限（以管理员重启后重试）。");
        }
        catch (Exception ex)
        {
            return new CleanResult(false, $"✘ 删除失败：{ex.Message}");
        }
    }

    /// <summary>等效命令文本（复制到管理员终端执行）。</summary>
    public static string CommandText(MenuEntry entry, string action) => action switch
    {
        "disable" => entry.Kind == MenuEntryKind.Shell
            ? $"reg add \"HKCU\\Software\\Classes\\{entry.RegistryPath}\" /v LegacyDisable /t REG_SZ /d \"\" /f"
            : $"reg add \"HKCU\\Software\\Microsoft\\Windows\\CurrentVersion\\Shell Extensions\\Blocked\" /v {entry.Clsid} /t REG_SZ /d \"\" /f",
        "enable" => entry.Kind == MenuEntryKind.Shell
            ? $"reg delete \"HKCU\\Software\\Classes\\{entry.RegistryPath}\" /v LegacyDisable /f"
            : $"reg delete \"HKCU\\Software\\Microsoft\\Windows\\CurrentVersion\\Shell Extensions\\Blocked\" /v {entry.Clsid} /f",
        _ => $"reg delete \"{(entry.HiveRoot == "HKCR" ? "HKCR" : entry.HiveRoot + "\\Software\\Classes")}\\{entry.RegistryPath}\" /f",
    };

    private static RegistryKey EnsureHkcuKey(string path) =>
        Registry.CurrentUser.CreateSubKey(@"Software\Classes\" + path, writable: true)
        ?? throw new InvalidOperationException("无法创建 HKCU 覆盖键。");

    private static string GetParentPath(string path) => path[..path.LastIndexOf('\\')];
}
