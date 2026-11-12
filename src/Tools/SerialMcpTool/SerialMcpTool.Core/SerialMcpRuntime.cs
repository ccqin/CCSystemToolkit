using Toolkit.Shell.Mcp;

namespace SerialMcpTool.Core;

/// <summary>
/// 串口 MCP 运行时（进程级单例）：持有串口服务与 MCP 服务器。
/// 宿主每次进入工具都会重建视图/VM，本单例保证服务器与串口状态跨视图存活；
/// 并实现 <see cref="IMcpServiceDefinition"/> 注册到宿主「MCP 服务」设置统一管理。
/// </summary>
public sealed class SerialMcpRuntime : IMcpServiceDefinition
{
    private const int MaxHistory = 500;

    private readonly object _historyLock = new();
    private readonly List<SerialTrafficItem> _history = [];
    private McpServer? _server;
    private long _calls;

    private SerialMcpRuntime() => Serial.Traffic += Record;

    public static SerialMcpRuntime Instance { get; } = new();

    /// <summary>串口服务（UI 与 MCP 服务器共用同一实例）。</summary>
    public SerialPortService Serial { get; } = new();

    /// <summary>每次收发/提示触发（UI 线程外，订阅方自行调度）。</summary>
    public event Action<SerialTrafficItem>? Traffic;

    /// <summary>重进工具视图时的历史回放（最近 MaxHistory 条快照）。</summary>
    public SerialTrafficItem[] Snapshot()
    {
        lock (_historyLock) return [.. _history];
    }

    /// <summary>清空监控历史（重进视图后不再回放已清掉的内容）。</summary>
    public void ClearHistory()
    {
        lock (_historyLock) _history.Clear();
    }

    private void Record(SerialTrafficItem item)
    {
        lock (_historyLock)
        {
            _history.Add(item);
            while (_history.Count > MaxHistory) _history.RemoveAt(0);
        }
        Traffic?.Invoke(item);
    }

    // ===== IMcpServiceDefinition =====

    public string Id => "serial";
    public string Name => "串口 MCP";
    public string Description => "AI 客户端通过 MCP 协议打开串口、收发数据（HTTP 传输，仅监听本机回环；需接入令牌）";
    public int DefaultPort => 8642;
    public string EndpointPath => "/mcp";

    public string EndpointUrl =>
        $"http://{(_server?.AdvertisedHost ?? "127.0.0.1")}:{Port}{EndpointPath}";

    public bool IsRunning => _server?.IsRunning == true;

    public int Port => _server?.Port ?? McpAccessStore.GetServicePort(Id, DefaultPort);

    public long TotalCalls => _calls;

    public event Action? StateChanged;

    public void Start(int port)
    {
        if (IsRunning) return;
        if (port <= 0 || port > 65535) port = McpAccessStore.GetServicePort(Id, DefaultPort);
        var server = new McpServer(Serial, port, () => Interlocked.Increment(ref _calls));
        server.Start();
        _server = server;
        McpAccessStore.SetServicePort(Id, port);
        Record(new SerialTrafficItem
        {
            Direction = SerialDirection.Info,
            Message = $"串口 MCP 服务器已启动：{server.EndpointUrl}（接入 URL 需带令牌，在顶栏「MCP 服务」中复制）",
        });
        StateChanged?.Invoke();
    }

    public void Stop()
    {
        if (_server is not { } server) return;
        _server = null;
        server.Stop();
        Record(new SerialTrafficItem { Direction = SerialDirection.Info, Message = "串口 MCP 服务器已停止" });
        StateChanged?.Invoke();
    }
}
