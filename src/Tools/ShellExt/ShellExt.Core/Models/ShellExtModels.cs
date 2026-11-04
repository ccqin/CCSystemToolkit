namespace ShellExt.Core.Models;

/// <summary>Shell 扩展类型。</summary>
public enum ShellExtKind
{
    ContextMenu,    // 右键菜单处理器
    IconOverlay,    // 图标叠加（OneDrive 绿勾 / Git 图标，全局上限 15 个）
    Preview,        // 预览窗格处理器
    Thumbnail,      // 缩略图提供程序
    PropertySheet,  // 属性页
    Unknown,
}

public enum ShellExtStatus
{
    Enabled,
    Blocked,   // 在 Shell Extensions\Blocked 黑名单中（可逆禁用）
}

/// <summary>一个 COM Shell 扩展（按 CLSID 聚合；同一扩展可能注册在多个文件类型上）。</summary>
public sealed class ShellExtEntry
{
    public required string Clsid { get; init; }

    /// <summary>可读描述（Approved 官方清单 / DLL 描述 / 键名；扫描中可能被更好的来源覆盖）。</summary>
    public string Description { get; set; } = "";

    public ShellExtKind Kind { get; set; }
    public ShellExtStatus Status { get; set; }

    /// <summary>DLL 路径（CLSID\InprocServer32）。</summary>
    public string? Dll { get; init; }

    /// <summary>线程模型。</summary>
    public string? ThreadingModel { get; init; }

    /// <summary>来源公司（DLL 版本信息）。</summary>
    public string? Publisher { get; init; }

    /// <summary>DLL 不存在（软件卸载残留）。</summary>
    public bool IsResidual { get; init; }

    public bool IsSystem { get; init; }

    /// <summary>在多少个注册点被引用（文件类型 × 扩展类型）。</summary>
    public int Occurrences { get; set; }

    /// <summary>疑似菜单/界面文字（DLL 字符串资源启发式扫描，静态分析，动态拼接的无法获取）。</summary>
    public List<string> SuspectedTexts { get; set; } = [];

    /// <summary>疑似文字的拼接展示（表格列用）。</summary>
    public string SuspectedTextsText => SuspectedTexts.Count == 0 ? "" : string.Join(" / ", SuspectedTexts);

    public string KindText => Kind switch
    {
        ShellExtKind.ContextMenu => "右键菜单",
        ShellExtKind.IconOverlay => "图标叠加",
        ShellExtKind.Preview => "预览",
        ShellExtKind.Thumbnail => "缩略图",
        ShellExtKind.PropertySheet => "属性页",
        _ => "其他",
    };

    public string StatusText => Status == ShellExtStatus.Blocked ? "已禁用" : "启用";
    public bool IsEnabled => Status != ShellExtStatus.Blocked;
    public string SourceName => IsSystem ? "Microsoft / 系统" : Publisher ?? "未知来源";
}
