namespace Toolkit.Shell;

/// <summary>
/// 宿主提供给工具模块的能力。
/// 工具模块通过 <see cref="ToolModule.Host"/> 访问宿主，避免工具直接依赖宿主实现。
/// </summary>
public interface IToolHost
{
    /// <summary>当前进程是否以管理员身份运行。</summary>
    bool IsElevated { get; }

    /// <summary>宿主全局状态栏文本。</summary>
    string StatusText { get; set; }

    /// <summary>
    /// 向主页统一日志追加一条记录（所有工具的日志都归集到主页展示）。
    /// source 用工具显示名；消息以 ✔ / ✘ 开头会被识别为成功 / 失败并着色。
    /// </summary>
    void Log(string source, string message);

    /// <summary>
    /// 以管理员身份重启整个宿主程序（UAC 确认后当前实例退出）。
    /// 提权是进程级操作，必须由宿主统一处理。
    /// </summary>
    void ElevateRestart();
}
