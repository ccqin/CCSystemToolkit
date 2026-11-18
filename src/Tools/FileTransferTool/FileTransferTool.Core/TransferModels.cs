using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace FileTransferTool.Core;

/// <summary>传输协议。</summary>
public enum TransferProtocol
{
    /// <summary>FTP（FluentFTP）。</summary>
    Ftp,

    /// <summary>SFTP（SSH.NET，SSH 通道）。</summary>
    Sftp,

    /// <summary>SCP（SSH.NET，SSH 通道）。</summary>
    Scp,
}

/// <summary>传输方向。</summary>
public enum TransferDirection
{
    Upload,
    Download,
}

/// <summary>传输监控行类别（卡片着色用）。</summary>
public enum TransferLogKind
{
    /// <summary>一次成功传输（协议/方向/文件/大小/耗时）。</summary>
    Done,

    /// <summary>失败。</summary>
    Error,

    /// <summary>AI 调用记录（参数摘要）。</summary>
    AiCall,

    /// <summary>系统提示（服务启停等）。</summary>
    Info,
}

/// <summary>一个传输站点（协议 + 连接参数；密码明文存本地 JSON，注意保管）。</summary>
public sealed class TransferSite
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string Name { get; set; } = "";
    public TransferProtocol Protocol { get; set; } = TransferProtocol.Ftp;
    public string Host { get; set; } = "";
    public int Port { get; set; }                       // 0 = 协议默认端口
    public string Username { get; set; } = "";
    public string Password { get; set; } = "";
    /// <summary>SFTP/SCP 私钥路径（设置后优先于密码认证）。</summary>
    public string? KeyPath { get; set; }
    /// <summary>FTP 被动模式（多数防火墙环境需要）。</summary>
    public bool Passive { get; set; } = true;

    public int EffectivePort => Port > 0 ? Port : Protocol switch
    {
        TransferProtocol.Ftp => 21,
        _ => 22,
    };

    public string ProtocolText => Protocol switch
    {
        TransferProtocol.Ftp => "FTP",
        TransferProtocol.Sftp => "SFTP",
        _ => "SCP",
    };
}

/// <summary>传输监控/记录行。</summary>
public sealed class TransferLogItem
{
    public DateTime Time { get; init; } = DateTime.Now;
    public TransferLogKind Kind { get; init; }
    public string Message { get; init; } = "";
    public string TimeText => Time.ToString("HH:mm:ss.fff");
    public string Tag => Kind.ToString();
}

/// <summary>
/// 站点持久化：%LOCALAPPDATA%\SystemToolkit\transfer-sites.json。
/// 静态类：设置弹窗无关，工具 UI 与 MCP 服务器共用同一份数据。
/// </summary>
public static class TransferSiteStore
{
    private static readonly object _sync = new();
    private static readonly JsonSerializerOptions _jsonOpts = new()
    {
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter() },
    };
    private static List<TransferSite> _sites = [];

    /// <summary>站点增删改时触发（UI 刷新）。</summary>
    public static event Action? Changed;

    private static string BaseDir =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "SystemToolkit");

    private static string StorePath => Path.Combine(BaseDir, "transfer-sites.json");

    static TransferSiteStore()
    {
        try
        {
            if (File.Exists(StorePath))
            {
                // 注意必须传 _jsonOpts：写入时枚举是字符串（JsonStringEnumConverter），
                // 用默认选项读字符串枚举会抛异常（曾导致"重启后站点全丢"）
                if (JsonSerializer.Deserialize<List<TransferSite>>(File.ReadAllText(StorePath), _jsonOpts) is { } data)
                {
                    _sites = data;
                }
            }
        }
        catch
        {
            // 文件损坏：备份后从空开始（避免后续保存把原数据永久覆盖）
            try { File.Copy(StorePath, StorePath + ".bad.bak", overwrite: true); } catch { }
        }
    }

    /// <summary>全部站点快照。</summary>
    public static IReadOnlyList<TransferSite> Sites
    {
        get { lock (_sync) return _sites.ToArray(); }
    }

    public static TransferSite? Find(string nameOrId)
    {
        lock (_sync) return _sites.FirstOrDefault(s =>
            string.Equals(s.Id, nameOrId, StringComparison.OrdinalIgnoreCase)
            || string.Equals(s.Name, nameOrId, StringComparison.OrdinalIgnoreCase));
    }

    public static void Add(TransferSite site)
    {
        lock (_sync)
        {
            if (_sites.Any(s => string.Equals(s.Name, site.Name, StringComparison.OrdinalIgnoreCase)
                                && s.Protocol == site.Protocol))
                throw new InvalidOperationException($"站点名「{site.Name}」在该协议下已存在");
            _sites.Add(site);
        }
        Save();
        Changed?.Invoke();
    }

    public static void Update(TransferSite site)
    {
        lock (_sync)
        {
            var old = _sites.FirstOrDefault(s => s.Id == site.Id)
                ?? throw new InvalidOperationException("站点不存在（可能已被删除）");
            old.Name = site.Name;
            old.Protocol = site.Protocol;
            old.Host = site.Host;
            old.Port = site.Port;
            old.Username = site.Username;
            old.Password = site.Password;
            old.KeyPath = site.KeyPath;
            old.Passive = site.Passive;
        }
        Save();
        Changed?.Invoke();
    }

    public static void Remove(string id)
    {
        lock (_sync)
        {
            if (_sites.RemoveAll(s => s.Id == id) == 0) return;
        }
        Save();
        Changed?.Invoke();
    }

    private static void Save()
    {
        lock (_sync)
        {
            try
            {
                Directory.CreateDirectory(BaseDir);
                File.WriteAllText(StorePath, JsonSerializer.Serialize(_sites, _jsonOpts));
            }
            catch (Exception ex)
            {
                // 保存失败必须让调用方看到（否则表现为"站点没保存/丢失"）
                throw new InvalidOperationException($"站点配置写盘失败：{ex.Message}", ex);
            }
        }
    }
}
