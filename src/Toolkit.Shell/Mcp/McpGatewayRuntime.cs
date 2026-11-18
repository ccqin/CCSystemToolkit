using System.Text.Json;
using System.Text.Json.Nodes;

namespace Toolkit.Shell.Mcp;

/// <summary>
/// MCP 聚合网关（进程级单例）：单端口单 URL 暴露注册表里全部启用能力的工具，
/// AI 客户端只需配置一条接入 URL；以后新增 MCP 能力自动进网关，客户端配置不用改。
/// 能力自身的启停（SetEnabled）只影响其工具是否出现在 tools/list，网关运行状态不受影响。
/// </summary>
public sealed class McpGatewayRuntime
{
    private sealed class GatewayServer : McpHttpServer
    {
        public GatewayServer(int port) : base(port) { }

        protected override string ServerName => "ccsystemtoolkit-mcp";
        protected override string ServerVersion => "1.0.0";

        protected override string Instructions =>
            "系统管理工具集的聚合 MCP 服务（串口、文件传输等能力）。可用工具按前缀区分能力："
            + "serial_* / open·close·send·read_serial_* 等属于串口能力（先 open_serial_port 打开串口）；"
            + "transfer_* / list_transfer_sites 等属于文件传输能力（FTP/SFTP/SCP）。"
            + "某能力的工具不在清单中时，说明该能力被停用或对应服务未就绪，请提示用户在工具集顶栏「MCP 服务」中启用。"
            + "所有操作都会实时显示在工具集的对应卡片中。";

        protected override JsonArray BuildTools()
        {
            var tools = new JsonArray();
            var seen = new HashSet<string>(StringComparer.Ordinal);
            foreach (var service in McpServiceRegistry.All)
            {
                if (!service.Enabled) continue;
                foreach (var node in service.BuildTools())
                {
                    var name = node?["name"]?.GetValue<string>();
                    if (name is null) continue;
                    if (!seen.Add(name))
                        throw new InvalidOperationException($"MCP 工具名冲突：{name}（能力 {service.Id} 与已注册工具重名）");
                    tools.Add(node!.DeepClone());
                }
            }
            return tools;
        }

        protected override string ExecuteTool(string name, JsonElement args)
        {
            // 按"谁声明谁执行"分发：遍历启用能力，声明该工具的执行
            foreach (var service in McpServiceRegistry.All)
            {
                if (!service.Enabled) continue;
                if (service.BuildTools().Any(t => t?["name"]?.GetValue<string>() == name))
                    return service.ExecuteTool(name, args);
            }
            throw new InvalidOperationException($"未知工具：{name}（可能对应能力已停用，请在工具集「MCP 服务」中启用）");
        }

        protected override void OnToolCalled(string name, JsonElement args, string result, bool isError)
        {
            // 通知到拥有该工具的能力（写各自的监控日志）
            foreach (var service in McpServiceRegistry.All)
            {
                if (!service.Enabled) continue;
                if (service.BuildTools().Any(t => t?["name"]?.GetValue<string>() == name))
                {
                    service.OnToolCalled(name, args, result, isError);
                    return;
                }
            }
        }
    }

    private McpGatewayRuntime() { }

    public static McpGatewayRuntime Instance { get; } = new();

    private GatewayServer? _server;

    /// <summary>网关记忆端口用的服务 Id（沿用串口时期的 8642，老客户端配置无需改动）。</summary>
    public const string GatewayId = "gateway";

    public int DefaultPort => 8642;

    public bool IsRunning => _server?.IsRunning == true;

    public int Port => _server?.Port ?? McpAccessStore.GetServicePort(GatewayId, DefaultPort);

    /// <summary>不含令牌的网关端点 URL。</summary>
    public string EndpointUrl =>
        $"http://{(_server?.AdvertisedHost ?? "127.0.0.1")}:{Port}/mcp";

    public long TotalCalls => _server?.TotalCalls ?? 0;

    /// <summary>网关启动/停止时触发（UI 刷新）。</summary>
    public event Action? StateChanged;

    public void Start(int port = 0)
    {
        if (IsRunning) return;
        if (port <= 0 || port > 65535) port = McpAccessStore.GetServicePort(GatewayId, DefaultPort);
        var server = new GatewayServer(port);
        server.Start();
        _server = server;
        McpAccessStore.SetServicePort(GatewayId, port);
        StateChanged?.Invoke();
    }

    public void Stop()
    {
        if (_server is not { } server) return;
        _server = null;
        server.Stop();
        StateChanged?.Invoke();
    }
}
