using Microsoft.Win32;
using ShellExt.Core.Models;

namespace ShellExt.Core.Services;

/// <summary>操作结果。</summary>
public sealed record ExtResult(bool Success, string Message);

/// <summary>
/// COM 扩展启用/禁用：HKCU Shell Extensions\Blocked 黑名单（可逆，普通权限可写）。
/// </summary>
public sealed class ShellExtCleaner
{
    private const string BlockedPath = @"Software\Microsoft\Windows\CurrentVersion\Shell Extensions\Blocked";

    public ExtResult SetBlocked(ShellExtEntry entry, bool block)
    {
        try
        {
            using var key = Registry.CurrentUser.CreateSubKey(BlockedPath, writable: true);
            if (block) key.SetValue(entry.Clsid, "");
            else key.DeleteValue(entry.Clsid, throwOnMissingValue: false);

            entry.Status = block ? ShellExtStatus.Blocked : ShellExtStatus.Enabled;
            return new ExtResult(true,
                $"✔ 已{(block ? "禁用" : "启用")}「{entry.Description}」（{(block ? "加入黑名单，可随时恢复" : "已从黑名单移除")}）。资源管理器可能需要重启后生效。");
        }
        catch (Exception ex)
        {
            return new ExtResult(false, $"✘ 操作失败：{ex.Message}");
        }
    }

    /// <summary>等效命令（管理员终端）。</summary>
    public static string CommandText(ShellExtEntry entry, bool block) => block
        ? $"reg add \"HKCU\\{BlockedPath}\" /v {entry.Clsid} /t REG_SZ /d \"\" /f"
        : $"reg delete \"HKCU\\{BlockedPath}\" /v {entry.Clsid} /f";
}
