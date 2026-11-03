using System.Security.AccessControl;

namespace PermDoctor.Core.Models;

/// <summary>ACL 条目展示模型（一行 = 一条 FileSystemAccessRule）。</summary>
public sealed class AclEntryView
{
    /// <summary>身份名称（用户/组；孤儿 SID 显示原始 SID 并标注）。</summary>
    public required string Identity { get; init; }

    /// <summary>SID（孤儿检测与排查用）。</summary>
    public required string Sid { get; init; }

    /// <summary>是否为本机无法映射的孤儿 SID（常见于从旧系统/其他电脑拷来的目录）。</summary>
    public bool IsOrphan { get; init; }

    /// <summary>权限友好文本（如“修改” “读取和执行”）。</summary>
    public required string RightsText { get; init; }

    /// <summary>原始权限位（FileSystemRights）。</summary>
    public FileSystemRights Rights { get; init; }

    /// <summary>允许 / 拒绝。</summary>
    public required string TypeText { get; init; }

    /// <summary>应用于（此文件夹 / 及子文件夹和文件 …）。</summary>
    public required string AppliesTo { get; init; }

    /// <summary>是否继承自父目录。</summary>
    public bool IsInherited { get; init; }

    /// <summary>是否匹配当前用户（本人或所属组）。</summary>
    public bool AppliesToCurrentUser { get; init; }

    /// <summary>规则是否作用于本文件夹（false = 仅继承到子项的 IO 规则）。</summary>
    public bool AppliesToSelf { get; init; }

    /// <summary>行标记文本（"✔ 你的权限" / "⚠ 孤儿 SID" / ""），ACL 表首列展示。</summary>
    public string BadgeText => IsOrphan ? "⚠ 孤儿 SID" : AppliesToCurrentUser ? "✔ 你的权限" : "";

    /// <summary>标记样式 Tag（样式选择器按 orphan/mine 着色）。</summary>
    public string BadgeTag => IsOrphan ? "orphan" : AppliesToCurrentUser ? "mine" : "";

    /// <summary>原始继承标志（删除规则时需精确重建）。</summary>
    public System.Security.AccessControl.InheritanceFlags Inheritance { get; init; }

    /// <summary>原始传播标志。</summary>
    public System.Security.AccessControl.PropagationFlags Propagation { get; init; }

    /// <summary>是否系统关键授权（SYSTEM / Administrators / TrustedInstaller，删除会被阻止，防止锁死目录）。</summary>
    public bool IsSystemCritical =>
        Sid.Equals("S-1-5-18", StringComparison.OrdinalIgnoreCase) ||                        // NT AUTHORITY\SYSTEM
        Sid.StartsWith("S-1-5-32-544", StringComparison.OrdinalIgnoreCase) ||                 // BUILTIN\Administrators
        Identity.Contains("TrustedInstaller", StringComparison.OrdinalIgnoreCase) ||
        Sid.StartsWith("S-1-5-80", StringComparison.OrdinalIgnoreCase);                        // 虚拟账户（服务 SID，如 TrustedInstaller）
}

/// <summary>写入实测结果。</summary>
public enum WriteTestResult
{
    NotRun,
    Ok,             // 建目录+删目录都成功
    CreateDenied,   // 创建被拒（安装“错误 5”的直接原因）
    CleanupDenied,  // 能建但删不掉
    Error,          // 其他异常
}

/// <summary>完整诊断报告。</summary>
public sealed class DiagnosisReport
{
    public required string Path { get; init; }
    public bool Exists { get; init; }
    public string? Owner { get; init; }
    public bool InheritanceEnabled { get; init; }

    /// <summary>当前用户对“此文件夹”的有效权限位（含所属组；Deny 优先扣除）。</summary>
    public FileSystemRights EffectiveRights { get; init; }

    /// <summary>有效权限友好文本。</summary>
    public required string EffectiveRightsText { get; init; }

    /// <summary>实测：在目录内创建+删除临时子目录。</summary>
    public WriteTestResult WriteTest { get; init; }
    public string? WriteTestError { get; init; }

    /// <summary>安装/写入场景所需但缺失的权限（红字提示）。</summary>
    public IReadOnlyList<string> MissingRights { get; init; } = [];

    /// <summary>人类可读的问题清单（按严重程度排序）。</summary>
    public IReadOnlyList<string> Problems { get; init; } = [];

    /// <summary>ACL 明细。</summary>
    public IReadOnlyList<AclEntryView> Entries { get; init; } = [];

    /// <summary>等效 icacls / takeown 命令（修复方案，可在管理员终端执行）。</summary>
    public IReadOnlyList<string> FixCommands { get; init; } = [];
}
