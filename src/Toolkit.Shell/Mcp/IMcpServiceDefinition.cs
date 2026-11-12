namespace Toolkit.Shell.Mcp;

/// <summary>
/// 工具暴露给外部 AI 客户端的 MCP 服务定义。
/// 由各工具的运行时实现并注册到 <see cref="McpServiceRegistry"/>，
/// 宿主「MCP 服务」设置弹窗统一管理启停/端口，并展示带令牌的接入 URL。
/// </summary>
public interface IMcpServiceDefinition
{
    /// <summary>服务标识（稳定字符串，如 "serial"）。</summary>
    string Id { get; }

    /// <summary>服务显示名（如 "串口 MCP"）。</summary>
    string Name { get; }

    /// <summary>一句话功能描述。</summary>
    string Description { get; }

    /// <summary>默认监听端口。</summary>
    int DefaultPort { get; }

    /// <summary>MCP 端点路径（如 "/mcp"，不含令牌参数）。</summary>
    string EndpointPath { get; }

    /// <summary>当前完整端点 URL（http://127.0.0.1:端口/路径；未运行时按记忆端口拼出）。</summary>
    string EndpointUrl { get; }

    /// <summary>当前是否运行中。</summary>
    bool IsRunning { get; }

    /// <summary>当前监听端口（未运行时为持久化的配置端口）。</summary>
    int Port { get; }

    /// <summary>本次运行期间工具调用累计次数。</summary>
    long TotalCalls { get; }

    /// <summary>启动/停止/端口变化时触发（UI 刷新状态显示）。</summary>
    event Action? StateChanged;

    /// <summary>启动服务（失败抛异常）。port 小于等于 0 表示用记忆端口/默认端口。</summary>
    void Start(int port);

    /// <summary>停止服务。</summary>
    void Stop();
}
