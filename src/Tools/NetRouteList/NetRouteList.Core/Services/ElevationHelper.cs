using System.Diagnostics;
using System.Security.Principal;

namespace NetRouteList.Core.Services;

/// <summary>管理员权限检测与提权重启。</summary>
[System.Runtime.Versioning.SupportedOSPlatform("windows")]
public static class ElevationHelper
{
    public static bool IsElevated()
    {
        using var identity = WindowsIdentity.GetCurrent();
        var principal = new WindowsPrincipal(identity);
        return principal.IsInRole(WindowsBuiltInRole.Administrator);
    }

    /// <summary>用 UAC 提权重启当前程序。成功启动新进程后应退出当前进程。</summary>
    public static bool RestartElevated(out string error)
    {
        try
        {
            var exe = Environment.ProcessPath;
            if (string.IsNullOrEmpty(exe)) { error = "无法确定当前程序路径。"; return false; }
            Process.Start(new ProcessStartInfo(exe) { UseShellExecute = true, Verb = "runas" });
            error = "";
            return true;
        }
        catch (Exception ex)
        {
            error = $"请求提权失败：{ex.Message}（如果你取消了 UAC 弹窗，这就是原因。）";
            return false;
        }
    }
}
