using System.Text.Json;
using Microsoft.Win32;
using StartupMgr.Core.Models;

namespace StartupMgr.Core.Services;

/// <summary>操作结果。</summary>
public sealed record StartupResult(bool Success, string Message);

/// <summary>备份记录（删除前自动保存，JSON 持久化，可还原）。</summary>
public sealed record StartupBackupRecord(
    string Id, DateTime Time, string Kind,   // registry / file
    string? Hive = null, string? KeyPath = null, string? ValueName = null, string? ValueData = null,   // 注册表项还原参数
    string? FilePath = null, string? MovedTo = null);                                                    // 文件项还原参数

/// <summary>
/// 启动项清理器：禁用/启用（StartupApproved，与任务管理器互通）、删除（自动 JSON 备份）、还原、新增（HKCU Run）。
/// </summary>
public sealed class StartupCleaner
{
    private static string BackupDir => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "SystemToolkit", "StartupMgr", "backups");
    private static string IndexPath => Path.Combine(BackupDir, "index.json");

    private static readonly JsonSerializerOptions JsonOpts = new() { WriteIndented = true };

    // ----- 禁用 / 启用 -----

    public StartupResult Toggle(StartupEntry entry, bool disable)
    {
        if (!entry.SupportsToggle)
        {
            return new StartupResult(false,
                $"✘ 「{entry.LocationText}」类启动项不受 StartupApproved 禁用机制管辖，仅提供查看与删除。");
        }
        try
        {
            StartupScanner.SetApprovedDisabled(StartupScanner.ApprovedKeyOf(entry), entry.Name, disable);
            entry.Status = disable ? StartupStatus.Disabled : StartupStatus.Enabled;
            return new StartupResult(true,
                $"✔ 已{(disable ? "禁用" : "启用")}「{entry.Name}」（任务管理器同一机制，注销/重启后生效）。");
        }
        catch (Exception ex)
        {
            return new StartupResult(false, $"✘ 操作失败：{ex.Message}");
        }
    }

    // ----- 删除（自动备份） -----

    public StartupResult Delete(StartupEntry entry)
    {
        if (!entry.SupportsDelete)
        {
            return new StartupResult(false,
                $"✘ 「{entry.LocationText}」是系统关键监控位（改坏可能导致无法进桌面），本工具仅展示不提供修改。");
        }
        try
        {
            if (entry.Location is StartupLocation.UserFolder or StartupLocation.CommonFolder)
            {
                Directory.CreateDirectory(BackupDir);
                var id = DateTime.Now.ToString("yyyyMMdd-HHmmss");
                var movedTo = Path.Combine(BackupDir, $"{id}-{Sanitize(entry.Name)}");
                File.Move(entry.SourcePath, movedTo);
                SaveRecord(new StartupBackupRecord(id, DateTime.Now, "file", null, null, null, null, entry.SourcePath, movedTo));
                return new StartupResult(true, $"✔ 已删除启动文件「{entry.Name}」（已备份，可还原）。");
            }

            RegistryKey hive;
            string path;
            if (entry.Location == StartupLocation.DefaultUserRun)
            {
                hive = Registry.Users;
                path = @".DEFAULT\Software\Microsoft\Windows\CurrentVersion\Run";
            }
            else if (entry.Location is StartupLocation.HkcuPolicyRun or StartupLocation.HklmPolicyRun)
            {
                hive = entry.Location == StartupLocation.HkcuPolicyRun ? Registry.CurrentUser : Registry.LocalMachine;
                path = @"Software\Microsoft\Windows\CurrentVersion\Policies\Explorer\Run";
            }
            else
            {
                hive = entry.Location is StartupLocation.HkcuRun or StartupLocation.HkcuRunOnce
                    ? Registry.CurrentUser
                    : Registry.LocalMachine;
                path = SubPath(entry);
            }
            using (var key = hive.OpenSubKey(path, writable: true))
            {
                if (key is null)
                    return new StartupResult(false, "✘ 删除被拒绝：HKLM 下的项需要管理员（以管理员重启后重试）。");
                var raw = key.GetValue(entry.Name, null, RegistryValueOptions.DoNotExpandEnvironmentNames);
                key.DeleteValue(entry.Name);
                SaveRecord(new StartupBackupRecord(
                    DateTime.Now.ToString("yyyyMMdd-HHmmss"), DateTime.Now, "registry",
                    hive == Registry.CurrentUser ? "HKCU" : hive == Registry.Users ? "HKU" : "HKLM",
                    path, entry.Name, raw?.ToString()));
            }
            return new StartupResult(true, $"✔ 已删除「{entry.Name}」（已备份，可还原）。");
        }
        catch (UnauthorizedAccessException)
        {
            return new StartupResult(false, "✘ 删除被拒绝：需要管理员权限（以管理员重启后重试）。");
        }
        catch (Exception ex)
        {
            return new StartupResult(false, $"✘ 删除失败：{ex.Message}");
        }
    }

    private static string SubPath(StartupEntry e) => e.Location switch
    {
        StartupLocation.HklmRun => @"SOFTWARE\Microsoft\Windows\CurrentVersion\Run",
        StartupLocation.HklmRun32 => @"SOFTWARE\WOW6432Node\Microsoft\Windows\CurrentVersion\Run",
        StartupLocation.HkcuRunOnce => @"Software\Microsoft\Windows\CurrentVersion\RunOnce",
        StartupLocation.HklmRunOnce => @"SOFTWARE\Microsoft\Windows\CurrentVersion\RunOnce",
        _ => @"Software\Microsoft\Windows\CurrentVersion\Run",
    };

    // ----- 还原 -----

    public List<StartupBackupRecord> ListBackups()
    {
        if (!File.Exists(IndexPath)) return [];
        try
        {
            return JsonSerializer.Deserialize<List<StartupBackupRecord>>(File.ReadAllText(IndexPath)) ?? [];
        }
        catch
        {
            return [];
        }
    }

    public StartupResult Restore(StartupBackupRecord record)
    {
        try
        {
            if (record.Kind == "registry")
            {
                var hive = record.Hive switch
                {
                    "HKCU" => Registry.CurrentUser,
                    "HKU" => Registry.Users,
                    _ => Registry.LocalMachine,
                };
                using var key = hive.CreateSubKey(record.KeyPath!, writable: true);
                key.SetValue(record.ValueName!, record.ValueData ?? "");
                return new StartupResult(true, $"✔ 已还原注册表启动项「{record.ValueName}」。");
            }

            if (record.MovedTo is { } from && record.FilePath is { } to && File.Exists(from))
            {
                File.Move(from, to);
                return new StartupResult(true, $"✔ 已还原启动文件「{Path.GetFileName(to)}」。");
            }
            return new StartupResult(false, "✘ 备份文件已不存在，无法还原。");
        }
        catch (Exception ex)
        {
            return new StartupResult(false, $"✘ 还原失败：{ex.Message}");
        }
    }

    // ----- 新增（HKCU Run） -----

    public StartupResult Add(string name, string command)
    {
        if (string.IsNullOrWhiteSpace(name) || string.IsNullOrWhiteSpace(command))
            return new StartupResult(false, "✘ 名称与命令都不能为空。");
        try
        {
            using var key = Registry.CurrentUser.CreateSubKey(@"Software\Microsoft\Windows\CurrentVersion\Run", writable: true);
            if (key.GetValueNames().Contains(name))
                return new StartupResult(false, "✘ HKCU·Run 里已存在同名项。");
            key.SetValue(name, command);
            return new StartupResult(true, $"✔ 已添加启动项「{name}」到 HKCU·Run。");
        }
        catch (Exception ex)
        {
            return new StartupResult(false, $"✘ 添加失败：{ex.Message}");
        }
    }

    public void OpenBackupFolder()
    {
        Directory.CreateDirectory(BackupDir);
        System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo("explorer.exe", BackupDir) { UseShellExecute = true });
    }

    private void SaveRecord(StartupBackupRecord record)
    {
        Directory.CreateDirectory(BackupDir);
        var list = ListBackups();
        list.Add(record);
        if (list.Count > 200) list = [.. list.TakeLast(200)];
        File.WriteAllText(IndexPath, JsonSerializer.Serialize(list, JsonOpts));
    }

    private static string Sanitize(string name) => string.Concat(name.Where(char.IsLetterOrDigit).Take(30));
}
