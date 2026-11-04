using System.Diagnostics;
using MenuClean.Core.Models;
using Microsoft.Win32;

namespace MenuClean.Core.Services;

/// <summary>详情行（标签/值）。</summary>
public sealed record DetailRow(string Label, string Value);

/// <summary>
/// 条目详情服务：点击条目时实时读取注册表键的全部值与目标文件信息（版本/公司/大小/时间）。
/// </summary>
public sealed class EntryDetailService
{
    /// <summary>读取一个条目的扩展详情。</summary>
    public List<DetailRow> GetDetails(MenuEntry entry)
    {
        var rows = new List<DetailRow>();

        // 注册表键（真实 hive 定位 + HKCR 合并视图）
        var hiveKey = entry.HiveRoot switch
        {
            "HKCU" => @"HKCU\Software\Classes\" + entry.RegistryPath,
            "HKLM" => @"HKLM\Software\Classes\" + entry.RegistryPath,
            _ => @"HKCR\" + entry.RegistryPath,
        };
        rows.Add(new DetailRow("注册表键", hiveKey));

        // 键下的全部值 + command 子键
        try
        {
            using var key = Registry.ClassesRoot.OpenSubKey(entry.RegistryPath);
            if (key is not null)
            {
                foreach (var name in key.GetValueNames())
                {
                    var label = name.Length == 0 ? "（默认值）" : name;
                    var value = key.GetValue(name, null, RegistryValueOptions.DoNotExpandEnvironmentNames)?.ToString();
                    if (!string.IsNullOrEmpty(value))
                    {
                        rows.Add(new DetailRow(label, value));
                    }
                }

                if (key.OpenSubKey("command") is { } cmd)
                {
                    var cmdValue = cmd.GetValue(null, null, RegistryValueOptions.DoNotExpandEnvironmentNames)?.ToString();
                    if (!string.IsNullOrEmpty(cmdValue)) rows.Add(new DetailRow("command", cmdValue));
                    cmd.Dispose();
                }
            }
        }
        catch (Exception ex)
        {
            rows.Add(new DetailRow("读取注册表", "失败：" + ex.Message));
        }

        // 目标文件信息
        if (!string.IsNullOrEmpty(entry.TargetPath))
        {
            if (File.Exists(entry.TargetPath))
            {
                try
                {
                    var info = FileVersionInfo.GetVersionInfo(entry.TargetPath);
                    if (!string.IsNullOrEmpty(info.FileDescription)) rows.Add(new("文件描述", info.FileDescription));
                    if (!string.IsNullOrEmpty(info.ProductName)) rows.Add(new("产品", info.ProductName));
                    if (!string.IsNullOrEmpty(info.FileVersion)) rows.Add(new("文件版本", info.FileVersion));
                    var fi = new FileInfo(entry.TargetPath);
                    rows.Add(new("文件大小", $"{fi.Length / 1024.0:F0} KB"));
                    rows.Add(new("修改时间", fi.LastWriteTime.ToString("yyyy-MM-dd HH:mm")));
                }
                catch
                {
                    // 版本信息读取失败忽略
                }
            }
            else
            {
                rows.Add(new DetailRow("目标文件", $"{entry.TargetPath}（不存在 → 卸载残留）"));
            }
        }

        return rows;
    }
}
