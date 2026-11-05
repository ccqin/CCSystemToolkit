using System.Diagnostics;
using MenuClean.Core.Models;
using Microsoft.Win32;

namespace MenuClean.Core.Services;

/// <summary>场景定义（扫描位置 → 场景的映射）。</summary>
/// <param name="RootKeys">相对 HKCR 的根（不含 shell 后缀）。</param>
/// <param name="Scene">场景。</param>
/// <param name="Label">显示名。</param>
public sealed record SceneDef(string[] RootKeys, MenuSceneKind Scene, string Label, string Glyph);

/// <summary>
/// 右键菜单扫描器：按“场景”扫描 HKCR（HKLM/HKCU 合并视图）下的 shell 项与 shellex 扩展，
/// 解析命令目标做残留检测，用文件版本信息推断来源软件。
/// </summary>
public sealed class ContextMenuScanner
{
    /// <summary>固定场景（不含后缀场景，后缀动态发现）。</summary>
    private static readonly SceneDef[] FixedScenes =
    [
        new(["*"], MenuSceneKind.AnyFile, "任意文件", "📄"),
        new(["Directory", "Folder"], MenuSceneKind.Directory, "文件夹", "📁"),
        new(["Directory\\Background"], MenuSceneKind.Background, "空白处 / 桌面", "🖥"),
        new(["Drive"], MenuSceneKind.Drive, "驱动器", "💾"),
    ];

    /// <summary>系统保留 verb（真实菜单中置顶的标准命令）。</summary>
    private static readonly HashSet<string> SystemVerbs = new(StringComparer.OrdinalIgnoreCase)
    {
        "open", "opennewwindow", "edit", "print", "printto", "runas", "explore", "find", "preview",
        "openwith", "properties", "pinToHomeScreen", "ModernSharing", "UniformResourceIdentifier",
    };

    /// <summary>扫描全部场景（含动态发现的后缀场景）。纯读取，普通权限即可。</summary>
    public List<MenuEntry> ScanAll(Action<string, int>? progress = null)
    {
        var entries = new List<MenuEntry>();
        int done = 0;
        foreach (var def in FixedScenes)
        {
            progress?.Invoke($"扫描 {def.Label}…", done++);
            foreach (var root in def.RootKeys)
            {
                ScanShell($@"{root}\shell", def, entries);
                ScanShellex($@"{root}\shellex\ContextMenuHandlers", def, entries);
            }
        }

        // AllFilesystemObjects 的项同时出现在文件/文件夹/驱动器上，并入“任意文件”
        var afo = new SceneDef(["AllFilesystemObjects"], MenuSceneKind.AnyFile, "任意文件", "📄");
        ScanShell(@"AllFilesystemObjects\shell", afo, entries);
        ScanShellex(@"AllFilesystemObjects\shellex\ContextMenuHandlers", afo, entries);

        progress?.Invoke("发现文件类型注入…", done++);
        ScanExtensionScenes(entries);

        return entries;
    }

    /// <summary>发现哪些后缀有自定义菜单项：SystemFileAssociations / HKCR\.ext 直挂 / .ext→ProgID 间接。</summary>
    private void ScanExtensionScenes(List<MenuEntry> entries)
    {
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        // A. SystemFileAssociations\.xxx\shell
        using (var sfa = Registry.ClassesRoot.OpenSubKey("SystemFileAssociations"))
        {
            if (sfa is not null)
            {
                foreach (var ext in sfa.GetSubKeyNames().Where(n => n.StartsWith('.')))
                {
                    if (CountShellChildren(sfa, $@"{ext}\shell") == 0) continue;
                    if (!seen.Add(ext.ToLowerInvariant())) continue;
                    var def = new SceneDef([$"SystemFileAssociations\\{ext}"], MenuSceneKind.FileExtension, ext, "🧩");
                    ScanShell($@"SystemFileAssociations\{ext}\shell", def, entries);
                }
            }
        }

        // B/C. 遍历 HKCR\.xxx：直挂 shell 与 ProgID 间接（.exe→exefile 等，多数第三方菜单注册在 ProgID 下）
        var extKeys = new List<(string Ext, string? ProgId)>();
        foreach (var ext in Registry.ClassesRoot.GetSubKeyNames().Where(n => n.StartsWith('.') && n.Length <= 12))
        {
            try
            {
                using var k = Registry.ClassesRoot.OpenSubKey(ext);
                if (k is null) continue;
                var progId = k.GetValue(null) as string;
                extKeys.Add((ext, string.IsNullOrWhiteSpace(progId) || progId.StartsWith('.') ? null : progId));
            }
            catch
            {
                // 个别键访问受限，跳过
            }
        }

        foreach (var (ext, progId) in extKeys)
        {
            try
            {
                // B. .xxx\shell 直挂
                if (CountShellChildren(Registry.ClassesRoot, $@"{ext}\shell") > 0 && seen.Add(ext.ToLowerInvariant()))
                {
                    var def = new SceneDef([ext], MenuSceneKind.FileExtension, ext, "🧩");
                    ScanShell($@"{ext}\shell", def, entries);
                }

                // C. .xxx → ProgID（exefile/txtfile/jpegfile…）：菜单在 HKCR\<ProgID>\shell 与 shellex
                if (progId is null) continue;
                bool hasShell = CountShellChildren(Registry.ClassesRoot, $@"{progId}\shell") > 0;
                bool hasShellex = CountShellChildren(Registry.ClassesRoot, $@"{progId}\shellex\ContextMenuHandlers") > 0;
                if (!hasShell && !hasShellex) continue;

                var pd = new SceneDef([progId], MenuSceneKind.FileExtension, ext, "🧩");
                if (hasShell) ScanShell($@"{progId}\shell", pd, entries);
                ScanShellex($@"{progId}\shellex\ContextMenuHandlers", pd, entries);
            }
            catch
            {
            }
        }
    }

