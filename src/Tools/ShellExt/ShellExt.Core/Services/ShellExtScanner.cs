using System.Diagnostics;
using Microsoft.Win32;
using ShellExt.Core.Models;

namespace ShellExt.Core.Services;

/// <summary>
/// Shell 扩展（COM）扫描器：
/// - Approved 官方清单（CLSID → 可读描述）
/// - 图标叠加 / 预览处理器专项注册点
/// - 各文件类型（.ext 与 ProgID）shellex 下的右键/缩略图/属性页处理器（按 CLSID 聚合）
/// 禁用走 HKCU Shell Extensions\Blocked 黑名单（可逆，普通权限可写）。
/// </summary>
public sealed class ShellExtScanner
{
    /// <summary>缩略图提供程序的固定谓词 GUID。</summary>
    private const string ThumbnailIid = "{e357fccd-a995-4576-b01f-234630154e96}";

    /// <summary>扫描全部（返回按 CLSID 聚合的扩展列表）。</summary>
    public List<ShellExtEntry> ScanAll(Action<string>? progress = null)
    {
        var blocked = GetBlockedClsids();
        var approved = LoadApproved();
        var byClsid = new Dictionary<string, ShellExtEntry>(StringComparer.OrdinalIgnoreCase);

        progress?.Invoke("扫描图标叠加…");
        ScanIconOverlays(byClsid, approved, blocked);

        progress?.Invoke("扫描预览处理器…");
        ScanPreviewHandlers(byClsid, approved, blocked);

        progress?.Invoke("扫描文件类型扩展（右键/缩略图/属性页）…");
        ScanFileTypeShellex(byClsid, approved, blocked);

        return [.. byClsid.Values.OrderByDescending(e => e.Occurrences)];
    }

    /// <summary>图标叠加：HKLM\...\Explorer\ShellIconOverlayIdentifiers（名字或默认值 = CLSID）。</summary>
    private static void ScanIconOverlays(Dictionary<string, ShellExtEntry> byClsid, Dictionary<string, string> approved, HashSet<string> blocked)
    {
        foreach (var hive in new[] { Registry.LocalMachine, Registry.CurrentUser })
        {
            using var key = hive.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Explorer\ShellIconOverlayIdentifiers");
            if (key is null) continue;
            foreach (var name in key.GetSubKeyNames())
            {
                using var sub = key.OpenSubKey(name);
                var clsid = (sub?.GetValue(null) as string)?.Trim() ?? name;
                if (!clsid.StartsWith('{')) continue;
                GetOrCreate(byClsid, clsid, name, approved, blocked).Kind = ShellExtKind.IconOverlay;
                Bump(byClsid, clsid);
            }
        }
    }

    /// <summary>预览处理器：HKLM\...\PreviewHandlers（值名 = CLSID，值 = 描述）。</summary>
    private static void ScanPreviewHandlers(Dictionary<string, ShellExtEntry> byClsid, Dictionary<string, string> approved, HashSet<string> blocked)
    {
        using var key = Registry.LocalMachine.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\PreviewHandlers");
        if (key is null) return;
        foreach (var clsid in key.GetValueNames())
        {
            if (!clsid.StartsWith('{')) continue;
            var desc = key.GetValue(clsid) as string;
            var e = GetOrCreate(byClsid, clsid, desc ?? clsid, approved, blocked);
            e.Kind = ShellExtKind.Preview;
            if (!string.IsNullOrEmpty(desc)) e.Description = desc!;
            Bump(byClsid, clsid);
        }
    }

    /// <summary>文件类型 shellex：通用根（*/文件夹等）+ 遍历 .ext 与其 ProgID 下的右键/缩略图/属性页处理器。</summary>
    private static void ScanFileTypeShellex(Dictionary<string, ShellExtEntry> byClsid, Dictionary<string, string> approved, HashSet<string> blocked)
    {
        var roots = new List<string>
        {
            // 通用根（大量第三方扩展注册在这里，而非具体后缀）
            "*", "AllFilesystemObjects", "Directory", "Folder", @"Directory\Background", "Drive",
        };
        foreach (var ext in Registry.ClassesRoot.GetSubKeyNames().Where(n => n.StartsWith('.') && n.Length <= 12))
        {
            roots.Add(ext);
            using var k = Registry.ClassesRoot.OpenSubKey(ext);
            if (k?.GetValue(null) as string is { Length: > 1 } progId && !progId.StartsWith('.') && !roots.Contains(progId))
            {
                roots.Add(progId);
            }
        }

        foreach (var root in roots)
        {
            CollectHandlers(byClsid, $@"{root}\shellex\ContextMenuHandlers", ShellExtKind.ContextMenu, approved, blocked);
            CollectHandlers(byClsid, $@"{root}\shellex\{ThumbnailIid}", ShellExtKind.Thumbnail, approved, blocked);
            CollectHandlers(byClsid, $@"{root}\shellex\PropertySheetHandlers", ShellExtKind.PropertySheet, approved, blocked);
        }
    }

