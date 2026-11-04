namespace MenuClean.Core.Models;

/// <summary>右键场景（决定菜单项出现在哪类对象的右键里）。</summary>
public enum MenuSceneKind
{
    AnyFile,        // 任意文件
    Directory,      // 文件夹上
    Background,     // 文件夹/桌面空白处
    Drive,          // 驱动器
    FileExtension,  // 特定后缀
}

/// <summary>菜单项类型：标准 shell 命令 / COM 上下文扩展。</summary>
public enum MenuEntryKind
{
    Shell,
    Shellex,
}

/// <summary>条目状态。</summary>
public enum MenuEntryStatus
{
    Enabled,
    DisabledLegacy,   // 有 LegacyDisable（shell 项被禁用）
    ShiftOnly,        // Extended：按住 Shift 才显示
    Blocked,          // shellex 被 Shell Extensions\Blocked 黑名单拦截
}

/// <summary>一个右键菜单条目（一个注册表注册点；同一软件可在多个场景各有一条）。</summary>
public sealed class MenuEntry
{
    /// <summary>相对 HKCR 的注册表路径，如 Directory\Background\shell\Git Bash Here。</summary>
    public required string RegistryPath { get; init; }

    /// <summary>实际所在 hive 根：HKLM\Software\Classes 或 HKCU\Software\Classes。</summary>
    public required string HiveRoot { get; init; }

    /// <summary>注册键名（verb）。</summary>
    public required string VerbName { get; init; }

    /// <summary>显示标题（MUIVerb / 默认值 / 键名）。</summary>
    public required string Title { get; init; }

    /// <summary>shell 项的命令行。</summary>
    public string? Command { get; init; }

    /// <summary>提取出的 exe/dll 路径（残留检测与来源推断）。</summary>
    public string? TargetPath { get; init; }

    public MenuEntryKind Kind { get; init; }

    /// <summary>状态（禁用/启用后由 VM 就地更新，避免整表重建）。</summary>
    public MenuEntryStatus Status { get; set; }
    public MenuSceneKind Scene { get; init; }

    /// <summary>场景显示名（“空白处” / “.txt” / “任意文件”…）。</summary>
    public required string SceneLabel { get; init; }

    /// <summary>来源软件（文件 CompanyName 推断；null=未知）。</summary>
    public string? Publisher { get; init; }

    /// <summary>残留：目标文件不存在（软件卸载未清理）。</summary>
    public bool IsResidual { get; init; }

    /// <summary>系统/Microsoft 项（保护：不提供删除）。</summary>
    public bool IsSystem { get; init; }

    /// <summary>shellex 的 CLSID（禁用黑名单用）。</summary>
    public string? Clsid { get; init; }

    public bool IsEnabled => Status is MenuEntryStatus.Enabled or MenuEntryStatus.ShiftOnly;

    /// <summary>来源分组名。</summary>
    public string SourceName => IsSystem ? "Microsoft / 系统"
        : Publisher ?? (TargetPath is { } t ? TryPublisherFromPath(t) : null) ?? "未知来源";

    private static string TryPublisherFromPath(string path) =>
        Path.GetFileName(Path.GetDirectoryName(path)!) is { Length: > 0 } dir ? $"（{dir}）" : "未知来源";
}
