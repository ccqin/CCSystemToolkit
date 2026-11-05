namespace StartupMgr.Core.Models;

/// <summary>计划任务条目（TaskCache 索引 + XML 定义解析）。</summary>
public sealed class TaskEntry
{
    /// <summary>任务路径（如 \Microsoft\Windows\UpdateOrchestrator\... 或 \QuarkCloudDrive）。</summary>
    public required string Path { get; init; }

    /// <summary>显示名（路径末段）。</summary>
    public string Name => System.IO.Path.GetFileName(Path.TrimEnd('\\'));

    /// <summary>触发器类型标签集合（登录/开机/定时/事件/空闲/会话…）。</summary>
    public IReadOnlyList<string> Triggers { get; init; } = [];

    /// <summary>是否登录触发（自启动语义）。</summary>
    public bool IsLogonTrigger => Triggers.Contains("登录");

    /// <summary>动作程序（Exec 的 Command；ComHandler 任务为 null）。</summary>
    public string? Command { get; init; }

    public string? Arguments { get; init; }

    /// <summary>状态（3=已禁用；切换后由 VM 就地更新）。</summary>
    public int State { get; set; }

    /// <summary>上次运行时间/下次运行时间（TaskCache）。</summary>
    public string LastRunText { get; init; } = "";
    public string NextRunText { get; init; } = "";

    public string? TargetPath { get; init; }
    public string? Publisher { get; init; }
    public bool IsResidual { get; init; }
    public bool IsSystem { get; init; }

    public bool IsEnabled => State != 3;

    public string StateText => State switch
    {
        0 => "就绪",
        1 => "运行中",
        2 => "排队",
        3 => "已禁用",
        _ => $"状态{State}",
    };

    public string TriggersText => Triggers.Count == 0 ? "未知" : string.Join("+", Triggers);
    public string BadgeText => IsResidual ? "⚠ 残留" : IsSystem ? "✔ 系统" : !IsEnabled ? "⊘ 禁用" : "";
    public string SourceName => IsSystem ? "Microsoft / 系统" : Publisher ?? "未知来源";
}