    private static int CountShellChildren(RegistryKey parent, string path)
    {
        try
        {
            using var k = parent.OpenSubKey(path);
            return k?.SubKeyCount ?? 0;
        }
        catch
        {
            return 0;
        }
    }

    /// <summary>扫描 shell 项（标准命令）。</summary>
    private void ScanShell(string path, SceneDef def, List<MenuEntry> entries)
    {
        using var key = Registry.ClassesRoot.OpenSubKey(path);
        if (key is null) return;

        foreach (var verb in key.GetSubKeyNames())
        {
            try
            {
                using var sub = key.OpenSubKey(verb);
                if (sub is null) continue;

                string? command = null;
                if (sub.OpenSubKey("command") is { } cmdKey)
                {
                    command = cmdKey.GetValue(null) as string;
                    cmdKey.Dispose();
                }

                // 标题回退：MUIVerb → 默认值 → 键名（空字符串也要回退，否则渲染成空行）
                var rawTitle = (sub.GetValue("MUIVerb") as string)?.Trim();
                if (string.IsNullOrWhiteSpace(rawTitle)) rawTitle = (sub.GetValue(null) as string)?.Trim();
                var title = PrettyTitle(string.IsNullOrWhiteSpace(rawTitle) ? verb : rawTitle);
                bool legacy = sub.GetValueNames().Any(n => n.Equals("LegacyDisable", StringComparison.OrdinalIgnoreCase));
                bool extended = sub.GetValueNames().Any(n => n.Equals("Extended", StringComparison.OrdinalIgnoreCase));

                var target = ExtractTarget(command);
                var (publisher, residual) = InspectTarget(target);

                entries.Add(new MenuEntry
                {
                    RegistryPath = $@"{path}\{verb}",
                    HiveRoot = ResolveHiveRoot($@"{path}\{verb}"),
                    VerbName = verb,
                    Title = title,
                    Command = command,
                    TargetPath = target,
                    Kind = MenuEntryKind.Shell,
                    Status = legacy ? MenuEntryStatus.DisabledLegacy
                             : extended ? MenuEntryStatus.ShiftOnly
                             : MenuEntryStatus.Enabled,
                    Scene = def.Scene,
                    SceneLabel = def.Label,
                    Publisher = publisher,
                    IsResidual = residual,
                    IsSystem = IsSystemEntry(verb, publisher, target),
                });
            }
            catch
            {
                // 单项失败不阻断整体扫描
            }
        }
    }

    /// <summary>扫描 shellex 上下文扩展（COM 处理器）。</summary>
    private void ScanShellex(string path, SceneDef def, List<MenuEntry> entries)
    {
        using var key = Registry.ClassesRoot.OpenSubKey(path);
        if (key is null) return;

        string[] blocked = GetBlockedClsids();

        foreach (var handler in key.GetSubKeyNames())
        {
            try
            {
                using var sub = key.OpenSubKey(handler);
                var clsid = (sub?.GetValue(null) as string)?.Trim() ?? handler;
                if (!clsid.StartsWith('{')) continue;

                var title = handler;
                string? dll = null;
                if (Registry.ClassesRoot.OpenSubKey($@"CLSID\{clsid}\InprocServer32") is { } inproc)
                {
                    dll = inproc.GetValue(null) as string;
                    inproc.Dispose();
                }

                var (publisher, residual) = InspectTarget(dll);
                bool isBlocked = blocked.Contains(clsid, StringComparer.OrdinalIgnoreCase);

                // 标题优先用 DLL 的文件描述（如“夸克网盘右键菜单”，可读可搜索）；
                // COM 扩展的真实菜单文字（如“发送到手机”）是 DLL 运行时生成的，注册表里不存在
                var displayTitle = title;
                try
                {
                    if (dll is not null && File.Exists(dll))
                    {
                        var desc = FileVersionInfo.GetVersionInfo(dll).FileDescription;
                        if (!string.IsNullOrWhiteSpace(desc)) displayTitle = desc!;
                    }
                }
                catch
                {
                }

                entries.Add(new MenuEntry
                {
                    RegistryPath = $@"{path}\{handler}",
                    HiveRoot = ResolveHiveRoot($@"{path}\{handler}"),
                    VerbName = handler,
                    Title = displayTitle,
                    Command = dll,
                    TargetPath = dll,
                    Kind = MenuEntryKind.Shellex,
                    Status = isBlocked ? MenuEntryStatus.Blocked : MenuEntryStatus.Enabled,
                    Scene = def.Scene,
                    SceneLabel = def.Label,
                    Publisher = publisher,
                    IsResidual = residual,
                    IsSystem = IsSystemEntry(handler, publisher, dll),
                    Clsid = clsid,
                });
            }
            catch
            {
            }
        }
    }

