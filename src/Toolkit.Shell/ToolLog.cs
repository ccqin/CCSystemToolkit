namespace Toolkit.Shell;

/// <summary>日志级别：由消息前缀约定解析（✔ 成功 / ✘ 失败），或显式指定。</summary>
public enum ToolLogLevel
{
    Info,
    Success,
    Error,
}

/// <summary>统一日志条目：所有工具经 IToolHost.Log 归集到宿主主页。</summary>
public sealed class ToolLogEntry
{
    public DateTime Time { get; init; } = DateTime.Now;

    /// <summary>来源：工具显示名（如“路由管理”）或“系统”（宿主自身动作）。</summary>
    public required string Source { get; init; }

    public required string Message { get; init; }

    public ToolLogLevel Level { get; init; } = ToolLogLevel.Info;

    /// <summary>展示用短时间（HH:mm:ss）。</summary>
    public string TimeText => Time.ToString("HH:mm:ss");

    /// <summary>级别名（日志列表样式选择器按 Tag 匹配着色用）。</summary>
    public string LevelText => Level.ToString();
}
