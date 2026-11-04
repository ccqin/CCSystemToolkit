using System.Diagnostics;

namespace MenuClean.Core.Services;

/// <summary>备份文件信息。</summary>
/// <param name="FileName">文件名。</param>
/// <param name="FullPath">完整路径。</param>
/// <param name="Time">创建时间。</param>
/// <param name="Size">大小（字节）。</param>
public sealed record BackupFile(string FileName, string FullPath, DateTime Time, long Size);

/// <summary>
/// .reg 备份服务：修改/删除注册表键前自动导出（reg export），
/// 备份目录 %LOCALAPPDATA%\SystemToolkit\MenuClean\backups\，可 reg import 还原。
/// </summary>
public sealed class MenuBackupService
{
    private static string BackupDir => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "SystemToolkit", "MenuClean", "backups");

    /// <summary>导出一个注册表键（以 HKCR 合并视图导出，还原时 reg import 即可）。</summary>
    public string Export(string registryPath, string nameHint)
    {
        Directory.CreateDirectory(BackupDir);
        var stamp = DateTime.Now.ToString("yyyyMMdd-HHmmss");
        var safe = string.Concat(nameHint.Where(char.IsLetterOrDigit).Take(20));
        var file = Path.Combine(BackupDir, $"{stamp}-{safe}.reg");

        // reg export 退出码 0 = 成功；失败时抛出由调用方处理
        var psi = new ProcessStartInfo("reg.exe", $"export \"HKCR\\{registryPath}\" \"{file}\" /y")
        {
            CreateNoWindow = true,
            UseShellExecute = false,
            RedirectStandardError = true,
        };
        using var p = Process.Start(psi)!;
        p.WaitForExit(5000);
        if (p.ExitCode != 0 || !File.Exists(file))
        {
            throw new InvalidOperationException("reg export 失败：" + p.StandardError.ReadToEnd());
        }
        return file;
    }

    /// <summary>列出全部备份（新→旧）。</summary>
    public List<BackupFile> List()
    {
        if (!Directory.Exists(BackupDir)) return [];
        return Directory.EnumerateFiles(BackupDir, "*.reg")
            .Select(f => new BackupFile(
                Path.GetFileName(f), f, File.GetLastWriteTime(f), new FileInfo(f).Length))
            .OrderByDescending(b => b.Time)
            .ToList();
    }

    /// <summary>还原一个备份（reg import；若原键在 HKLM 下需要管理员）。</summary>
    public CleanResult Restore(string file)
    {
        try
        {
            var psi = new ProcessStartInfo("reg.exe", $"import \"{file}\"")
            {
                CreateNoWindow = true,
                UseShellExecute = false,
                RedirectStandardError = true,
                RedirectStandardOutput = true,
            };
            using var p = Process.Start(psi)!;
            p.WaitForExit(8000);
            return p.ExitCode == 0
                ? new CleanResult(true, $"✔ 已还原备份：{Path.GetFileName(file)}。")
                : new CleanResult(false, $"✘ 还原失败：{p.StandardError.ReadToEnd().Trim()}");
        }
        catch (Exception ex)
        {
            return new CleanResult(false, $"✘ 还原失败：{ex.Message}");
        }
    }

    /// <summary>打开备份目录。</summary>
    public void OpenFolder()
    {
        Directory.CreateDirectory(BackupDir);
        Process.Start(new ProcessStartInfo("explorer.exe", BackupDir) { UseShellExecute = true });
    }
}
