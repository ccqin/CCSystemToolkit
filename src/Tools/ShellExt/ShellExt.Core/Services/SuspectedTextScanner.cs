using System.Runtime.InteropServices;
using System.Text;

namespace ShellExt.Core.Services;

/// <summary>
/// 疑似菜单文字扫描：以纯数据方式映射 DLL（LOAD_LIBRARY_AS_DATAFILE，不执行任何代码），
/// 遍历字符串资源表，用启发式（中文短语 / 常见动词关键词）筛出该扩展可能显示的菜单/界面文字。
/// 局限：运行时动态拼接的文字（如带文件名）静态无法获取。
/// </summary>
public static class SuspectedTextScanner
{
    private const uint LOAD_LIBRARY_AS_DATAFILE = 0x2;
    private const uint LOAD_LIBRARY_AS_IMAGE_RESOURCE = 0x20;

    /// <summary>常见右键菜单动词关键词（中英）。</summary>
    private static readonly string[] Keywords =
    [
        "打开", "上传", "下载", "发送", "扫描", "解压", "压缩", "同步", "分享", "共享",
        "复制", "删除", "编辑", "预览", "属性", "加密", "解密", "转换", "浏览", "管理",
        "上传到", "发送到", "用", "通过", "添加", "还原", "锁定", "解锁", "上传或",
        "open", "upload", "send", "scan", "extract", "sync", "share", "delete", "edit", "preview", "properties", "unlock",
    ];

    /// <summary>扫描一个 DLL 的字符串资源，返回疑似菜单/界面文字（最多 max 条）。</summary>
    public static List<string> Scan(string dllPath, int max = 10)
    {
        var results = new List<string>();
        nint hMod;
        try
        {
            hMod = LoadLibraryEx(dllPath, nint.Zero, LOAD_LIBRARY_AS_DATAFILE | LOAD_LIBRARY_AS_IMAGE_RESOURCE);
        }
        catch
        {
            return results;
        }
        if (hMod == nint.Zero) return results;

        try
        {
            var sb = new StringBuilder(512);
            // 字符串表按 16 条/块组织；扫 1..3072 覆盖绝大多数资源 ID
            for (int block = 0; block < 192 && results.Count < max; block++)
            {
                for (int i = 0; i < 16; i++)
                {
                    int id = block * 16 + i;
                    if (id == 0) continue;
                    int len = LoadString(hMod, id, sb, sb.Capacity);
                    if (len <= 0) continue;
                    var s = sb.ToString().Trim();
                    if (LooksLikeMenuText(s))
                    {
                        results.Add(s);
                        if (results.Count >= max) break;
                    }
                }
            }
        }
        finally
        {
            FreeLibrary(hMod);
        }
        return results;
    }

    /// <summary>启发式：像菜单/界面文字的字符串。</summary>
    private static bool LooksLikeMenuText(string s)
    {
        if (s.Length is < 2 or > 48) return false;
        if (s.Contains('\n') || s.Contains('\r') || s.Contains('\t')) return false;
        if (s.EndsWith(':') || s.Contains("://") || s.Contains('%')) return false;
        if (s.StartsWith("!", StringComparison.Ordinal)) return false;   // 加速器标记
        if (s.All(char.IsDigit) || s.All(char.IsWhiteSpace)) return false;

        // 含中文且短语化 → 高概率是界面文字
        bool hasChinese = s.Any(c => c >= 0x4E00 && c <= 0x9FFF);
        if (hasChinese) return true;

        // 英文：含常见动词关键词，或"首词大写的短语"
        foreach (var kw in Keywords)
        {
            if (s.Contains(kw, StringComparison.OrdinalIgnoreCase)) return true;
        }
        if (char.IsUpper(s[0]) && s.Contains(' ') && s.Split(' ').Length is <= 6)
        {
            return true;   // "Open with XXX" 类
        }
        return false;
    }

    [DllImport("kernel32", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern nint LoadLibraryEx(string lpFileName, nint hFile, uint dwFlags);

    [DllImport("user32", CharSet = CharSet.Unicode)]
    private static extern int LoadString(nint hInstance, int uID, StringBuilder lpBuffer, int cchBufferMax);

    [DllImport("kernel32", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool FreeLibrary(nint hModule);
}
