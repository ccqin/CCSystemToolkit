namespace Toolkit.Shell.Mcp;

/// <summary>
/// MCP 服务注册表：各工具把自身服务定义（<see cref="IMcpServiceDefinition"/>）注册进来，
/// 宿主「MCP 服务」设置弹窗统一管理。静态注册，进程级一份。
/// </summary>
public static class McpServiceRegistry
{
    private static readonly object _sync = new();
    private static readonly List<IMcpServiceDefinition> _services = [];

    /// <summary>注册服务（按 Id 幂等，重复注册只保留首个）。</summary>
    public static void Register(IMcpServiceDefinition service)
    {
        lock (_sync)
        {
            if (_services.Any(s => s.Id == service.Id)) return;
            _services.Add(service);
        }
    }

    /// <summary>已注册的全部服务（快照）。</summary>
    public static IReadOnlyList<IMcpServiceDefinition> All
    {
        get { lock (_sync) return _services.ToArray(); }
    }

    public static IMcpServiceDefinition? Find(string id)
    {
        lock (_sync) return _services.FirstOrDefault(s => s.Id == id);
    }
}