    private static void CollectHandlers(Dictionary<string, ShellExtEntry> byClsid, string path, ShellExtKind kind,
        Dictionary<string, string> approved, HashSet<string> blocked)
    {
        try
        {
            using var key = Registry.ClassesRoot.OpenSubKey(path);
            if (key is null) return;
            foreach (var name in key.GetSubKeyNames())
            {
                using var sub = key.OpenSubKey(name);
                var clsid = (sub?.GetValue(null) as string)?.Trim() ?? name;
                if (!clsid.StartsWith('{')) continue;
                GetOrCreate(byClsid, clsid, name, approved, blocked).Kind = kind;
                Bump(byClsid, clsid);
            }
        }
        catch
        {
        }
    }

    /// <summary>取或建条目：解析 CLSID 的 DLL / 来源 / 残留 / 系统判定。</summary>
    private static ShellExtEntry GetOrCreate(Dictionary<string, ShellExtEntry> byClsid, string clsid, string nameHint,
        Dictionary<string, string> approved, HashSet<string> blocked)
    {
        if (byClsid.TryGetValue(clsid, out var existing)) return existing;

        string? dll = null, threading = null;
        if (Registry.ClassesRoot.OpenSubKey($@"CLSID\{clsid}\InprocServer32") is { } inproc)
        {
            dll = inproc.GetValue(null) as string;
            threading = inproc.GetValue("ThreadingModel") as string;
            inproc.Dispose();
        }

        string? publisher = null;
        bool residual = false;
        try
        {
            if (!string.IsNullOrEmpty(dll))
            {
                if (!File.Exists(dll)) residual = true;
                else publisher = FileVersionInfo.GetVersionInfo(dll).CompanyName?.Trim();
            }
        }
        catch
        {
        }

        bool isSystem = publisher?.Contains("Microsoft", StringComparison.OrdinalIgnoreCase) == true
                        || dll?.StartsWith(@"C:\Windows", StringComparison.OrdinalIgnoreCase) == true;

        var entry = new ShellExtEntry
        {
            Clsid = clsid,
            Description = approved.GetValueOrDefault(clsid) ?? nameHint,
            Kind = ShellExtKind.Unknown,
            Status = blocked.Contains(clsid) ? ShellExtStatus.Blocked : ShellExtStatus.Enabled,
            Dll = dll,
            ThreadingModel = threading,
            Publisher = publisher,
            IsResidual = residual,
            IsSystem = isSystem,
        };
        byClsid[clsid] = entry;
        return entry;
    }

    private static void Bump(Dictionary<string, ShellExtEntry> byClsid, string clsid)
    {
        if (byClsid.TryGetValue(clsid, out var e)) e.Occurrences++;
    }

    /// <summary>Approved 官方清单：CLSID → 描述。</summary>
    internal static Dictionary<string, string> LoadApproved()
    {
        var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var hive in new[] { Registry.LocalMachine, Registry.CurrentUser })
        {
            using var key = hive.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Shell Extensions\Approved");
            if (key is null) continue;
            foreach (var clsid in key.GetValueNames())
            {
                if (clsid.StartsWith('{') && key.GetValue(clsid) is string desc && !string.IsNullOrWhiteSpace(desc))
                {
                    result[clsid] = desc;
                }
            }
        }
        return result;
    }

    /// <summary>Blocked 黑名单（HKCU 优先，兼容 HKLM）。</summary>
    internal static HashSet<string> GetBlockedClsids()
    {
        var result = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var hive in new[] { Registry.CurrentUser, Registry.LocalMachine })
        {
            using var key = hive.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Shell Extensions\Blocked");
            if (key is null) continue;
            foreach (var name in key.GetValueNames()) result.Add(name);
        }
        return result;
    }
}