    /// <summary>Shell Extensions\Blocked 黑名单（HKCU 优先，兼容 HKLM）。</summary>
    internal static string[] GetBlockedClsids()
    {
        var result = new List<string>();
        foreach (var root in new[]
                 {
                     Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Shell Extensions\Blocked"),
                     Registry.LocalMachine.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Shell Extensions\Blocked"),
                 })
        {
            if (root is null) continue;
            result.AddRange(root.GetValueNames());
            root.Dispose();
        }
        return [.. result];
    }

    /// <summary>判断条目实际落在哪个 hive（HKCU 优先于 HKLM 合并）。</summary>
    private static string ResolveHiveRoot(string path) =>
        Registry.CurrentUser.OpenSubKey(@"Software\Classes\" + path) is not null ? "HKCU"
        : Registry.LocalMachine.OpenSubKey(@"Software\Classes\" + path) is not null ? "HKLM"
        : "HKCR";

    /// <summary>从命令行提取主程序路径（引号段 / 首个空格前；rundll32 取 dll）。</summary>
    internal static string? ExtractTarget(string? command)
    {
        if (string.IsNullOrWhiteSpace(command)) return null;
        var cmd = command.Trim();

        // 间接字符串命令（@dll.dll,-id）：目标是 dll 本身（存在性/系统判定用）
        if (cmd.StartsWith('@'))
        {
            var pathPart = cmd[1..].Split(',', 2)[0];
            return Environment.ExpandEnvironmentVariables(pathPart);
        }

        cmd = Environment.ExpandEnvironmentVariables(cmd);

        string first;
        if (cmd.StartsWith('"'))
        {
            var end = cmd.IndexOf('"', 1);
            first = end > 1 ? cmd[1..end] : cmd.Trim('"');
        }
        else
        {
            // 无引号但路径含空格：逐段累积直到文件存在（避免误判残留）
            var parts = cmd.Split(' ');
            first = parts[0];
            for (int i = 1; i < parts.Length && i < 10; i++)
            {
                if (File.Exists(first)) break;
                first += " " + parts[i];
            }
        }
        if (string.IsNullOrWhiteSpace(first)) return null;

        // rundll32.exe,x.dll → 目标是 dll
        var rest = cmd[first.Length..].TrimStart();
        if (first.Contains("rundll32", StringComparison.OrdinalIgnoreCase) && rest.Length > 0)
        {
            var dll = rest.Split(',', 2)[0].Trim().Trim('"');
            if (dll.Contains('\\')) return dll;
        }
        return first;
    }

    /// <summary>检查目标文件：存在性（残留）+ 版本公司（来源）。</summary>
    private static (string? Publisher, bool Residual) InspectTarget(string? target)
    {
        if (string.IsNullOrWhiteSpace(target)) return (null, false);
        try
        {
            if (!File.Exists(target)) return (null, true);
            var info = FileVersionInfo.GetVersionInfo(target);
            var company = string.IsNullOrWhiteSpace(info.CompanyName) ? null : info.CompanyName.Trim();
            return (company, false);
        }
        catch
        {
            return (null, false);
        }
    }

    /// <summary>系统项判定：标准 verb、微软公司、或目标在系统目录。</summary>
    private static bool IsSystemEntry(string verb, string? publisher, string? target)
    {
        if (SystemVerbs.Contains(verb)) return true;
        if (publisher?.Contains("Microsoft", StringComparison.OrdinalIgnoreCase) == true) return true;
        if (target is not null && target.StartsWith(@"C:\Windows", StringComparison.OrdinalIgnoreCase)) return true;
        return false;
    }

    /// <summary>间接字符串（@dll,-id）缩短为文件名+资源号，便于阅读。</summary>
    private static string PrettyTitle(string title)
    {
        if (!title.StartsWith('@') || !title.Contains(',')) return title;
        var pathPart = title[1..title.IndexOf(',')];
        var file = Path.GetFileName(Environment.ExpandEnvironmentVariables(pathPart));
        var resId = title[title.IndexOf(',')..];
        return $"@{file}{resId}";
    }
}
