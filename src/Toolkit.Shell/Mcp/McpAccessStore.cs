using System.IO;
using System.Security.Cryptography;
using System.Text.Json;

namespace Toolkit.Shell.Mcp;

/// <summary>一个 AI 客户端的接入授权项：名称 + 令牌（接入 URL 以 ?token= 携带），可停用/删除（立即失效）。</summary>
public sealed class McpAccessItem
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string Name { get; set; } = "";
    public string Token { get; set; } = "";
    public bool Enabled { get; set; } = true;
    public DateTime CreatedAt { get; set; } = DateTime.Now;
    public DateTime? LastUsedAt { get; set; }
    public long CallCount { get; set; }
}

/// <summary>持久化文件结构（接入项 + 各 MCP 服务的记忆端口）。</summary>
internal sealed class McpAccessFile
{
    public List<McpAccessItem> Items { get; set; } = [];
    public Dictionary<string, int> Ports { get; set; } = [];
}

/// <summary>
/// MCP 接入授权存储（进程级一份）：%LOCALAPPDATA%\SystemToolkit\mcp-access.json。
/// AI 客户端以「服务 URL + ?token=令牌」访问已启动的 MCP 服务；令牌停用或删除后立即失效。
/// 宿主「MCP 服务」设置弹窗与各工具内嵌的 MCP 服务器共用同一份数据（都走本静态类）。
/// </summary>
public static class McpAccessStore
{
    private static readonly object _sync = new();
    private static readonly JsonSerializerOptions _jsonOpts = new() { WriteIndented = true };
    private static List<McpAccessItem> _items = [];
    private static Dictionary<string, int> _ports = [];
    private static bool _dirty;

    private static string BaseDir =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "SystemToolkit");

    private static string StorePath => Path.Combine(BaseDir, "mcp-access.json");

    /// <summary>接入项增删/启停时触发（UI 刷新；调用统计变化不触发）。</summary>
    public static event Action? Changed;

    static McpAccessStore()
    {
        try
        {
            if (File.Exists(StorePath))
            {
                if (JsonSerializer.Deserialize<McpAccessFile>(File.ReadAllText(StorePath)) is { } data)
                {
                    _items = data.Items;
                    _ports = data.Ports;
                }
            }
        }
        catch
        { /* 文件损坏时回退空配置，首次访问令牌前重新生成即可 */ }

        if (_items.Count == 0)
        {
            // 首次使用自动建一个默认接入项，开箱即用
            _items.Add(new McpAccessItem { Name = "默认接入", Token = NewToken() });
            _dirty = true;
            Save();
        }
    }

    /// <summary>全部接入项快照。</summary>
    public static IReadOnlyList<McpAccessItem> Items
    {
        get { lock (_sync) return _items.ToArray(); }
    }

    /// <summary>新增接入项（自动生成 32 位随机令牌）。</summary>
    public static McpAccessItem AddItem(string name)
    {
        var item = new McpAccessItem
        {
            Name = string.IsNullOrWhiteSpace(name) ? "未命名客户端" : name.Trim(),
            Token = NewToken(),
        };
        lock (_sync)
        {
            _items.Add(item);
            _dirty = true;
        }
        Save();
        Changed?.Invoke();
        return item;
    }

    /// <summary>删除接入项（其令牌立即失效）。</summary>
    public static void RemoveItem(string id)
    {
        lock (_sync)
        {
            if (_items.RemoveAll(i => i.Id == id) == 0) return;
            _dirty = true;
        }
        Save();
        Changed?.Invoke();
    }

    /// <summary>启用/停用接入项（停用后令牌立即失效，重新启用恢复）。</summary>
    public static void SetEnabled(string id, bool enabled)
    {
        lock (_sync)
        {
            if (_items.FirstOrDefault(i => i.Id == id) is not { } item || item.Enabled == enabled) return;
            item.Enabled = enabled;
            _dirty = true;
        }
        Save();
        Changed?.Invoke();
    }

    /// <summary>校验接入令牌；有效则记录调用统计（时间 + 次数）并返回 true。</summary>
    public static bool Validate(string token)
    {
        lock (_sync)
        {
            if (_items.FirstOrDefault(i =>
                i.Enabled && string.Equals(i.Token, token, StringComparison.Ordinal)) is not { } item)
                return false;
            item.LastUsedAt = DateTime.Now;
            item.CallCount++;
            _dirty = true;
        }
        Save();
        return true;
    }

    /// <summary>读取某服务的记忆端口（无效或未配置时返回默认端口）。</summary>
    public static int GetServicePort(string serviceId, int defaultPort)
    {
        lock (_sync)
            return _ports.TryGetValue(serviceId, out var p) && p is > 0 and < 65536 ? p : defaultPort;
    }

    /// <summary>记住某服务的端口（下次启动沿用）。</summary>
    public static void SetServicePort(string serviceId, int port)
    {
        lock (_sync)
        {
            _ports[serviceId] = port;
            _dirty = true;
        }
        Save();
    }

    private static string NewToken() =>
        Convert.ToHexString(RandomNumberGenerator.GetBytes(16)).ToLowerInvariant();

    private static void Save()
    {
        lock (_sync)
        {
            if (!_dirty) return;
            try
            {
                Directory.CreateDirectory(BaseDir);
                File.WriteAllText(StorePath, JsonSerializer.Serialize(
                    new McpAccessFile { Items = _items, Ports = _ports }, _jsonOpts));
                _dirty = false;
            }
            catch
            { /* 写盘失败不影响运行期校验（内存数据仍有效） */ }
        }
    }
}
