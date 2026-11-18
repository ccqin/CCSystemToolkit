using System.Text.Json;
using System.Text.Json.Nodes;

namespace Toolkit.Shell.Mcp;

/// <summary>
/// 工具暴露给 AI 客户端的「能力」定义：由各工具的运行时实现并注册到 <see cref="McpServiceRegistry"/>，
/// 由聚合网关（McpGatewayRuntime，单端口单 URL）统一对外——能力自身不监听端口。
/// </summary>
public interface IMcpServiceDefinition
{
    /// <summary>能力标识（稳定字符串，如 "serial"）。</summary>
    string Id { get; }

    /// <summary>能力显示名（如 "串口 MCP"）。</summary>
    string Name { get; }

    /// <summary>一句话功能描述。</summary>
    string Description { get; }

    /// <summary>能力是否启用（停用后网关的 tools/list 不再包含其工具）。</summary>
    bool Enabled { get; }

    /// <summary>本次运行期间工具调用累计次数。</summary>
    long TotalCalls { get; }

    /// <summary>启用状态/统计变化时触发（UI 刷新）。</summary>
    event Action? StateChanged;

    /// <summary>启用/停用本能力（网关运行状态不受影响）。</summary>
    void SetEnabled(bool enabled);

    /// <summary>本能力的 MCP 工具定义（name/description/inputSchema）。</summary>
    JsonArray BuildTools();

    /// <summary>
    /// 执行一个本能力提供的工具（网关分发调用；name 不属于本能力时抛 InvalidOperationException）。
    /// 抛出的其他异常 = 工具级错误（isError 返回，AI 可恢复）。
    /// </summary>
    string ExecuteTool(string name, JsonElement args);

    /// <summary>工具执行完成的通知（写监控日志用；result 截断过、密码等敏感参数应在日志里脱敏）。</summary>
    void OnToolCalled(string name, JsonElement args, string result, bool isError);
}
