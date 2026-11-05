using System.Diagnostics;
using Microsoft.Win32;
using StartupMgr.Core.Models;

namespace StartupMgr.Core.Services;

/// <summary>
/// 启动项扫描器：注册表 Run/RunOnce（HKCU / HKLM / WOW6432Node）+ 启动文件夹（用户/全局）。
/// 禁用状态读取 StartupApproved（与任务管理器/设置的禁用互通）。
/// </summary>
public sealed class StartupScanner
{
    private static readonly (RegistryKey Hive, string Path, StartupLocation Loc, string ApprovedKey)[] RunKeys =
    [
        (Registry.CurrentUser, @"Software\Microsoft\Windows\CurrentVersion\Run", StartupLocation.HkcuRun, "Run"),
        (Registry.LocalMachine, @"SOFTWARE\Microsoft\Windows\CurrentVersion\Run", StartupLocation.HklmRun, "Run"),
        (Registry.LocalMachine, @"SOFTWARE\WOW6432Node\Microsoft\Windows\CurrentVersion\Run", StartupLocation.HklmRun32, "Run32"),
        (Registry.CurrentUser, @"Software\Microsoft\Windows\CurrentVersion\RunOnce", StartupLocation.HkcuRunOnce, "Run"),
        (Registry.LocalMachine, @"SOFTWARE\Microsoft\Windows\CurrentVersion\RunOnce", StartupLocation.HklmRunOnce, "Run"),
    ];

    private const string UserFolder = @"%APPDATA%\Microsoft\Windows\Start Menu\Programs\Startup";
    private const string CommonFolder = @"%ProgramData%\Microsoft\Windows\Start Menu\Programs\Startup";

    public List<StartupEntry> ScanAll(Action<string>? progress = null)
    {
        var entries = new List<StartupEntry>();

        progress?.Invoke("扫描注册表 Run/RunOnce…");
        foreach (var (hive, path, loc, approvedKey) in RunKeys)
        {
            using var key = hive.OpenSubKey(path);
            if (key is null) continue;
            foreach (var name in key.GetValueNames())
            {
                if (key.GetValue(name, null, RegistryValueOptions.DoNotExpandEnvironmentNames) is not string cmd) continue;
                var expanded = Environment.ExpandEnvironmentVariables(cmd);
                var target = ExtractTarget(expanded);
                var (publisher, residual) = InspectTarget(target);

                entries.Add(new StartupEntry
                {
                    Name = name,
                    Command = expanded,
                    Location = loc,
                    Status = IsApprovedDisabled(approvedKey, name) ? StartupStatus.Disabled : StartupStatus.Enabled,
                    TargetPath = target,
                    Publisher = publisher,
                    IsResidual = residual,
                    SourcePath = $@"{(hive == Registry.CurrentUser ? "HKCU" : "HKLM")}\{path}\{name}",
                });
            }
        }

        progress?.Invoke("扫描启动文件夹…");
        ScanFolder(Environment.ExpandEnvironmentVariables(UserFolder), StartupLocation.UserFolder, "StartupFolder", entries);
        ScanFolder(Environment.ExpandEnvironmentVariables(CommonFolder), StartupLocation.CommonFolder, "StartupFolder", entries);

        return [.. entries.OrderBy(e => e.Status == StartupStatus.Disabled ? 1 : 0).ThenBy(e => e.Name, StringComparer.OrdinalIgnoreCase)];
    }

    private static void ScanFolder(string folder, StartupLocation loc, string approvedKey, List<StartupEntry> entries)
    {
        if (!Directory.Exists(folder)) return;
        foreach (var file in Directory.EnumerateFileSystemEntries(folder))
        {
            if (Path.GetFileName(file).Equals("desktop.ini", StringComparison.OrdinalIgnoreCase)) continue;   // 文件夹配置文件，非启动项
            string command = file;
            string? target = file;

            // .lnk 解析目标（IShellLink COM；简化：目标仍是 lnk 本身，存在性检查 lnk 文件）
            var (publisher, residual) = InspectTarget(file);

            entries.Add(new StartupEntry
            {
                Name = Path.GetFileName(file),
                Command = command,
                Location = loc,
                Status = IsApprovedDisabled(approvedKey, Path.GetFileName(file)) ? StartupStatus.Disabled : StartupStatus.Enabled,
                TargetPath = target,
                Publisher = publisher,
                IsResidual = residual,
                SourcePath = file,
            });
        }
    }

    // ----- StartupApproved（任务管理器同一禁用机制；均在 HKCU） -----

    private static RegistryKey OpenApproved(string approvedKey, bool writable) =>
        Registry.CurrentUser.CreateSubKey($@"Software\Microsoft\Windows\CurrentVersion\Explorer\StartupApproved\{approvedKey}", writable);

    internal static bool IsApprovedDisabled(string approvedKey, string name)
    {
        using var key = Registry.CurrentUser.OpenSubKey($@"Software\Microsoft\Windows\CurrentVersion\Explorer\StartupApproved\{approvedKey}");
        if (key?.GetValue(name) is not byte[] data || data.Length < 1) return false;
        // 首字节：偶数（2/6/0x0A…）= 启用，奇数（3/7/0x0B…）= 禁用
        return data[0] % 2 == 1;
    }

    /// <summary>写禁用/启用标记（12 字节：首字节 03=禁用 / 02=启用，其余时间戳由系统维护）。</summary>
    public static void SetApprovedDisabled(string approvedKey, string name, bool disabled)
    {
        using var key = OpenApproved(approvedKey, writable: true);
        var data = new byte[12];
        data[0] = disabled ? (byte)0x03 : (byte)0x02;
        data[4] = 0x00; data[8] = 0x00;   // FILETIME 占位，系统下次会更新
        key.SetValue(name, data, RegistryValueKind.Binary);
    }

    /// <summary>Approved 分组名（禁用标记用）。</summary>
    public static string ApprovedKeyOf(StartupEntry e) => e.Location switch
    {
        StartupLocation.HklmRun32 => "Run32",
        StartupLocation.UserFolder or StartupLocation.CommonFolder => "StartupFolder",
        _ => "Run",
    };

    // ----- 目标提取与检查（与清理右键菜单同法） -----

    internal static string? ExtractTarget(string? command)
    {
        if (string.IsNullOrWhiteSpace(command)) return null;
        var cmd = command.Trim();
        string first;
        if (cmd.StartsWith('"'))
        {
            var end = cmd.IndexOf('"', 1);
            first = end > 1 ? cmd[1..end] : cmd.Trim('"');
        }
        else
        {
            // 无引号但路径含空格（如 "E:\Program Files (x86)\DingDing\x.exe" /autorun）：
            // 逐段累积直到文件存在，避免取到 "E:\Program" 误判残留
            var parts = cmd.Split(' ');
            first = parts[0];
            for (int i = 1; i < parts.Length && i < 10; i++)
            {
                if (File.Exists(first)) break;
                first += " " + parts[i];
            }
        }
        return string.IsNullOrWhiteSpace(first) ? null : first;
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
