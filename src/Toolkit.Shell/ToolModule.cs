using Avalonia.Controls;

namespace Toolkit.Shell;

/// <summary>
/// 工具模块基类：系统管理工具集中的每个工具实现一个子类，
/// 由宿主在启动时注册到工具清单并在主页展示为卡片。
/// </summary>
public abstract class ToolModule
{
    /// <summary>宿主能力注入（提权、全局状态等）。</summary>
    public required IToolHost Host { get; init; }

    /// <summary>工具唯一标识（稳定字符串，用于将来记忆状态等）。</summary>
    public abstract string Id { get; }

    /// <summary>工具显示名（主页卡片与进入后的标题）。</summary>
    public abstract string Title { get; }

    /// <summary>卡片图标（Unicode 字符，如 🌐）。</summary>
    public abstract string Glyph { get; }

    /// <summary>卡片上的一句话功能描述。</summary>
    public abstract string Description { get; }

    /// <summary>
    /// 创建工具根视图。每次从主页进入工具时调用，
    /// 返回的实例由宿主放入主窗口内容区（DataContext 由模块自行配对）。
    /// </summary>
    public abstract UserControl CreateView();

    /// <summary>进入工具后的一次性激活钩子（默认空）。</summary>
    public virtual void OnEnter() { }
}
