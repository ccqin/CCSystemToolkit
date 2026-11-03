namespace NetRouteList.Core.Models;

public enum IssueSeverity
{
    /// <summary>错误：明显影响网络可达性。</summary>
    Error,
    /// <summary>警告：潜在冲突或风险。</summary>
    Warning,
    /// <summary>信息：值得了解的运行状态。</summary>
    Info,
}

/// <summary>总览页的一条诊断结论。</summary>
public sealed class DiagnosticIssue
{
    public required IssueSeverity Severity { get; init; }
    public required string Title { get; init; }

    /// <summary>通俗解释：这个问题是什么、为什么重要、会造成什么后果。</summary>
    public required string Explanation { get; init; }

    /// <summary>受影响的具体条目（每条一行文本）。</summary>
    public List<string> Affected { get; init; } = [];

    /// <summary>建议的处理方式（文字描述）。</summary>
    public required string Suggestion { get; init; }

    /// <summary>可选的一键修复动作。</summary>
    public RepairAction? Repair { get; init; }
}

/// <summary>一键修复动作：由诊断结果生成的一组路由操作。</summary>
public sealed class RepairAction
{
    public required string Title { get; init; }

    /// <summary>执行前给用户看的说明（会做什么、有什么风险）。</summary>
    public required string ConfirmText { get; init; }

    /// <summary>确认框上的复选文案（如“作为持久路由添加”）；null 表示无可选项。</summary>
    public string? OptionText { get; init; }

    /// <summary>复选框默认状态。</summary>
    public bool OptionDefault { get; init; }

    /// <summary>按选项状态生成预览命令（勾选状态变化时刷新，例如是否带 -p）。</summary>
    public required Func<bool, IReadOnlyList<string>> Preview { get; init; }

    /// <summary>需要管理员权限。</summary>
    public bool RequiresAdmin { get; init; } = true;

    /// <summary>按选项状态执行修复，返回每步结果。</summary>
    public required Func<bool, IReadOnlyList<RouteOpResult>> Execute { get; init; }
}
